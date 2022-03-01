using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace ChatApi
{
    public sealed class ChatConnection
    {
        private readonly PipelineSocket _pipelineSocket;
        private readonly Channel<IMessage> _inputChannel;
        private readonly Channel<IMessage> _outputChannel;

        public ChatConnection(PipelineSocket pipelineSocket)
        {
            _pipelineSocket = pipelineSocket;
            _inputChannel = Channel.CreateBounded<IMessage>(4);
            _outputChannel = Channel.CreateBounded<IMessage>(4);
            PipelineToChannelAsync();
            ChannelToPipelineAsync();
        }

        public Socket Socket => _pipelineSocket.Socket;
        public IPEndPoint RemoteEndPoint => _pipelineSocket.RemoteEndPoint;

        public void Complete() => _outputChannel.Writer.Complete();

        public async Task SendMessageAsync(IMessage message)
        {
            await _outputChannel.Writer.WriteAsync(message);
        }

        public IAsyncEnumerable<IMessage> InputMessages => _inputChannel.Reader.ReadAllAsync();

        private void Close(Exception? exception = null)
        {
            _inputChannel.Writer.TryComplete(exception);
            _pipelineSocket.OutputPipe.CancelPendingFlush();
            _pipelineSocket.OutputPipe.Complete();
        }

        private async void ChannelToPipelineAsync()
        {
            try
            {
                await foreach (var message in _outputChannel.Reader.ReadAllAsync())
                {
                    if (message is ChatMessage chatMessage)
                    {
                        var textBytes = Encoding.UTF8.GetBytes(chatMessage.Text);
                        var memory = _pipelineSocket.OutputPipe.GetMemory(4 + 4 + textBytes.Length);
                        BinaryPrimitives.WriteUInt32BigEndian(memory.Span, (uint)textBytes.Length + 4);
                        BinaryPrimitives.WriteUInt32BigEndian(memory.Span.Slice(4), 0);
                        textBytes.CopyTo(memory.Span.Slice(8));
                        _pipelineSocket.OutputPipe.Advance(textBytes.Length + 8);
                    }
                    else if (message is BroadcastMessage broadcastMessage)
                    {
                        var fromBytes = Encoding.UTF8.GetBytes(broadcastMessage.From);
                        if (fromBytes.Length > 255)
                            throw new InvalidOperationException("From field in BroadcastMessage is too big.");
                        var textBytes = Encoding.UTF8.GetBytes(broadcastMessage.Text);
                        var messageLength = 4 + 1 + fromBytes.Length + textBytes.Length;
                        var memory = _pipelineSocket.OutputPipe.GetMemory(4 + messageLength);
                        BinaryPrimitives.WriteUInt32BigEndian(memory.Span, (uint)messageLength);
                        BinaryPrimitives.WriteUInt32BigEndian(memory.Span.Slice(4), 1);
                        memory.Span[8..][0] = (byte)fromBytes.Length;
                        fromBytes.CopyTo(memory.Span.Slice(9));
                        textBytes.CopyTo(memory.Span.Slice(9 + fromBytes.Length));
                        _pipelineSocket.OutputPipe.Advance(4 + messageLength);
                    }
                    else
                    {
                        throw new InvalidOperationException("Unknown message type.");
                    }

                    var flushResult = await _pipelineSocket.OutputPipe.FlushAsync();
                    if (flushResult.IsCanceled)
                        break;
                }

                Close();
            }
            catch (Exception exception)
            {
                Close(exception);
            }
        }

        private async void PipelineToChannelAsync()
        {
            try
            {
                while (true)
                {
                    var data = await _pipelineSocket.InputPipe.ReadAsync();

                    foreach (var message in ParseMessages(data.Buffer))
                        await _inputChannel.Writer.WriteAsync(message);

                    if (data.IsCompleted)
                    {
                        Close();
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Close(ex);
            }
        }

        private IReadOnlyList<IMessage> ParseMessages(ReadOnlySequence<byte> buffer)
        {
            var result = new List<IMessage>();
            var sequenceReader = new SequenceReader<byte>(buffer);

            while (sequenceReader.Remaining != 0)
            {
                var beginOfMessagePosition = sequenceReader.Position;
                if (!sequenceReader.TryReadBigEndian(out int signedLengthPrefix))
                {
                    _pipelineSocket.InputPipe.AdvanceTo(beginOfMessagePosition, buffer.End);
                    break;
                }
                var lengthPrefix = (uint)signedLengthPrefix;
                if (lengthPrefix > _pipelineSocket.MaxMessageSize)
                    throw new InvalidOperationException("Message size too big");

                if (!sequenceReader.TryReadBigEndian(out int messageType))
                {
                    _pipelineSocket.InputPipe.AdvanceTo(beginOfMessagePosition, buffer.End);
                    break;
                }

                if (messageType == 0)
                {
                    var textBytes = new byte[lengthPrefix - 4];
                    if (!sequenceReader.TryCopyTo(textBytes))
                    {
                        _pipelineSocket.InputPipe.AdvanceTo(beginOfMessagePosition, buffer.End);
                        break;
                    }

                    // Unlike other SequenceReader methods, TryCopyTo does *not* advance the position.
                    sequenceReader.Advance(textBytes.Length);

                    result.Add(new ChatMessage(Encoding.UTF8.GetString(textBytes)));
                }
                else if (messageType == 1)
                {
                    if (!sequenceReader.TryRead(out var fromLength))
                    {
                        _pipelineSocket.InputPipe.AdvanceTo(beginOfMessagePosition, buffer.End);
                        break;
                    }

                    var fromBytes = new byte[fromLength];
                    if (!sequenceReader.TryCopyTo(fromBytes))
                    {
                        _pipelineSocket.InputPipe.AdvanceTo(beginOfMessagePosition, buffer.End);
                        break;
                    }
                    else
                    {
                        // Unlike other SequenceReader methods, TryCopyTo does *not* advance the position.
                        sequenceReader.Advance(fromBytes.Length);
                    }

                    var textBytes = new byte[lengthPrefix - 4 - 1 - fromBytes.Length];
                    if (!sequenceReader.TryCopyTo(textBytes))
                    {
                        _pipelineSocket.InputPipe.AdvanceTo(beginOfMessagePosition, buffer.End);
                        break;
                    }
                    else
                    {
                        // Unlike other SequenceReader methods, TryCopyTo does *not* advance the position.
                        sequenceReader.Advance(textBytes.Length);
                    }

                    result.Add(new BroadcastMessage(Encoding.UTF8.GetString(fromBytes),
                        Encoding.UTF8.GetString(textBytes)));
                }
                else
                {
                    throw new InvalidOperationException("Unknown message type");
                }

                _pipelineSocket.InputPipe.AdvanceTo(sequenceReader.Position);
            }

            return result;
        }
    }
}
