// See https://aka.ms/new-console-template for more information
using System.Buffers;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Text;

Console.WriteLine("Starting server...");

var listeningSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
listeningSocket.Bind(new IPEndPoint(IPAddress.Any, 33333));
listeningSocket.Listen();

Console.WriteLine("Listening...");

while (true)
{
    var connectedSocket = await listeningSocket.AcceptAsync();
    Console.WriteLine($"Got a connection from {connectedSocket.RemoteEndPoint} to {connectedSocket.LocalEndPoint}.");

    // TODO: fix discard
    _ = ProcessSocket(connectedSocket);
}

const uint MaxMessageSize = 65536;

async Task ProcessSocket(Socket socket)
{
    var pipe = new Pipe();
    var socketToPipelineTask = SocketToPipelineAsync(socket, pipe.Writer);
    var handlePipelineTask = HandlePipelineAsync(pipe.Reader);
    await Task.WhenAll(socketToPipelineTask, handlePipelineTask);

    async Task HandlePipelineAsync(PipeReader pipeReader)
    {
        while (true)
        {
            var data = await pipeReader.ReadAsync();
            
            foreach (var message in ParseMessages(data.Buffer, pipeReader))
            {
                Console.WriteLine($"Got message from {socket.RemoteEndPoint}: {message}");
            }

            if (data.IsCompleted)
                break;
        }

    }

    async Task SocketToPipelineAsync(Socket socket, PipeWriter pipeWriter)
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
}

IReadOnlyList<string> ParseMessages(ReadOnlySequence<byte> buffer, PipeReader pipeReader)
{
    var result = new List<string>();
    var sequenceReader = new SequenceReader<byte>(buffer);

    while (true)
    {
        if (!sequenceReader.TryReadBigEndian(out int signedLengthPrefix))
        {
            pipeReader.AdvanceTo(buffer.Start, buffer.End);
            break;
        }
        var lengthPrefix = (uint)signedLengthPrefix;
        if (lengthPrefix > MaxMessageSize)
            throw new InvalidOperationException("Message size too big");

        if (!sequenceReader.TryReadBigEndian(out int messageType))
        {
            pipeReader.AdvanceTo(buffer.Start, buffer.End);
            break;
        }

        if (messageType == 0)
        {
            var chatMessageBytes = new byte[lengthPrefix - 4];
            if (!sequenceReader.TryCopyTo(chatMessageBytes))
            {
                // TODO: Ensure pipeline has enough room for MaxMessageSize bytes.
                pipeReader.AdvanceTo(buffer.Start, buffer.End);
                break;
            }

            pipeReader.AdvanceTo(sequenceReader.Position);
            result.Add(Encoding.UTF8.GetString(chatMessageBytes));
        }
        else
        {
            throw new InvalidOperationException("Unknown message type");
        }
    }

    return result;
}