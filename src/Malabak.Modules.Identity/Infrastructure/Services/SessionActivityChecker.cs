using Malabak.Modules.Identity.Application.Sessions;
using Malabak.SharedKernel.Contracts;

namespace Malabak.Modules.Identity.Infrastructure.Services;

public sealed class SessionActivityChecker(ISessionStore store) : ISessionActivityChecker
{
    public Task<bool> IsActiveAsync(Guid sessionId, CancellationToken cancellationToken = default) =>
        store.IsActiveAsync(sessionId, cancellationToken);
}
