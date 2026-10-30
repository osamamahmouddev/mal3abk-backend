using Microsoft.Extensions.Options;

namespace Malabak.Api.Infrastructure.Security;

/// <summary>Fails startup (ValidateOnStart) when key material is missing or malformed.</summary>
public sealed class EncryptionOptionsValidator : IValidateOptions<EncryptionOptions>
{
    public ValidateOptionsResult Validate(string? name, EncryptionOptions options)
    {
        var failures = new List<string>();

        if (options.UsesAwsKms)
        {
            if (string.IsNullOrWhiteSpace(options.KmsKeyId))
            {
                failures.Add($"{EncryptionOptions.SectionName}:KmsKeyId is required when KeyProvider is AwsKms.");
            }
        }
        else if (string.IsNullOrWhiteSpace(options.CurrentKeyEncryptionKeyId)
            || !options.KeyEncryptionKeys.ContainsKey(options.CurrentKeyEncryptionKeyId))
        {
            failures.Add($"{EncryptionOptions.SectionName}:CurrentKeyEncryptionKeyId must name an entry in KeyEncryptionKeys.");
        }

        foreach (var (keyId, value) in options.KeyEncryptionKeys)
        {
            if (!IsValidKey(value))
            {
                failures.Add($"{EncryptionOptions.SectionName}:KeyEncryptionKeys:{keyId} must be a base64 {EncryptionOptions.KeySizeBytes}-byte key.");
            }
        }

        if (!IsValidKey(options.BlindIndexKey))
        {
            failures.Add($"{EncryptionOptions.SectionName}:BlindIndexKey must be a base64 {EncryptionOptions.KeySizeBytes}-byte key.");
        }
        else if (options.KeyEncryptionKeys.Values.Any(k => string.Equals(k, options.BlindIndexKey, StringComparison.Ordinal)))
        {
            failures.Add($"{EncryptionOptions.SectionName}:BlindIndexKey must not reuse a key-encryption key.");
        }

        if (options.KeyRingRefreshMinutes <= 0)
        {
            failures.Add($"{EncryptionOptions.SectionName}:KeyRingRefreshMinutes must be positive.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static bool IsValidKey(string? base64)
    {
        if (string.IsNullOrWhiteSpace(base64))
        {
            return false;
        }

        var buffer = new byte[EncryptionOptions.KeySizeBytes + 1];
        return Convert.TryFromBase64String(base64, buffer, out var written) && written == EncryptionOptions.KeySizeBytes;
    }
}
