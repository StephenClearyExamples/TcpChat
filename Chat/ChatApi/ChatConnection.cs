using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace ChatApi
{
    public sealed class ChatConnection
    {
        private readonly PipelineSocket _pipelineSocket;
        private readonly uint _maxMessageSize;

        public ChatConnection(PipelineSocket pipelineSocket, uint maxMessageSize = 65536)
        {
            _pipelineSocket = pipelineSocket;
            _maxMessageSize = maxMessageSize;
            MainTask = Task.WhenAll(pipelineSocket.MainTask, HandlePipelineAsync());
        }

        public Socket Socket => _pipelineSocket.Socket;
        public Task MainTask { get; }

        public async Task SendMessage(IMessage message)
        {
            // TODO: currently not thread safe!
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

        private async Task HandlePipelineAsync()
        {
            while (true)
            {
                var data = await _pipelineSocket.InputPipe.ReadAsync();

                foreach (var message in ParseMessages(data.Buffer))
                {
                    // TODO: remove hardcoded processing!
                    if (message is ChatMessage chatMessage)
                        Console.WriteLine($"Got message from {Socket.RemoteEndPoint}: {chatMessage.Text}");
                    else
                        Console.WriteLine($"Got unknown message from {Socket.RemoteEndPoint}.");
                }

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
                if (lengthPrefix > _maxMessageSize)
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
                        // TODO: Ensure pipeline has enough room for MaxMessageSize bytes.
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
