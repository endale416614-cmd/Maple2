using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Health.V1;
using Grpc.Net.Client;
using Grpc.Net.ClientFactory;
using Maple2.Server.Core.Modules;
using Maple2.Server.World.Service;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WorldClient = Maple2.Server.World.Service.World.WorldClient;

namespace Maple2.Server.Tests.Offline;

/// <summary>NT-01 (BT-215): the servers' gRPC calls to each other reach the server on this PC when a proxy applies to 127.0.0.1
/// (HTTP_PROXY, ALL_PROXY, a Windows proxy without the local exception); a default channel fails there before it sends a byte.</summary>
[NonParallelizable] // HttpClient.DefaultProxy is process-wide.
public class DirectGrpcTest {
    private WebApplication server = null!;
    private Uri address = null!;
    private IWebProxy savedProxy = null!;

    [OneTimeSetUp]
    public async Task Start() {
        // A gRPC server as the World and the Game run one: HTTP/2 only, on 127.0.0.1; here with the health service alone.
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseKestrel(options => options.Listen(IPAddress.Loopback, 0, listen => listen.Protocols = HttpProtocols.Http2));
        builder.Logging.ClearProviders();
        builder.Services.AddGrpc();
        builder.Services.AddGrpcHealthChecks();
        server = builder.Build();
        server.MapGrpcHealthChecksService();
        await server.StartAsync();
        address = new Uri(server.Urls.First());

        savedProxy = HttpClient.DefaultProxy;
        HttpClient.DefaultProxy = new WebProxy("http://127.0.0.1:9"); // applies to 127.0.0.1 too; nothing listens there
    }

    [OneTimeTearDown]
    public async Task Stop() {
        HttpClient.DefaultProxy = savedProxy;
        await server.StopAsync();
        await server.DisposeAsync();
    }

    [Test]
    public void DirectChannel_ReachesTheServer() {
        using GrpcChannel channel = DirectGrpc.Channel(address);
        Assert.DoesNotThrowAsync(async () => await new Health.HealthClient(channel).CheckAsync(new HealthCheckRequest(), deadline: Deadline()));
    }

    [Test]
    public void WorldClientModule_ReachesTheServer() {
        var services = new ServiceCollection();
        services.RegisterModule<WorldClientModule>();
        services.PostConfigureAll<GrpcClientFactoryOptions>(options => options.Address = address); // this test's server, not the World's port
        using ServiceProvider provider = services.BuildServiceProvider();
        var error = Assert.ThrowsAsync<RpcException>(async () => await provider.GetRequiredService<WorldClient>().AddChannelAsync(new AddChannelRequest(), deadline: Deadline()));
        Assert.That(error!.StatusCode, Is.EqualTo(StatusCode.Unimplemented), error.Status.Detail); // the server answered: it has no World service
    }

    // The control: the channel the servers made before fails through the proxy, as in the reports.
    [Test]
    public void DefaultChannel_FailsThroughTheProxy() {
        using GrpcChannel channel = GrpcChannel.ForAddress(address);
        var error = Assert.ThrowsAsync<RpcException>(async () => await new Health.HealthClient(channel).CheckAsync(new HealthCheckRequest(), deadline: Deadline()));
        Assert.That(error!.StatusCode, Is.EqualTo(StatusCode.Internal), error.Status.Detail);
        Assert.That(error.Status.Detail, Does.Contain("RequestVersionExact"));
        // The Game logs the exception, and its inner exception is the cause, so the log shows the HttpRequestException.
        Assert.That(error.InnerException, Is.Not.Null.And.SameAs(error.Status.DebugException));
    }

    private static DateTime Deadline() => DateTime.UtcNow.AddSeconds(5);
}
