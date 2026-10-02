# Polhem.JsonRpc

The JSON-RPC 2.0 types shared by [Polhem.JsonRpc.Server](https://www.nuget.org/packages/Polhem.JsonRpc.Server) and
[Polhem.JsonRpc.Client](https://www.nuget.org/packages/Polhem.JsonRpc.Client): requests, responses, errors, ids, the
standard error codes, the transport abstraction (`IJsonRpcTransport`) and `JsonRpcSerializer`, which reads and
writes messages without reflection.

An application rarely references this package on its own: it comes with the server or the client package. It depends
on nothing but .NET and supports trimming and Native AOT.

Documentation and samples: https://github.com/polhem-dev/polhem-jsonrpc
