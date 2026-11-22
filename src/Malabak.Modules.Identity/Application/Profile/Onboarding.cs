using Malabak.Modules.Identity.Domain;
using Malabak.Modules.Identity.Infrastructure;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Reference;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Identity.Application.Profile;

public sealed record OnboardingStatusDto(bool IsComplete, IReadOnlyList<string> MissingSteps, string Language);

public sealed record MyProfileDto(
    Guid UserId,
    string FullName,
    string PhoneNumber,
    string? Email,
    string? City,
    string? Area,
    IReadOnlyList<string> Positions,
    string? SkillLevel,
    string Language,
    bool OnboardingComplete,
    string? AvatarUrl);

/// <summary>
/// What other players may see. Deliberately excludes phone, email, area and anything finer than the governorate
/// (SECURITY.md data minimisation).
/// </summary>
public sealed record PublicPlayerProfileDto(
    Guid UserId, string FullName, string? AvatarUrl, string? City, IReadOnlyList<string> Positions, string? SkillLevel, PlayerStatsSummary? Stats);

public sealed record PlayerStatsSummary(int MatchesPlayed, int TeamsCount);

/// <summary>Hook for the teams/matches modules to contribute a stats summary; none is registered in Phase 1.</summary>
public interface IPlayerStatsSummaryProvider
{
    Task<PlayerStatsSummary?> GetAsync(Guid userId, CancellationToken cancellationToken = default);
}

public sealed class NoPlayerStatsSummaryProvider : IPlayerStatsSummaryProvider
{
    public Task<PlayerStatsSummary?> GetAsync(Guid userId, CancellationToken cancellationToken = default) => Task.FromResult<PlayerStatsSummary?>(null);
}

/// <summary>Partial update: null fields stay unchanged; City and Area must be sent together.</summary>
public sealed record UpdateMyProfileCommand(
    Guid UserId, string? FullName, string? City, string? Area, IReadOnlyList<PlayerPosition>? Positions, SkillLevel? SkillLevel, string? Language)
    : ICommand<Result<MyProfileDto>>;

/// <param name="AvatarUrl">URL of a ready media-pipeline avatar owned by the user (checked by the caller), or null to remove.</param>
public sealed record SetAvatarCommand(Guid UserId, string? AvatarUrl) : ICommand<Result<MyProfileDto>>;

public sealed record CompleteProfileCommand(
    Guid UserId, string FullName, string City, string Area, IReadOnlyList<PlayerPosition>? Positions, SkillLevel? SkillLevel, string? Language)
    : ICommand<Result<MyProfileDto>>;

public sealed record SetLanguageCommand(Guid UserId, string Language) : ICommand<Result>;

public static class ProfileErrors
{
    public static readonly Error NotFound = new("Profile.NotFound", "Account not found.");
    public static readonly Error InvalidName = new("Profile.InvalidName", "Full name is required (2–100 characters).");
    public static readonly Error InvalidPlace = new("Profile.InvalidPlace", "Choose a governorate and area from the reference list (GET /reference/governorates).");
    public static readonly Error InvalidLanguage = new("Profile.InvalidLanguage", "Language must be 'ar' or 'en'.");
    public static readonly Error InvalidPositions = new("Profile.InvalidPositions", "Choose up to 4 valid positions.");
}

