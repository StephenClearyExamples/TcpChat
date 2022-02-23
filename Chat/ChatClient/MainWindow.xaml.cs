using ChatApi;
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace ChatClient
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private async void Button_Click(object sender, RoutedEventArgs e)
        {
            _clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            await _clientSocket.ConnectAsync("localhost", 33333);

            Log.Text += $"Connected to {_clientSocket.RemoteEndPoint}\n";

            var pipelineSocket = new PipelineSocket(_clientSocket);

            var message = "Hello, server!";
            var messageBytes = Encoding.UTF8.GetBytes(message);
            var memory = pipelineSocket.OutputPipe.GetMemory(messageBytes.Length + 8);
            BinaryPrimitives.WriteUInt32BigEndian(memory.Span, (uint)messageBytes.Length + 4);
            BinaryPrimitives.WriteUInt32BigEndian(memory.Span.Slice(4), 0);
            messageBytes.CopyTo(memory.Span.Slice(8));
            pipelineSocket.OutputPipe.Advance(messageBytes.Length + 8);
            await pipelineSocket.OutputPipe.FlushAsync();
        }

        private Socket? _clientSocket;
    }
}
