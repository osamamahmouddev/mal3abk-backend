using Malabak.Modules.Identity.Domain;
using Malabak.Modules.Identity.Infrastructure;
using Malabak.SharedKernel;
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
    bool OnboardingComplete);

public sealed record CompleteProfileCommand(
    Guid UserId, string FullName, string City, string Area, IReadOnlyList<PlayerPosition>? Positions, SkillLevel? SkillLevel, string? Language)
    : ICommand<Result<MyProfileDto>>;

public sealed record SetLanguageCommand(Guid UserId, string Language) : ICommand<Result>;

public static class ProfileErrors
{
    public static readonly Error NotFound = new("Profile.NotFound", "Account not found.");
    public static readonly Error InvalidName = new("Profile.InvalidName", "Full name is required (2–100 characters).");
    public static readonly Error InvalidPlace = new("Profile.InvalidPlace", "City and area are required (up to 100 characters).");
    public static readonly Error InvalidLanguage = new("Profile.InvalidLanguage", "Language must be 'ar' or 'en'.");
    public static readonly Error InvalidPositions = new("Profile.InvalidPositions", "Choose up to 4 valid positions.");
}

/// <summary>
/// Player onboarding after the first OTP sign-in (the account itself is created by OTP verify). Collects only
/// what the product needs: name, city/area (never precise location), optional positions/skill, language.
/// </summary>
public sealed class ProfileHandlers :
    ICommandHandler<CompleteProfileCommand, Result<MyProfileDto>>,
    ICommandHandler<SetLanguageCommand, Result>
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
        user.UpdateProfile(Clean(command.FullName), Clean(command.City), Clean(command.Area), command.Positions, command.SkillLevel, nowUtc);
        if (command.Language is not null)
        {
            user.SetLanguage(command.Language, nowUtc);
        }

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

    internal static MyProfileDto ToDto(User user) => new(
        user.Id, user.FullName, user.PhoneNumber, user.Email, user.City, user.Area,
        user.Positions.Select(p => p.ToString()).ToList(), user.SkillLevel?.ToString(), user.Language, user.IsOnboardingComplete);

    private static Error? Validate(CompleteProfileCommand command)
    {
        var name = Clean(command.FullName);
        if (name.Length is < 2 or > User.MaxNameLength)
        {
            return ProfileErrors.InvalidName;
        }

        var city = Clean(command.City);
        var area = Clean(command.Area);
        if (city.Length == 0 || area.Length == 0 || city.Length > User.MaxPlaceLength || area.Length > User.MaxPlaceLength)
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