/// <summary>
/// Player onboarding after the first OTP sign-in (the account itself is created by OTP verify). Collects only
/// what the product needs: name, city/area (never precise location), optional positions/skill, language.
/// </summary>
public sealed class ProfileHandlers :
    ICommandHandler<CompleteProfileCommand, Result<MyProfileDto>>,
    ICommandHandler<SetLanguageCommand, Result>,
    ICommandHandler<UpdateMyProfileCommand, Result<MyProfileDto>>,
    ICommandHandler<SetAvatarCommand, Result<MyProfileDto>>
{
    private readonly IdentityDbContext _dbContext;
    private readonly IClock _clock;

    public ProfileHandlers(IdentityDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    public async Task<OnboardingStatusDto?> GetOnboardingStatusAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId && !u.IsDeleted, cancellationToken);
        if (user is null)
        {
            return null;
        }

        var missing = new List<string>();
        if (!user.IsOnboardingComplete || string.IsNullOrWhiteSpace(user.FullName))
        {
            missing.Add("profile");
        }

        return new OnboardingStatusDto(missing.Count == 0, missing, user.Language);
    }

    public async Task<MyProfileDto?> GetMyProfileAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId && !u.IsDeleted, cancellationToken);
        return user is null ? null : ToDto(user);
    }

    public async Task<PublicPlayerProfileDto?> GetPublicProfileAsync(
        Guid userId, IPlayerStatsSummaryProvider stats, CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId && !u.IsDeleted && u.OnboardingCompletedAtUtc != null, cancellationToken);
        if (user is null)
        {
            return null;
        }

        return new PublicPlayerProfileDto(
            user.Id, user.FullName, user.AvatarUrl, user.City, user.Positions.Select(p => p.ToString()).ToList(), user.SkillLevel?.ToString(),
            await stats.GetAsync(user.Id, cancellationToken));
    }

    public async Task<Result<MyProfileDto>> HandleAsync(CompleteProfileCommand command, CancellationToken cancellationToken = default)
    {
        var validation = Validate(command);
        if (validation is not null)
        {
            return Result.Failure<MyProfileDto>(validation);
        }

        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == command.UserId && !u.IsDeleted, cancellationToken);
        if (user is null)
        {
            return Result.Failure<MyProfileDto>(ProfileErrors.NotFound);
        }

        var nowUtc = _clock.UtcNow;
        var (city, area) = NormalizePlace(Clean(command.City), Clean(command.Area));
        user.UpdateProfile(Clean(command.FullName), city, area, command.Positions, command.SkillLevel, nowUtc);
        if (command.Language is not null)
        {
            user.SetLanguage(command.Language, nowUtc);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(user));
    }

    public async Task<Result<MyProfileDto>> HandleAsync(UpdateMyProfileCommand command, CancellationToken cancellationToken = default)
    {
        if ((command.City is null) != (command.Area is null))
        {
            return Result.Failure<MyProfileDto>(ProfileErrors.InvalidPlace);
        }

        var user = await _dbContext.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == command.UserId && !u.IsDeleted, cancellationToken);
        if (user is null)
        {
            return Result.Failure<MyProfileDto>(ProfileErrors.NotFound);
        }

        if (command.City is null && (user.City is null || user.Area is null))
        {
            // Owners and staff have no player profile (city, positions): they only change their name and language.
            return user.Roles.Any(r => r != UserRole.Player) && command.Positions is null && command.SkillLevel is null
                ? await RenameAsync(command, cancellationToken)
                : Result.Failure<MyProfileDto>(ProfileErrors.InvalidPlace);
        }

        // Merge onto the current values and reuse the onboarding validation/normalisation.
        return await HandleAsync(new CompleteProfileCommand(
            command.UserId,
            command.FullName ?? user.FullName,
            command.City ?? user.City!,
            command.Area ?? user.Area!,
            command.Positions ?? user.Positions.ToList(),
            command.SkillLevel ?? user.SkillLevel,
            command.Language), cancellationToken);
    }

    public async Task<Result<MyProfileDto>> HandleAsync(SetAvatarCommand command, CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == command.UserId && !u.IsDeleted, cancellationToken);
        if (user is null)
        {
            return Result.Failure<MyProfileDto>(ProfileErrors.NotFound);
        }

        user.SetAvatar(command.AvatarUrl, _clock.UtcNow);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(user));
    }

    public async Task<Result> HandleAsync(SetLanguageCommand command, CancellationToken cancellationToken = default)
    {
        if (!UserLanguages.IsSupported(command.Language))
        {
            return Result.Failure(ProfileErrors.InvalidLanguage);
        }

        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == command.UserId && !u.IsDeleted, cancellationToken);
        if (user is null)
        {
            return Result.Failure(ProfileErrors.NotFound);
        }

        user.SetLanguage(command.Language, _clock.UtcNow);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private async Task<Result<MyProfileDto>> RenameAsync(UpdateMyProfileCommand command, CancellationToken cancellationToken)
    {
        if (command.Language is not null && !UserLanguages.IsSupported(command.Language))
        {
            return Result.Failure<MyProfileDto>(ProfileErrors.InvalidLanguage);
        }

        var name = command.FullName is null ? null : Clean(command.FullName);
        if (name is not null && name.Length is < 2 or > User.MaxNameLength)
        {
            return Result.Failure<MyProfileDto>(ProfileErrors.InvalidName);
        }

        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == command.UserId && !u.IsDeleted, cancellationToken);
        if (user is null)
        {
            return Result.Failure<MyProfileDto>(ProfileErrors.NotFound);
        }

        var nowUtc = _clock.UtcNow;
        if (name is not null)
        {
            user.Rename(name, nowUtc);
        }

        if (command.Language is not null)
        {
            user.SetLanguage(command.Language, nowUtc);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(user));
    }

    internal static MyProfileDto ToDto(User user) => new(
        user.Id, user.FullName, user.PhoneNumber, user.Email, user.City, user.Area,
        user.Positions.Select(p => p.ToString()).ToList(), user.SkillLevel?.ToString(), user.Language, user.IsOnboardingComplete, user.AvatarUrl);

    private static Error? Validate(CompleteProfileCommand command)
    {
        var name = Clean(command.FullName);
        if (name.Length is < 2 or > User.MaxNameLength)
        {
            return ProfileErrors.InvalidName;
        }

        var city = Clean(command.City);
        var area = Clean(command.Area);
        var governorate = EgyptReferenceData.FindGovernorate(city);
        if (city.Length == 0 || area.Length == 0 || city.Length > User.MaxPlaceLength || area.Length > User.MaxPlaceLength
            || governorate is null || !EgyptReferenceData.IsValidArea(governorate, area))
        {
            return ProfileErrors.InvalidPlace;
        }

        if (command.Positions is { } positions && (positions.Count > 4 || positions.Any(p => !Enum.IsDefined(p))))
        {
            return ProfileErrors.InvalidPositions;
        }

        if (command.SkillLevel is { } skill && !Enum.IsDefined(skill))
        {
            return ProfileErrors.InvalidPositions;
        }

        return command.Language is not null && !UserLanguages.IsSupported(command.Language) ? ProfileErrors.InvalidLanguage : null;
    }

    /// <summary>Stores stable codes (e.g. "cairo", "nasr_city") so names can be re-localized; free-text areas stay as typed.</summary>
    private static (string City, string Area) NormalizePlace(string city, string area)
    {
        var governorate = EgyptReferenceData.FindGovernorate(city)!;
        var curated = governorate.Areas.FirstOrDefault(a =>
            string.Equals(a.Code, area, StringComparison.OrdinalIgnoreCase)
            || string.Equals(a.NameEn, area, StringComparison.OrdinalIgnoreCase)
            || string.Equals(a.NameAr, area, StringComparison.Ordinal));
        return (governorate.Code, curated?.Code ?? area);
    }

    /// <summary>Trims and strips control / bidi-override characters that could spoof how a name renders (SECURITY.md §15.2).</summary>
    internal static string Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var cleaned = new string(value.Where(c => !char.IsControl(c) && c is not ('‪' or '‫' or '‬' or '‭' or '‮'
            or '⁦' or '⁧' or '⁨' or '⁩' or '‎' or '‏')).ToArray());
        return string.Join(' ', cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
