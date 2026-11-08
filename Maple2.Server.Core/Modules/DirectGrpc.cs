using Grpc.Net.Client;

namespace Maple2.Server.Core.Modules;

/// <summary>The servers' gRPC clients to each other, which never use a proxy: a proxy that applies to 127.0.0.1 (HTTP_PROXY,
/// ALL_PROXY, a Windows proxy without the local exception) cannot carry plain-http HTTP/2, so a call through it fails (BT-215).</summary>
public static class DirectGrpc {
    // EnableMultipleHttp2Connections as on the handler Grpc.Net.Client makes itself.
    public static SocketsHttpHandler Handler() => new() { UseProxy = false, EnableMultipleHttp2Connections = true };

    public static GrpcChannel Channel(Uri address) => GrpcChannel.ForAddress(address, new GrpcChannelOptions { HttpHandler = Handler(), DisposeHttpClient = true });
}
