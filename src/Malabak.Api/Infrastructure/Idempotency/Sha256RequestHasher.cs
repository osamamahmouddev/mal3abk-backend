using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace Malabak.Api.Infrastructure.Idempotency;

public sealed class Sha256RequestHasher : IRequestHasher
{
    public async Task<string> ComputeHashAsync(HttpRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!request.Body.CanSeek)
        {
            request.EnableBuffering();
        }

        var bodyText = string.Empty;
        if (request.Body.CanSeek)
        {
            request.Body.Position = 0;
            using (var reader = new StreamReader(request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true))
            {
                bodyText = await reader.ReadToEndAsync(cancellationToken);
            }
            request.Body.Position = 0;
        }

        var method = request.Method.ToUpperInvariant();
        var path = request.Path.Value?.ToLowerInvariant() ?? string.Empty;
        var query = request.QueryString.Value ?? string.Empty;

        var combined = $"{method}:{path}:{query}:{bodyText}";
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(combined));

        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}
