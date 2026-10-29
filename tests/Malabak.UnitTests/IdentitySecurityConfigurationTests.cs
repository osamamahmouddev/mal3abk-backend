using System.Security.Cryptography;
using FluentAssertions;
using Malabak.Modules.Identity.Application.Common;
using Malabak.Modules.Identity.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Malabak.UnitTests;

/// <summary>
/// Security shortcuts (Turnstile bypass and test tokens, built-in HMAC keys, a throwaway JWT key) are
/// allowed only in Development/Testing; everywhere else a missing secret must fail at startup.
/// </summary>
public sealed class IdentitySecurityConfigurationTests
{
    private const string _realTurnstileSecret = "0x4AAAAAAAStagingSecretKeyNotATestKey";

    // Nothing listens on the discard port, so a validator that does call Cloudflare fails fast and offline.
    private const string _unreachableVerifyUrl = "http://127.0.0.1:9/siteverify";

    public static TheoryData<string> ShortcutEnvironments => new() { Environments.Development, "Testing" };

    public static TheoryData<string> StrictEnvironments => new() { Environments.Staging, Environments.Production };

    // --- Turnstile ---

    [Theory]
    [MemberData(nameof(ShortcutEnvironments))]
    public async Task Turnstile_InDevelopmentOrTesting_AcceptsTestTokensAsync(string environment)
    {
        var validator = CreateTurnstileValidator(environment, bypass: false);

        (await validator.ValidateTokenAsync("test-token")).Should().BeTrue();
        (await validator.ValidateTokenAsync("dummy-token")).Should().BeTrue();
        (await validator.ValidateTokenAsync("1x00000000000000000000AA")).Should().BeTrue();
        (await validator.ValidateTokenAsync("real-looking-token")).Should().BeFalse();
    }

    [Theory]
    [MemberData(nameof(StrictEnvironments))]
    public async Task Turnstile_OutsideDevelopmentOrTesting_RejectsTestTokensAsync(string environment)
    {
        var validator = CreateTurnstileValidator(environment, bypass: false);

        (await validator.ValidateTokenAsync("test-token")).Should().BeFalse();
        (await validator.ValidateTokenAsync("dummy-token")).Should().BeFalse();
        (await validator.ValidateTokenAsync("1x00000000000000000000AA")).Should().BeFalse();
    }

