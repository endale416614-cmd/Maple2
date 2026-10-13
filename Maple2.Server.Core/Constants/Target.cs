using System.Net;

namespace Maple2.Server.Core.Constants;

public static class Target {
    public const string SERVER_NAME = "Paperwood";
    public const string LOCALE = "NA";

    public static readonly IPAddress LoginIp = IPAddress.Loopback;
    public static readonly ushort LoginPort = 20001;

    public static readonly ushort GrpcLoginPort = 21000;

    public static readonly IPAddress GameIp = IPAddress.Loopback;
    public static readonly ushort BaseGamePort = 20002;

    public static readonly string GrpcGameIp = IPAddress.Loopback.ToString();

    public static readonly Uri WebUri = new("http://localhost");

    public static readonly IPAddress GrpcWorldIp = IPAddress.Loopback;
    public static readonly ushort GrpcWorldPort = 21001;
    public static readonly Uri GrpcWorldUri = new($"http://{GrpcWorldIp}:{GrpcWorldPort}");

    public static readonly ushort BaseGrpcChannelPort = 21002;

    public static readonly bool InstancedContent = false;

    // Address every server socket listens on. Loopback keeps the servers reachable from this PC only;
    // set BIND_IP=0.0.0.0 to host for other machines.
    public static readonly IPAddress BindIp = IPAddress.Loopback;

    static Target() {
        if (IPAddress.TryParse(Environment.GetEnvironmentVariable("BIND_IP"), out IPAddress? bindIpAddress)) {
            BindIp = bindIpAddress;
        }

        if (IPAddress.TryParse(Environment.GetEnvironmentVariable("LOGIN_IP"), out IPAddress? loginIpAddress)) {
            LoginIp = loginIpAddress;
        }
        if (ushort.TryParse(Environment.GetEnvironmentVariable("LOGIN_PORT"), out ushort loginPortOverride)) {
            LoginPort = loginPortOverride;
        }
        // Port overrides let a second server set (test servers) run next to the player's own on this PC.
        if (ushort.TryParse(Environment.GetEnvironmentVariable("GRPC_LOGIN_PORT"), out ushort grpcLoginPortOverride)) {
            GrpcLoginPort = grpcLoginPortOverride;
        }
        if (ushort.TryParse(Environment.GetEnvironmentVariable("GAME_PORT"), out ushort gamePortOverride)) {
            BaseGamePort = gamePortOverride;
        }
        if (ushort.TryParse(Environment.GetEnvironmentVariable("GRPC_CHANNEL_PORT"), out ushort grpcChannelPortOverride)) {
            BaseGrpcChannelPort = grpcChannelPortOverride;
        }

        if (IPAddress.TryParse(Environment.GetEnvironmentVariable("GAME_IP"), out IPAddress? gameIpAddress)) {
            GameIp = gameIpAddress;
        }

        string? grpcGameIpEnv = Environment.GetEnvironmentVariable("GRPC_GAME_IP");
        if (IPAddress.TryParse(grpcGameIpEnv, out IPAddress? grpcGameIpAddress)) {
            GrpcGameIp = grpcGameIpAddress.ToString();
        }

        string? grpcWorldIpEnv = Environment.GetEnvironmentVariable("GRPC_WORLD_IP");
        if (IPAddress.TryParse(grpcWorldIpEnv, out IPAddress? grpcWorldIpOverride)) {
            GrpcWorldIp = grpcWorldIpOverride;
        }

        if (ushort.TryParse(Environment.GetEnvironmentVariable("GRPC_WORLD_PORT"), out ushort grpcWorldPortOverride)) {
            GrpcWorldPort = grpcWorldPortOverride;
        }

        GrpcWorldUri = new Uri($"http://{GrpcWorldIp}:{GrpcWorldPort}");

        bool isDocker = Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true";
        if (isDocker) {
            grpcWorldIpEnv ??= IPAddress.Loopback.ToString();
            GrpcWorldUri = new Uri($"http://{grpcWorldIpEnv}:{GrpcWorldPort}");
            if (!string.IsNullOrEmpty(grpcGameIpEnv)) {
                GrpcGameIp = grpcGameIpEnv;
            }
        }

        string webIP = Environment.GetEnvironmentVariable("WEB_IP") ?? "localhost";
        string webPort = Environment.GetEnvironmentVariable("WEB_PORT") ?? "4000";

        if (Uri.TryCreate($"http://{webIP}:{webPort}", UriKind.Absolute, out Uri? webUriOverride)) {
            WebUri = webUriOverride;
        }

        if (bool.TryParse(Environment.GetEnvironmentVariable("INSTANCED_CONTENT"), out bool instancedOverride)) {
            InstancedContent = instancedOverride;
        }

        // Offline single-player edition: every socket stays on this PC whatever the environment says
        // (an inherited BIND_IP=0.0.0.0 or *_IP override must not expose the servers).
        if (string.Equals(Environment.GetEnvironmentVariable("OFFLINE_MODE"), "true", StringComparison.OrdinalIgnoreCase)) {
            ForcedToLoopback = !IPAddress.IsLoopback(BindIp) || !IPAddress.IsLoopback(LoginIp) || !IPAddress.IsLoopback(GameIp)
                               || !IPAddress.IsLoopback(GrpcWorldIp) || GrpcGameIp != IPAddress.Loopback.ToString() || !WebUri.IsLoopback;
            BindIp = IPAddress.Loopback;
            LoginIp = IPAddress.Loopback;
            GameIp = IPAddress.Loopback;
            GrpcGameIp = IPAddress.Loopback.ToString();
            GrpcWorldIp = IPAddress.Loopback;
            GrpcWorldUri = new Uri($"http://{GrpcWorldIp}:{GrpcWorldPort}");
            WebUri = new Uri($"http://{IPAddress.Loopback}:{WebUri.Port}");
        }
    }

    /// <summary>True when OFFLINE_MODE overrode a non-loopback address from the environment (logged at start-up).</summary>
    public static readonly bool ForcedToLoopback;

    /// <summary>Whether every listen and advertised address is on this PC.</summary>
    public static bool AllLoopback => IPAddress.IsLoopback(BindIp) && IPAddress.IsLoopback(LoginIp) && IPAddress.IsLoopback(GameIp)
                                      && IPAddress.IsLoopback(GrpcWorldIp) && GrpcGameIp == IPAddress.Loopback.ToString() && WebUri.IsLoopback;
}
