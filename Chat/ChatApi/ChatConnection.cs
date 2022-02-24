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
            MainTask = Task.WhenAll(pipelineSocket.MainTask,
                PipelineToChannelAsync(),
                ChannelToPipelineAsync());
        }

        public Socket Socket => _pipelineSocket.Socket;
        public Task MainTask { get; }
        public IPEndPoint RemoteEndPoint => _pipelineSocket.RemoteEndPoint;

        public async Task SendMessage(IMessage message)
        {
            await _outputChannel.Writer.WriteAsync(message);
        }

        public IAsyncEnumerable<IMessage> InputMessages => _inputChannel.Reader.ReadAllAsync();

        private async Task ChannelToPipelineAsync()
        {
            await foreach (var message in _outputChannel.Reader.ReadAllAsync())
            {
                if (message is ChatMessage chatMessage)
                {
                    var messageBytes = Encoding.UTF8.GetBytes(chatMessage.Text);
                    var memory = _pipelineSocket.OutputPipe.GetMemory(messageBytes.Length + 8);
                    BinaryPrimitives.WriteUInt32BigEndian(memory.Span, (uint)messageBytes.Length + 4);
                    BinaryPrimitives.WriteUInt32BigEndian(memory.Span.Slice(4), 0);
                    messageBytes.CopyTo(memory.Span.Slice(8));
                    _pipelineSocket.OutputPipe.Advance(messageBytes.Length + 8);
                }
                else
                {
                    throw new InvalidOperationException("Unknown message type.");
                }

                await _pipelineSocket.OutputPipe.FlushAsync();
            }

            _pipelineSocket.OutputPipe.Complete();
        }

        private async Task PipelineToChannelAsync()
        {
            while (true)
            {
                var data = await _pipelineSocket.InputPipe.ReadAsync();

                foreach (var message in ParseMessages(data.Buffer))
                    await _inputChannel.Writer.WriteAsync(message);

                if (data.IsCompleted)
                    break;
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
                    var chatMessageBytes = new byte[lengthPrefix - 4];
                    if (!sequenceReader.TryCopyTo(chatMessageBytes))
                    {
                        _pipelineSocket.InputPipe.AdvanceTo(beginOfMessagePosition, buffer.End);
                        break;
                    }

                    // Unlike other SequenceReader methods, TryCopyTo does *not* advance the position.
                    sequenceReader.Advance(chatMessageBytes.Length);

                    result.Add(new ChatMessage(Encoding.UTF8.GetString(chatMessageBytes)));
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
