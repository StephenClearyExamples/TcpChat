# Chat protocol specification v0.4

Server listens on port 33333. Each stream is a sequence of messages.

## Length prefixing

Every message starts with a 4-byte unsigned big-endian length prefix. This length is the length of the message, not including the length prefix.

## Message type

The first 4 bytes of every message is the message type, as a 4-byte big-endian unsigned integer.

## Message definitions

Any unknown message types should be ignored.

### Type 0: Chat message (Client to Server)

The payload of the message has a single field:

1. `Text` - a long string, representing the chat text the client sends to the server.

### Type 1: Broadcast message (Server to Client)

The payload of the message has two fields:

1. `From` - a short string, representing the source client of the message.
2. `Text` - a long string, representing the chat text from the source client.

### Type 2: Keepalive (bidirectional)

No payload. The message length prefix is always 4.

When received, this message should be ignored.

## Types

### Short String

A length-prefixed UTF-8 encoded string where the length prefix is a single-byte unsigned integer.

### Long String

A length-prefixed UTF-8 encoded string where the length prefix is a two-byte big-endian unsigned integer.

## Changelog

* v0.4 Added keepalive message
* v0.3 Changed text fields to be long strings
* v0.2 Added Broadcast message (type 1)
* v0.1 Initial incomplete version