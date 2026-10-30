using System.Net;
using System.Net.Sockets;
using System.Text;
using FluentAssertions;
using Malabak.Api.Infrastructure.Media;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Malabak.IntegrationTests.Infrastructure;

public sealed class MediaStorageTests
{
    [Theory]
    [InlineData("stream: OK", true)]
    [InlineData("stream: Eicar-Test-Signature FOUND", false)]
    public async Task ClamAv_ReportsCleanOnlyOnOkAsync(string reply, bool expected)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var received = FakeClamdAsync(listener, reply);

        var clean = await Scanner(((IPEndPoint)listener.LocalEndpoint).Port).IsCleanAsync(Encoding.ASCII.GetBytes("file content"));

        clean.Should().Be(expected);
        (await received).Should().StartWith("zINSTREAM\0", "the INSTREAM command is sent first");
    }

    [Fact]
    public async Task ClamAv_Unreachable_FailsClosedAsync()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        (await Scanner(port).IsCleanAsync([1, 2, 3])).Should().BeFalse();
    }

    [Fact]
    public void Production_RequiresS3AndClamAv()
    {
        var act = () => Register(Environments.Production, new Dictionary<string, string?>());

        act.Should().Throw<InvalidOperationException>().WithMessage("*S3*ClamAv*");
    }

    [Fact]
    public void S3_WithoutBucket_IsRejected()
    {
        var act = () => Register(Environments.Development, new Dictionary<string, string?> { ["Media:Storage"] = "S3" });

        act.Should().Throw<InvalidOperationException>().WithMessage("*BucketName*");
    }

    [Fact]
    public void Configured_S3AndClamAv_AreRegistered()
    {
        var provider = Register(Environments.Production, new Dictionary<string, string?>
        {
            ["Media:Storage"] = "S3",
            ["Media:BucketName"] = "malabak-media",
            ["Media:Region"] = "me-central-1",
            ["Media:MalwareScanner"] = "ClamAv",
            ["Media:ClamAvHost"] = "clamav"
        }).BuildServiceProvider();

        provider.GetRequiredService<IObjectStorage>().Should().BeOfType<S3ObjectStorage>();
        provider.GetRequiredService<IMalwareScanner>().Should().BeOfType<ClamAvMalwareScanner>();
    }

    private static ServiceCollection Register(string environment, Dictionary<string, string?> settings)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        services.AddMediaStorage(new ConfigurationBuilder().AddInMemoryCollection(settings).Build(), new TestEnv(environment));
        return services;
    }

    private static ClamAvMalwareScanner Scanner(int port) =>
        new(new MediaStorageOptions { MalwareScanner = "ClamAv", ClamAvHost = "127.0.0.1", ClamAvPort = port }, NullLogger<ClamAvMalwareScanner>.Instance);

    /// <summary>Reads the INSTREAM request up to the zero-length terminator, then answers like clamd.</summary>
    private static async Task<string> FakeClamdAsync(TcpListener listener, string reply)
    {
        using var client = await listener.AcceptTcpClientAsync();
        await using var stream = client.GetStream();
        var received = new List<byte>();
        var buffer = new byte[4096];
        while (received.Count < 4 || received[^4..].Any(b => b != 0) || received.Count <= 10)
        {
            var read = await stream.ReadAsync(buffer);
            if (read == 0)
            {
                break;
            }

            received.AddRange(buffer[..read]);
        }

        await stream.WriteAsync(Encoding.ASCII.GetBytes(reply + "\0"));
        client.Client.Shutdown(SocketShutdown.Send);
        return Encoding.ASCII.GetString(received.ToArray());
    }

    private sealed class TestEnv(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Malabak.Api";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