    [Fact]
    public async Task Turnstile_BypassInDevelopment_SkipsVerificationInDevelopmentAsync()
    {
        var validator = CreateTurnstileValidator(Environments.Development, bypass: true);

        (await validator.ValidateTokenAsync("real-looking-token")).Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(StrictEnvironments))]
    public async Task Turnstile_BypassInDevelopment_IsIgnoredOutsideDevelopmentOrTestingAsync(string environment)
    {
        var validator = CreateTurnstileValidator(environment, bypass: true);

        (await validator.ValidateTokenAsync("real-looking-token")).Should().BeFalse();
    }

    [Theory]
    [MemberData(nameof(StrictEnvironments))]
    public void Turnstile_BypassInDevelopment_OutsideDevelopmentOrTesting_IsRefused(string environment)
    {
        using var provider = OtpDeliveryTests.BuildServices(environment, new()
        {
            ["Security:Turnstile:SecretKey"] = _realTurnstileSecret,
            ["Security:Turnstile:BypassInDevelopment"] = "true",
        });

        var act = () => provider.GetRequiredService<IOptions<TurnstileOptions>>().Value;

        act.Should().Throw<OptionsValidationException>().WithMessage("*BypassInDevelopment is strictly prohibited*");
    }

    [Theory]
    [MemberData(nameof(StrictEnvironments))]
    public void Turnstile_WithoutSecretKey_OutsideDevelopmentOrTesting_IsRefused(string environment)
    {
        using var provider = OtpDeliveryTests.BuildServices(environment, []);

        var act = () => provider.GetRequiredService<IOptions<TurnstileOptions>>().Value;

        act.Should().Throw<OptionsValidationException>().WithMessage("*Turnstile:SecretKey is required*");
    }

    [Theory]
    [InlineData("1x0000000000000000000000000000000AA")]
    [InlineData("2x0000000000000000000000000000000AA")]
    [InlineData("3x0000000000000000000000000000000AA")]
    public void Turnstile_WithCloudflareTestSecretKey_InStaging_IsRefused(string secretKey)
    {
        using var provider = OtpDeliveryTests.BuildServices(Environments.Staging, new() { ["Security:Turnstile:SecretKey"] = secretKey });

        var act = () => provider.GetRequiredService<IOptions<TurnstileOptions>>().Value;

        act.Should().Throw<OptionsValidationException>().WithMessage("*must not be a Cloudflare test secret key*");
    }

    [Fact]
    public void Turnstile_WithRealSecretKey_InProduction_IsAccepted()
    {
        using var provider = OtpDeliveryTests.BuildServices(Environments.Production, new() { ["Security:Turnstile:SecretKey"] = _realTurnstileSecret });

        var options = provider.GetRequiredService<IOptions<TurnstileOptions>>().Value;

        options.SecretKey.Should().Be(_realTurnstileSecret);
        options.BypassInDevelopment.Should().BeFalse();
    }

    [Theory]
    [MemberData(nameof(ShortcutEnvironments))]
    public void Turnstile_WithoutSecretKey_InDevelopmentOrTesting_UsesCloudflareTestSecret(string environment)
    {
        using var provider = OtpDeliveryTests.BuildServices(environment, new() { ["Security:Turnstile:BypassInDevelopment"] = "true" });

        provider.GetRequiredService<IOptions<TurnstileOptions>>().Value.SecretKey.Should().Be("1x0000000000000000000000000000000AA");
    }

    // --- OTP and step-up HMAC secrets ---

    [Theory]
    [MemberData(nameof(StrictEnvironments))]
    public void OtpHmacSecret_Missing_OutsideDevelopmentOrTesting_IsRefused(string environment)
    {
        using var provider = OtpDeliveryTests.BuildServices(environment, []);

        var act = () => provider.GetRequiredService<IOtpSecurityService>();

        act.Should().Throw<OptionsValidationException>().WithMessage("*Security:Otp:HmacSecret is required*");
    }

    [Theory]
    [MemberData(nameof(StrictEnvironments))]
    public void StepUpSecretKey_Missing_OutsideDevelopmentOrTesting_IsRefused(string environment)
    {
        using var provider = OtpDeliveryTests.BuildServices(environment, []);

        var act = () => provider.GetRequiredService<IOptions<StepUpOptions>>().Value;

        act.Should().Throw<OptionsValidationException>().WithMessage("*Security:StepUp:SecretKey is required*");
    }

    [Fact]
    public void HmacSecrets_TooShort_AreRefusedEvenInDevelopment()
    {
        using var provider = OtpDeliveryTests.BuildServices(Environments.Development, new()
        {
            ["Security:Otp:HmacSecret"] = "short",
            ["Security:StepUp:SecretKey"] = "short",
        });

        ((Action)(() => _ = provider.GetRequiredService<IOptions<OtpSecurityOptions>>().Value))
            .Should().Throw<OptionsValidationException>().WithMessage("*at least 32 characters*");
        ((Action)(() => _ = provider.GetRequiredService<IOptions<StepUpOptions>>().Value))
            .Should().Throw<OptionsValidationException>().WithMessage("*at least 32 characters*");
    }

    [Theory]
    [MemberData(nameof(StrictEnvironments))]
    public void HmacSecrets_Configured_OutsideDevelopmentOrTesting_AreUsed(string environment)
    {
        using var provider = OtpDeliveryTests.BuildServices(environment, new()
        {
            ["Security:Otp:HmacSecret"] = TestSecrets.OtpHmacSecret,
            ["Security:StepUp:SecretKey"] = TestSecrets.StepUpSecretKey,
        });

        provider.GetRequiredService<IOptions<OtpSecurityOptions>>().Value.HmacSecret.Should().Be(TestSecrets.OtpHmacSecret);
        provider.GetRequiredService<IOptions<StepUpOptions>>().Value.SecretKey.Should().Be(TestSecrets.StepUpSecretKey);

        var otp = provider.GetRequiredService<IOtpSecurityService>();
        otp.VerifyCode("123456", otp.ComputeHmacHash("123456")).Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(ShortcutEnvironments))]
    public void HmacSecrets_Missing_InDevelopmentOrTesting_FallBackToDevelopmentKeys(string environment)
    {
        using var provider = OtpDeliveryTests.BuildServices(environment, []);

        provider.GetRequiredService<IOptions<OtpSecurityOptions>>().Value.HmacSecret.Should().Contain("Development-Only");
        provider.GetRequiredService<IOptions<StepUpOptions>>().Value.SecretKey.Should().Contain("Development-Only");
        provider.GetRequiredService<IOtpSecurityService>().Should().NotBeNull();
    }

    // --- JWT signing key ---

    [Theory]
    [MemberData(nameof(StrictEnvironments))]
    public void JwtPrivateKeyPem_Missing_OutsideDevelopmentOrTesting_IsRefused(string environment)
    {
        using var provider = OtpDeliveryTests.BuildServices(environment, []);

        var act = () => provider.GetRequiredService<IJwtKeyProvider>();

        act.Should().Throw<OptionsValidationException>().WithMessage("*PrivateKeyPem is required*");
    }

    [Theory]
    [MemberData(nameof(ShortcutEnvironments))]
    public void JwtPrivateKeyPem_Missing_InDevelopmentOrTesting_UsesThrowawayKey(string environment)
    {
        using var provider = OtpDeliveryTests.BuildServices(environment, []);

        provider.GetRequiredService<IJwtKeyProvider>().GetCurrentSigningKey().Should().NotBeNull();
    }

    [Fact]
    public void JwtPrivateKeyPem_Configured_InProduction_IsUsed()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var provider = OtpDeliveryTests.BuildServices(Environments.Production, new() { ["Security:Jwt:PrivateKeyPem"] = ecdsa.ExportPkcs8PrivateKeyPem() });

        var signingKey = provider.GetRequiredService<IJwtKeyProvider>().GetCurrentSigningKey();

        signingKey.ECDsa.ExportSubjectPublicKeyInfo().Should().Equal(ecdsa.ExportSubjectPublicKeyInfo());
    }

    [Theory]
    [InlineData("not a pem")]
    [InlineData("P384")]
    public void JwtPrivateKeyPem_NotAP256PrivateKey_IsRefused(string kind)
    {
        using var p384 = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        var pem = kind == "P384" ? p384.ExportPkcs8PrivateKeyPem() : kind;
        using var provider = OtpDeliveryTests.BuildServices(Environments.Production, new() { ["Security:Jwt:PrivateKeyPem"] = pem });

        var act = () => provider.GetRequiredService<IOptions<JwtOptions>>().Value;

        act.Should().Throw<OptionsValidationException>().WithMessage("*ECDSA P-256 private key*");
    }

    // --- Startup ---

    [Fact]
    public void Startup_InStagingWithoutSecrets_FailsValidation()
    {
        using var provider = OtpDeliveryTests.BuildServices(Environments.Staging, []);

        var act = () => provider.GetRequiredService<IStartupValidator>().Validate();

        act.Should().Throw<AggregateException>()
            .Which.InnerExceptions.Should().HaveCount(4).And.AllBeOfType<OptionsValidationException>();
    }

    [Fact]
    public void Startup_InDevelopmentWithoutSecrets_Passes()
    {
        using var provider = OtpDeliveryTests.BuildServices(Environments.Development, []);

        var act = () => provider.GetRequiredService<IStartupValidator>().Validate();

        act.Should().NotThrow();
    }

    private static TurnstileValidator CreateTurnstileValidator(string environment, bool bypass) => new(
        Options.Create(new TurnstileOptions { SecretKey = _realTurnstileSecret, VerifyUrl = _unreachableVerifyUrl, BypassInDevelopment = bypass }),
        new OtpDeliveryTests.TestHostEnvironment(environment),
        NullLogger<TurnstileValidator>.Instance);
}
