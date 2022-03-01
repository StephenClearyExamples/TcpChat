// See https://aka.ms/new-console-template for more information
using ChatApi;
using ChatServer;
using System.Buffers;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Text;

Console.WriteLine("Starting server...");

var connections = new ConnectionCollection();

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
    connections.Add(chatConnection);

    try
    {
        await foreach (var message in chatConnection.InputMessages)
        {
            if (message is ChatMessage chatMessage)
            {
                Console.WriteLine($"Got message from {chatConnection.RemoteEndPoint}: {chatMessage.Text}");

                var currentConnections = connections.CurrentConnections;
                var from = chatConnection.RemoteEndPoint.ToString();
                var tasks = currentConnections
                    .Where(x => x != chatConnection)
                    .Select(connection => connection.SendMessageAsync(new BroadcastMessage(from, chatMessage.Text)));

                try
                {
                    await Task.WhenAll(tasks);
                }
                catch
                {
                    // ignore
                }
            }
            else
                Console.WriteLine($"Got unknown message from {chatConnection.RemoteEndPoint}.");
        }

        Console.WriteLine($"Connection at {chatConnection.RemoteEndPoint} was disconnected.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Exception from {chatConnection.RemoteEndPoint}: [{ex.GetType().Name}] {ex.Message}");
    }
    finally
    {
        connections.Remove(chatConnection);
    }
}
