using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ChatApi
{
    internal static class MessageSerialization
    {
        public const int LengthPrefixLength = sizeof(uint);

        private const int MessageTypeLength = sizeof(uint);
        private const int LongStringLengthPrefixLength = sizeof(ushort);
        private const int ShortStringLengthPrefixLength = sizeof(byte);

        public static int GetMessageLengthPrefixValue(IMessage message)
        {
            if (message is ChatMessage chatMessage)
            {
                var textBytesLength = Encoding.UTF8.GetByteCount(chatMessage.Text);
                return MessageTypeLength + LongStringLengthPrefixLength + textBytesLength;
            }
            else if (message is BroadcastMessage broadcastMessage)
            {
                var fromBytesLength = Encoding.UTF8.GetByteCount(broadcastMessage.From);
                var textBytesLength = Encoding.UTF8.GetByteCount(broadcastMessage.Text);
                return MessageTypeLength +
                    ShortStringLengthPrefixLength + fromBytesLength +
                    LongStringLengthPrefixLength + textBytesLength;
            }
            else
            {
                throw new InvalidOperationException("Unknown message type.");
            }
        }

        public static bool TryReadLongString(ref this SequenceReader<byte> sequenceReader,
            [NotNullWhen(true)] out string? value)
        {
            value = null;
            if (!sequenceReader.TryReadBigEndian(out short signedLength))
                return false;
            var length = (ushort)signedLength;

            var bytes = new byte[length];
            if (!sequenceReader.TryCopyTo(bytes))
                return false;

            // Unlike other SequenceReader methods, TryCopyTo does *not* advance the position.
            sequenceReader.Advance(length);

            value = Encoding.UTF8.GetString(bytes);
            return true;
        }

        public static bool TryReadShortString(ref this SequenceReader<byte> sequenceReader,
            [NotNullWhen(true)] out string? value)
        {
            value = null;
            if (!sequenceReader.TryRead(out var length))
                return false;

            var bytes = new byte[length];
            if (!sequenceReader.TryCopyTo(bytes))
                return false;

            // Unlike other SequenceReader methods, TryCopyTo does *not* advance the position.
            sequenceReader.Advance(length);

            value = Encoding.UTF8.GetString(bytes);
            return true;
        }

        public ref struct SpanWriter
        {
            private readonly Span<byte> _span;
            private int _position;

            public SpanWriter(Span<byte> span)
            {
                _span = span;
                _position = 0;
            }

            public int Position => _position;

            public void WriteMessageLengthPrefix(uint value) => WriteUInt32BigEndian(value);
            public void WriteMessageType(uint value) => WriteUInt32BigEndian(value);

            public void WriteUInt32BigEndian(uint value)
            {
                BinaryPrimitives.WriteUInt32BigEndian(_span.Slice(_position, sizeof(uint)), value);
                _position += sizeof(uint);
            }

            public void WriteUInt16BigEndian(ushort value)
            {
                BinaryPrimitives.WriteUInt16BigEndian(_span.Slice(_position, sizeof(ushort)), value);
                _position += sizeof(ushort);
            }

            public void WriteLongString(string value)
            {
                var bytes = Encoding.UTF8.GetBytes(value);
                if (bytes.Length > ushort.MaxValue)
                    throw new InvalidOperationException("Long string field is too big.");
                WriteUInt16BigEndian((ushort)bytes.Length);
                WriteByteArray(bytes);
            }

            public void WriteShortString(string value)
            {
                var bytes = Encoding.UTF8.GetBytes(value);
                if (bytes.Length > byte.MaxValue)
                    throw new InvalidOperationException("Short string field is too big.");
                WriteByte((byte)bytes.Length);
                WriteByteArray(bytes);
            }

            public void WriteByte(byte value) => _span[_position++] = value;

            public void WriteByteArray(ReadOnlySpan<byte> value)
            {
                value.CopyTo(_span.Slice(_position, value.Length));
                _position += value.Length;
            }
        }
    }
}
