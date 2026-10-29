using Microsoft.AspNetCore.Http;

namespace Malabak.Api.Infrastructure.Idempotency;

public interface IRequestHasher
{
    Task<string> ComputeHashAsync(HttpRequest request, CancellationToken cancellationToken = default);
}
