using Malabak.Modules.Identity.Application.Common;
using Malabak.Modules.Identity.Infrastructure.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Malabak.UnitTests;

internal static class TestSecrets
{
    public const string OtpHmacSecret = "TestSecretKeyForHmacSha256Authentication123456!";
    public const string StepUpSecretKey = "TestStepUpSecretKeyHmacSha2561234567890!";

    /// <summary>A Development validator, which accepts the <c>test-</c>/<c>dummy-</c> tokens handler tests send.</summary>
    public static TurnstileValidator CreateDevelopmentTurnstileValidator() => new(
        Options.Create(new TurnstileOptions()),
        new OtpDeliveryTests.TestHostEnvironment(Environments.Development),
        NullLogger<TurnstileValidator>.Instance);
}
