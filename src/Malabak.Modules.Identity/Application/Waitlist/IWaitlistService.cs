using Malabak.Modules.Identity.Domain;
using Malabak.SharedKernel;

namespace Malabak.Modules.Identity.Application.Waitlist;

public sealed record WaitlistDto(
    Guid Id,
    WaitlistAudience Audience,
    string? ChosenPlan,
    string Phone,
    DateTime ConsentRecordedAtUtc,
    DateTime CreatedAtUtc);

public sealed record WaitlistJoinResult(
    Guid Id,
    bool IsNew,
    string Message);

public sealed record WaitlistPagedResult(
    IReadOnlyList<WaitlistDto> Items,
    int TotalCount,
    int Page,
    int PageSize);

public interface IWaitlistService
{
    Task<Result<WaitlistJoinResult>> JoinAsync(
        string rawPhone,
        WaitlistAudience audience,
        string? chosenPlan,
        string? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken = default);

    Task<Result<WaitlistPagedResult>> ListAsync(
        WaitlistAudience? audience,
        string? plan,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<Result> DeleteAsync(
        Guid id,
        CancellationToken cancellationToken = default);
}
