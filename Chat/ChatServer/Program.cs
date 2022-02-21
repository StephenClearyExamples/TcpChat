// See https://aka.ms/new-console-template for more information
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

    // TODO: fix
    _ = ProcessSocket(connectedSocket);
}

async Task ProcessSocket(Socket socket)
{
    while (true)
    {
        var buffer = new byte[1024];
        var bytesRead = await socket.ReceiveAsync(buffer, SocketFlags.None);
        if (bytesRead == 0) // Graceful close
            break;

        var message = Encoding.UTF8.GetString(buffer, 0, bytesRead);
        Console.WriteLine($"Got message from {socket.RemoteEndPoint}: {message}");
    }
}