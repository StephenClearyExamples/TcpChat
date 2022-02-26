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
        private ChatConnection? _chatConnection;

        public MainWindow()
        {
            InitializeComponent();
        }

        private async void Button_Click(object sender, RoutedEventArgs e)
        {
            var clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            await clientSocket.ConnectAsync("localhost", 33333);

            Log.Text += $"Connected to {clientSocket.RemoteEndPoint}\n";

            _chatConnection = new ChatConnection(new PipelineSocket(clientSocket));
        }

        private async void Button_Click_1(object sender, RoutedEventArgs e)
        {
            if (_chatConnection == null)
            {
                Log.Text += "No connection!\n";
            }
            else
            {
                await _chatConnection.SendMessage(new ChatMessage(chatMessageTextBox.Text));
                Log.Text += $"Sent message: {chatMessageTextBox.Text}\n";
            }
        }

        private void Button_Click_2(object sender, RoutedEventArgs e)
        {
            _chatConnection?.Complete();
        }
    }
}
