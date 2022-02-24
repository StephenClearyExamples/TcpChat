using System.Buffers;
using System.IO.Pipelines;
using System.Net.Sockets;

namespace ChatApi
{
    public sealed class PipelineSocket
    {
        private readonly Pipe _outputPipe;
        private readonly Pipe _inputPipe;

        public PipelineSocket(Socket connectedSocket, uint maxMessageSize = 65536)
        {
            Socket = connectedSocket;
            MaxMessageSize = maxMessageSize;
            _outputPipe = new Pipe();
            _inputPipe = new Pipe(new PipeOptions(pauseWriterThreshold: maxMessageSize + 4));

            MainTask = Task.WhenAll(
                PipelineToSocketAsync(_outputPipe.Reader, Socket),
                SocketToPipelineAsync(Socket, _inputPipe.Writer));
        }

        public Socket Socket { get; }

        public Task MainTask { get; }

        public uint MaxMessageSize { get; }

        public PipeWriter OutputPipe => _outputPipe.Writer;
        public PipeReader InputPipe => _inputPipe.Reader;

        private async Task SocketToPipelineAsync(Socket socket, PipeWriter pipeWriter)
        {
            while (true)
            {
                var buffer = pipeWriter.GetMemory();
                var bytesRead = await socket.ReceiveAsync(buffer, SocketFlags.None);
                if (bytesRead == 0) // Graceful close
                {
                    pipeWriter.Complete();
                    break;
                }

                pipeWriter.Advance(bytesRead);
                await pipeWriter.FlushAsync();
            }
        }

        private async Task PipelineToSocketAsync(PipeReader pipeReader, Socket socket)
        {
            while (true)
            {
                ReadResult result = await pipeReader.ReadAsync();
                ReadOnlySequence<byte> buffer = result.Buffer;

                while (true)
                {
                    var memory = buffer.First;
                    if (memory.IsEmpty)
                        break;
                    var bytesSent = await socket.SendAsync(memory, SocketFlags.None);
                    buffer = buffer.Slice(bytesSent);
                    if (bytesSent != memory.Length)
                        break;
                }

                pipeReader.AdvanceTo(buffer.Start);

                if (result.IsCompleted)
                {
                    socket.Close();
                    break;
                }
            }
        }
    }
}