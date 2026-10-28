using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace Malabak.Api.Infrastructure.Security;

/// <summary>
/// Development convenience only: when no keys are configured, generate random in-memory keys instead of
/// shipping a hardcoded fallback secret. Anything encrypted with them is unreadable after a restart.
/// </summary>
public sealed class EncryptionOptionsSetup : IPostConfigureOptions<EncryptionOptions>
{
    private const string _ephemeralKekId = "dev-ephemeral";

    private readonly IHostEnvironment _environment;

    public EncryptionOptionsSetup(IHostEnvironment environment)
    {
        _environment = environment;
    }

    public void PostConfigure(string? name, EncryptionOptions options)
    {
        if (!_environment.IsDevelopment())
        {
            return;
        }

        if (options.KeyEncryptionKeys.Count == 0)
        {
            options.CurrentKeyEncryptionKeyId = _ephemeralKekId;
            options.KeyEncryptionKeys[_ephemeralKekId] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(EncryptionOptions.KeySizeBytes));
            options.UsesEphemeralDevelopmentKeys = true;
        }

        if (string.IsNullOrWhiteSpace(options.BlindIndexKey))
        {
            options.BlindIndexKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(EncryptionOptions.KeySizeBytes));
            options.UsesEphemeralDevelopmentKeys = true;
        }
    }
}
