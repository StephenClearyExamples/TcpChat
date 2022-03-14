using ChatApi;
using ChatApi.Messages;
using System.IO;
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
    public class ChatConnectionTests
    {
        [Fact]
        public async Task ParseOneMessage()
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
        public async Task ParseTwoMessages()
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

        [Fact]
        public async Task ParseOneMessage_OneByteAtATime()
        {
            var fakeSocket = new FakePipelineSocket();
            var connection = new ChatConnection(fakeSocket, Timeout.InfiniteTimeSpan);

            var message = new ChatMessage("Hi");
            var pipe = new Pipe();
            WriteMessage(message, pipe.Writer);
            await pipe.Writer.FlushAsync();
            pipe.Writer.Complete();

            var memoryStream = new MemoryStream();
            pipe.Reader.AsStream().CopyTo(memoryStream);
            var bytes = memoryStream.ToArray();

            foreach (var value in bytes)
            {
                await fakeSocket.InputWriter.WriteAsync(new[] { value });
                await fakeSocket.InputWriter.FlushAsync();

                await Task.Delay(200);
            }

            var enumerator = connection.InputMessages.GetAsyncEnumerator();
            Assert.True(await enumerator.MoveNextAsync());
            Assert.Equal("Hi", Assert.IsType<ChatMessage>(enumerator.Current).Text);
        }

        [Fact]
        public async Task ParseTwoMessages_FirstBlockHasPartialSecondMessage()
        {
            var fakeSocket = new FakePipelineSocket();
            var connection = new ChatConnection(fakeSocket, Timeout.InfiniteTimeSpan);

            var message = new ChatMessage("Hi");
            var pipe = new Pipe();
            WriteMessage(message, pipe.Writer);
            WriteMessage(message, pipe.Writer);
            await pipe.Writer.FlushAsync();
            pipe.Writer.Complete();

            var memoryStream = new MemoryStream();
            pipe.Reader.AsStream().CopyTo(memoryStream);
            var bytes = memoryStream.ToArray();

            var firstMessageBytes = bytes
                .Take(bytes.Length / 2).ToArray();
            var secondMessageBytes = bytes
                .Skip(bytes.Length / 2).ToArray();
            var secondMessageFirstPartBytes = secondMessageBytes
                .Take(secondMessageBytes.Length / 2).ToArray();
            var secondMessageSecondPartBytes = secondMessageBytes
                .Skip(secondMessageBytes.Length / 2).ToArray();

            var firstByteGroup = firstMessageBytes
                .Concat(secondMessageFirstPartBytes).ToArray();

            await fakeSocket.InputWriter.WriteAsync(firstByteGroup);
            await fakeSocket.InputWriter.FlushAsync();
            await Task.Delay(200);
            await fakeSocket.InputWriter.WriteAsync(secondMessageSecondPartBytes);
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