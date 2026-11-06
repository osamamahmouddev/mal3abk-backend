using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;

namespace Malabak.UnitTests;

/// <summary>Entitlement stand-in for tests that are not about plan limits: everything is allowed.</summary>
internal sealed class AllowAllEntitlements : IEntitlementService
{
    public Task<Result> EnsureWithinLimitAsync(Guid ownerId, EntitlementResource resource, int currentCount, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Success());

    public Task<bool> HasFeatureAsync(Guid ownerId, string feature, CancellationToken cancellationToken = default) => Task.FromResult(true);
}
