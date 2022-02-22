# Chat protocol specification

Server listens on port 33333. Each stream is a sequence of messages.

## Length prefixing

Every message starts with a 4-byte unsigned big-endian length prefix. This length is the length of the message, not including the length prefix.

## Message type

The first 4 bytes of every message is the message type, as a 4-byte big-endian unsigned integer.

## Message definitions

### Type 0: Chat message

The payload of the message is a UTF-8 encoded string.

