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
using static ChatApi.MessageSerialization;

namespace ChatApi
{
    public sealed class ChatConnection
    {
        private readonly PipelineSocket _pipelineSocket;
        private readonly TimeSpan _keepaliveTimeSpan;
        private readonly Channel<IMessage> _inputChannel;
        private readonly Channel<IMessage> _outputChannel;
        private readonly Timer _timer;

        public ChatConnection(PipelineSocket pipelineSocket, TimeSpan keepaliveTimeSpan = default)
        {
            keepaliveTimeSpan = keepaliveTimeSpan == default ? TimeSpan.FromSeconds(5) : keepaliveTimeSpan;

            _pipelineSocket = pipelineSocket;
            _keepaliveTimeSpan = keepaliveTimeSpan;
            _inputChannel = Channel.CreateBounded<IMessage>(4);
            _outputChannel = Channel.CreateBounded<IMessage>(4);
            _timer = new Timer(_ => SendKeepaliveMessage(), null, keepaliveTimeSpan, Timeout.InfiniteTimeSpan);
            PipelineToChannelAsync();
            ChannelToPipelineAsync();
        }

        public Socket Socket => _pipelineSocket.Socket;
        public IPEndPoint RemoteEndPoint => _pipelineSocket.RemoteEndPoint;

        public void Complete() => _outputChannel.Writer.Complete();

        public async Task SendMessageAsync(IMessage message)
        {
            await _outputChannel.Writer.WriteAsync(message);
            _timer.Change(_keepaliveTimeSpan, Timeout.InfiniteTimeSpan);
        }

        public IAsyncEnumerable<IMessage> InputMessages => _inputChannel.Reader.ReadAllAsync();

        private void SendKeepaliveMessage()
        {
            _outputChannel.Writer.TryWrite(new KeepaliveMessage());
            _timer.Change(_keepaliveTimeSpan, Timeout.InfiniteTimeSpan);
        }

        private void Close(Exception? exception = null)
        {
            _timer.Dispose();
            _inputChannel.Writer.TryComplete(exception);
            _pipelineSocket.OutputPipe.CancelPendingFlush();
            _pipelineSocket.OutputPipe.Complete();
        }

        private void WriteMessage(IMessage message)
        {
            if (message is ChatMessage chatMessage)
            {
                var messageLengthPrefixValue = GetMessageLengthPrefixValue(message);
                var memory = _pipelineSocket.OutputPipe.GetMemory(LengthPrefixLength + messageLengthPrefixValue);
                SpanWriter writer = new(memory.Span);
                writer.WriteMessageLengthPrefix((uint)messageLengthPrefixValue);
                writer.WriteMessageType(0);
                writer.WriteLongString(chatMessage.Text);
                _pipelineSocket.OutputPipe.Advance(writer.Position);
            }
            else if (message is BroadcastMessage broadcastMessage)
            {
                var messageLengthPrefixValue = GetMessageLengthPrefixValue(message);
                var memory = _pipelineSocket.OutputPipe.GetMemory(LengthPrefixLength + messageLengthPrefixValue);
                SpanWriter writer = new(memory.Span);
                writer.WriteMessageLengthPrefix((uint)messageLengthPrefixValue);
                writer.WriteMessageType(1);
                writer.WriteShortString(broadcastMessage.From);
                writer.WriteLongString(broadcastMessage.Text);
                _pipelineSocket.OutputPipe.Advance(writer.Position);
            }
            else if (message is KeepaliveMessage)
            {
                var messageLengthPrefixValue = GetMessageLengthPrefixValue(message);
                var memory = _pipelineSocket.OutputPipe.GetMemory(LengthPrefixLength + messageLengthPrefixValue);
                SpanWriter writer = new(memory.Span);
                writer.WriteMessageLengthPrefix((uint)messageLengthPrefixValue);
                writer.WriteMessageType(2);
                _pipelineSocket.OutputPipe.Advance(writer.Position);
            }
            else
            {
                throw new InvalidOperationException("Unknown message type.");
            }
        }

        private async void ChannelToPipelineAsync()
        {
            try
            {
                await foreach (var message in _outputChannel.Reader.ReadAllAsync())
                {
                    WriteMessage(message);

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

                if (sequenceReader.Remaining < lengthPrefix)
                {
                    _pipelineSocket.InputPipe.AdvanceTo(beginOfMessagePosition, buffer.End);
                    break;
                }

                if (!sequenceReader.TryReadBigEndian(out int messageType))
                {
                    _pipelineSocket.InputPipe.AdvanceTo(beginOfMessagePosition, buffer.End);
                    break;
                }

                if (messageType == 0)
                {
                    if (!sequenceReader.TryReadLongString(out var text))
                    {
                        _pipelineSocket.InputPipe.AdvanceTo(beginOfMessagePosition, buffer.End);
                        break;
                    }

                    result.Add(new ChatMessage(text));
                }
                else if (messageType == 1)
                {
                    if (!sequenceReader.TryReadShortString(out var from))
                    {
                        _pipelineSocket.InputPipe.AdvanceTo(beginOfMessagePosition, buffer.End);
                        break;
                    }

                    if (!sequenceReader.TryReadLongString(out var text))
                    {
                        _pipelineSocket.InputPipe.AdvanceTo(beginOfMessagePosition, buffer.End);
                        break;
                    }

                    result.Add(new BroadcastMessage(from, text));
                }
                else
                {
                    // Ignore unknown message types.
                    sequenceReader.Advance(lengthPrefix - MessageTypeLength);
                }

                _pipelineSocket.InputPipe.AdvanceTo(sequenceReader.Position);
            }

            return result;
        }
    }
}
