# TCP/IP Chat Application

Basic asynchronous TCP/IP chat app, developed live at https://www.youtube.com/playlist?list=PLIebvSMVr_dehKSoq6vuAW0BGEM6QnDlS

There is no such thing as a "simple" TCP/IP application, but this is pretty close. This repository is a minimal correct implementation.

## Minimal Correct Implementation

Each socket connection is two unidirectional streams of bytes. A minimal correct implementation consists of:

- Message framing, to translate the streams of bytes to streams of messages.
- Error handling:
  - Always Be Reading - this lets you detect errors as quickly as possible.
  - Asynchronous APIs - so we don't need multiple threads per socket.
  - Close both streams for a connection at the same time - so that both sides can detect errors as long as data is still being transferred.
- Keepalives, to detect the half-open scenario.

## Overview of ChatApi Architecture

- `PipelineSocket` - kind of like `NetworkStream` but using System.IO.Pipelines instead of `Stream`. `PipelineSocket` provides two unidirectional pipelines of bytes.
- `ChatConnection`:
  - Serializes chat protocol messages to/from bytes. `ChatConnection` provides two unidirectional channels of messages.
  - Handles keepalives:
    - Periodically sends keepalive messages.
    - Ignores incoming keepalive messages.
  - Handles request/response logic:
    - Has a collection of outstanding requests.
    - Handles response messages by completing their matching request.

## What's Missing

- Security
  - Encryption. This protocol is a plain-text protocol.
  - Server/client authentication.
  - Misbehaving clients/servers. This app assumes correctly formed packets.
  - Denial of service. E.g., really large length prefixes, sending data too slowly, etc.
  - Reflection attacks. E.g., broadcast huge message at a high rate.
- Higher-level state machine. E.g., when connection is lost, retry connection after some timeout.
  - Consider a Desired State pattern: the user indicates the desired state, and your application performs whatever connection and messages are necessary to achieve that state.

## Resources

- [TCP/IP Illustrated, Volume 1](https://www.amazon.com/gp/product/0201633469?ie=UTF8&tag=stepheclearys-20&linkCode=as2&camp=1789&creative=390957&creativeASIN=0201633469)
- [My TCP/IP .NET FAQ](https://blog.stephencleary.com/2009/04/tcpip-net-sockets-faq.html)
- [(Unmanaged) WinSock Programmer's FAQ](https://tangentsoft.net/wskfaq/)

## Tools

- [Process Monitor](https://docs.microsoft.com/en-us/sysinternals/downloads/procmon) to trace socket APIs called by an application.
- [TcpView](https://docs.microsoft.com/en-us/sysinternals/downloads/tcpview) to show the state of TCP/IP sockets.
- [Clumsy](http://jagt.github.io/clumsy/) for simulating network black holes.
- [Wireshark](https://www.wireshark.org/) for monitoring network traffic.
  - [Template for custom TCP protocol extension for Wireshark](https://gist.github.com/StephenCleary/20c1f4a55bc80742f022c764e2fc5bc6) to teach Wireshark how to understand your TCP/IP protocol.