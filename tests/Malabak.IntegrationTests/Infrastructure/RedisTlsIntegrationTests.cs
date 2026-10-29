using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using FluentAssertions;
using Malabak.Api.Infrastructure.Configuration;
using Malabak.Api.Infrastructure.Redis;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;
using Xunit;

namespace Malabak.IntegrationTests.Infrastructure;

/// <summary>
/// Runs a TLS-only Redis whose certificate is signed by a CA created for this test run, the same
/// shape as the local stack from <c>scripts/generate-dev-certs.sh</c>.
/// </summary>
public sealed class RedisTlsIntegrationTests : IAsyncLifetime
{
    private const int _redisPort = 6379;

    private readonly string _certificateDirectory = Directory.CreateTempSubdirectory("malabak-redis-tls-").FullName;
    private readonly string _password = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
    private IContainer _container = null!;

    private string CaCertificatePath => Path.Combine(_certificateDirectory, "ca.crt");

    public async Task InitializeAsync()
    {
        var (caPem, serverCertificatePem, serverKeyPem) = CreateCertificates();
        await File.WriteAllTextAsync(CaCertificatePath, caPem);

        _container = new ContainerBuilder("redis:7.2-alpine")
            .WithResourceMapping(System.Text.Encoding.ASCII.GetBytes(caPem), "/tls/ca.crt")
            .WithResourceMapping(System.Text.Encoding.ASCII.GetBytes(serverCertificatePem), "/tls/redis.crt")
            .WithResourceMapping(System.Text.Encoding.ASCII.GetBytes(serverKeyPem), "/tls/redis.key")
            .WithCommand(
                "redis-server",
                "--port", "0",
                "--tls-port", _redisPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "--tls-cert-file", "/tls/redis.crt",
                "--tls-key-file", "/tls/redis.key",
                "--tls-ca-cert-file", "/tls/ca.crt",
                "--tls-auth-clients", "no",
                "--requirepass", _password)
            .WithPortBinding(_redisPort, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(_redisPort))
            .Build();

        await _container.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
        Directory.Delete(_certificateDirectory, recursive: true);
    }

    [Fact]
    public async Task CreateConnection_WithTrustedCa_ConnectsOverTlsAsync()
    {
        var options = CreateOptions(CaCertificatePath);

        using var multiplexer = RedisConnectionFactory.CreateConnection(options, NullLogger.Instance);
        var latency = await multiplexer.GetDatabase().PingAsync();

        multiplexer.IsConnected.Should().BeTrue();
        latency.Should().BeGreaterThanOrEqualTo(TimeSpan.Zero);
    }

    [Fact]
    public void CreateConnection_WithoutTrustedCa_RejectsTheServerCertificate()
    {
        var options = CreateOptions(caCertificatePath: null);

        var act = () => RedisConnectionFactory.CreateConnection(options, NullLogger.Instance);

        act.Should().Throw<RedisConnectionException>();
    }

    private RedisOptions CreateOptions(string? caCertificatePath) => new()
    {
        ConnectionString = $"{_container.Hostname}:{_container.GetMappedPublicPort(_redisPort)}",
        Password = _password,
        Ssl = true,
        TlsCaCertificatePath = caCertificatePath,
        AbortOnConnectFail = true,
        ConnectTimeoutMs = 3000,
    };

    private static (string CaPem, string ServerCertificatePem, string ServerKeyPem) CreateCertificates()
    {
        var notBefore = DateTimeOffset.UtcNow.AddMinutes(-5);
        var notAfter = DateTimeOffset.UtcNow.AddDays(1);

        using var caKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var caRequest = new CertificateRequest("CN=Malabak Test CA", caKey, HashAlgorithmName.SHA256);
        caRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        caRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        caRequest.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(caRequest.PublicKey, false));
        using var ca = caRequest.CreateSelfSigned(notBefore, notAfter);

        using var serverKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var serverRequest = new CertificateRequest("CN=localhost", serverKey, HashAlgorithmName.SHA256);
        var subjectAlternativeNames = new SubjectAlternativeNameBuilder();
        subjectAlternativeNames.AddDnsName("localhost");
        subjectAlternativeNames.AddIpAddress(IPAddress.Loopback);
        serverRequest.CertificateExtensions.Add(subjectAlternativeNames.Build());
        serverRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        serverRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        serverRequest.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new System.Security.Cryptography.Oid("1.3.6.1.5.5.7.3.1")], false));
        using var server = serverRequest.Create(ca, notBefore, notAfter, RandomNumberGenerator.GetBytes(16));

        return (ca.ExportCertificatePem(), server.ExportCertificatePem(), serverKey.ExportPkcs8PrivateKeyPem());
    }
}
