using ChatApi;
using ChatApi.Messages;
using System.IO.Pipelines;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using static ChatApi.Internals.MessageSerialization;

namespace UnitTests
{
    public class UnitTest1
    {
        [Fact]
        public async Task SimpleMessage()
        {
            var fakeSocket = new FakePipelineSocket();
            var connection = new ChatConnection(fakeSocket, Timeout.InfiniteTimeSpan);

            var message = new ChatMessage("Hi");
            WriteMessage(message, fakeSocket.InputWriter);
            await fakeSocket.InputWriter.FlushAsync();

            var enumerator = connection.InputMessages.GetAsyncEnumerator();
            Assert.True(await enumerator.MoveNextAsync());
            Assert.Equal("Hi", Assert.IsType<ChatMessage>(enumerator.Current).Text);
        }

        [Fact]
        public async Task TwoMessages()
        {
            var fakeSocket = new FakePipelineSocket();
            var connection = new ChatConnection(fakeSocket, Timeout.InfiniteTimeSpan);

            var message = new ChatMessage("Hi");
            WriteMessage(message, fakeSocket.InputWriter);
            WriteMessage(message, fakeSocket.InputWriter);
            await fakeSocket.InputWriter.FlushAsync();

            var enumerator = connection.InputMessages.GetAsyncEnumerator();
            Assert.True(await enumerator.MoveNextAsync());
            Assert.Equal("Hi", Assert.IsType<ChatMessage>(enumerator.Current).Text);
            Assert.True(await enumerator.MoveNextAsync());
            Assert.Equal("Hi", Assert.IsType<ChatMessage>(enumerator.Current).Text);
        }

        private sealed class FakePipelineSocket : IPipelineSocket
        {
            private readonly Pipe _input = new();
            private readonly Pipe _output = new();

            Socket IPipelineSocket.Socket => null!;
            uint IPipelineSocket.MaxMessageSize => ushort.MaxValue;
            IPEndPoint IPipelineSocket.RemoteEndPoint => new(IPAddress.Loopback, 0);
            PipeReader IDuplexPipe.Input => _input.Reader;
            PipeWriter IDuplexPipe.Output => _output.Writer;

            public PipeWriter InputWriter => _input.Writer;
            public PipeReader OutputReader => _output.Reader;
        }
    }
}