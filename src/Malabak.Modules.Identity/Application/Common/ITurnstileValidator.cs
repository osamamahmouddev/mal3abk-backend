namespace Malabak.Modules.Identity.Application.Common;

public interface ITurnstileValidator
{
    Task<bool> ValidateTokenAsync(string? turnstileToken, string? remoteIp = null, CancellationToken cancellationToken = default);
}
