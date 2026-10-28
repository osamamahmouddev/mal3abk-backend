namespace Malabak.SharedKernel.Contracts;

/// <summary>Lets other modules ask whether a device session (the token <c>sid</c>) is still signed in.</summary>
public interface ISessionActivityChecker
{
    Task<bool> IsActiveAsync(Guid sessionId, CancellationToken cancellationToken = default);
}
