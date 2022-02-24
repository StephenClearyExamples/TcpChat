// See https://aka.ms/new-console-template for more information
using ChatApi;
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

async Task ProcessSocket(Socket socket)
{
    var chatConnection = new ChatConnection(new PipelineSocket(socket));
    _ = chatConnection.MainTask; // TODO: discard

    await foreach (var message in chatConnection.InputMessages)
    {
        if (message is ChatMessage chatMessage)
            Console.WriteLine($"Got message from {chatConnection.RemoteEndPoint}: {chatMessage.Text}");
        else
            Console.WriteLine($"Got unknown message from {chatConnection.RemoteEndPoint}.");
    }
}

