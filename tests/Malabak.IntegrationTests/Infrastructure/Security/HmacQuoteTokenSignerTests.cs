using System.Text;
using FluentAssertions;
using Malabak.Api.Infrastructure.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Malabak.IntegrationTests.Infrastructure.Security;

public sealed class HmacQuoteTokenSignerTests
{
    [Fact]
    public void RoundTrip_Works_AndAnyChangeIsRejected()
    {
        var signer = Signer(Environments.Production, "quote-signing-key-for-tests-0123456789");
        var token = signer.Protect(Encoding.UTF8.GetBytes("{\"total\":800}"));

        Encoding.UTF8.GetString(signer.Unprotect(token)!).Should().Be("{\"total\":800}");
        signer.Unprotect(token.Replace("ODAw", "OTAw", StringComparison.Ordinal)).Should().BeNull("payload changed");
        signer.Unprotect(token + "x").Should().BeNull();
        signer.Unprotect("no-dot").Should().BeNull();
        Signer(Environments.Production, "another-key-0123456789").Unprotect(token).Should().BeNull("different key");
    }

    [Fact]
    public void MissingKey_FailsOutsideDevelopment()
    {
        var act = () => Signer(Environments.Production, null);

        act.Should().Throw<InvalidOperationException>().WithMessage("*QuoteSigningKey*");
        Signer(Environments.Development, null).Should().NotBeNull();
    }

    private static HmacQuoteTokenSigner Signer(string environment, string? key) =>
        new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Booking:QuoteSigningKey"] = key }).Build(), new TestEnv(environment));

    private sealed class TestEnv(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Malabak.Api";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
