using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Malabak.Modules.Venues.Application.Common;
using Malabak.Modules.Venues.Application.Venues;
using Malabak.Modules.Venues.Domain;
using Malabak.Modules.Venues.Infrastructure;
using Malabak.SharedKernel;
using Microsoft.Extensions.Logging;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Venues.Application.Venues.CreateVenue;

public sealed class CreateVenueHandler : ICommandHandler<CreateVenueCommand, Result<VenueDto>>
{
    private const int _maxAttempts = 3;

    private readonly IVenueRepository _venueRepository;
    private readonly VenuesDbContext _dbContext;
    private readonly IEntitlementService _entitlements;
    private readonly IClock _clock;
    private readonly ILogger<CreateVenueHandler> _logger;

    public CreateVenueHandler(
        IVenueRepository venueRepository,
        VenuesDbContext dbContext,
        IEntitlementService entitlements,
        IClock clock,
        ILogger<CreateVenueHandler> logger)
    {
        _venueRepository = venueRepository;
        _dbContext = dbContext;
        _entitlements = entitlements;
        _clock = clock;
        _logger = logger;
    }

    public async Task<Result<VenueDto>> HandleAsync(
        CreateVenueCommand command,
        CancellationToken cancellationToken = default)
    {
        var nowUtc = _clock.UtcNow;

        // Plan limit on venues per owner, checked on the server (TASK-060).
        var owned = await _dbContext.Venues.CountAsync(v => v.OwnerId == command.OwnerId && !v.IsDeleted, cancellationToken);
        var allowed = await _entitlements.EnsureWithinLimitAsync(command.OwnerId, EntitlementResource.Venues, owned, cancellationToken);
        if (allowed.IsFailure)
        {
            return Result.Failure<VenueDto>(allowed.Error);
        }

        for (var attempt = 1; attempt <= _maxAttempts; attempt++)
        {
            var baseSlug = GenerateSlug(command.Name);
            if (attempt > 1)
            {
                baseSlug = $"{baseSlug}-{RandomNumberGenerator.GetHexString(2).ToLowerInvariant()}";
            }

            var finalSlug = baseSlug;
            var counter = 1;

            while (await _venueRepository.ExistsBySlugAsync(finalSlug, null, cancellationToken))
            {
                finalSlug = $"{baseSlug}-{counter++}";
            }

            var venue = Venue.Create(
                id: Guid.NewGuid(),
                name: command.Name,
                slug: finalSlug,
                description: command.Description,
                ownerId: command.OwnerId,
                address: command.Address,
                city: command.City,
                area: command.Area,
                latitude: command.Latitude,
                longitude: command.Longitude,
                phoneNumber: command.PhoneNumber,
                facilities: command.Facilities,
                photos: command.Photos,
                createdAtUtc: nowUtc);

            _venueRepository.Add(venue);

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);

                _logger.LogInformation("Venue {VenueId} ('{VenueName}', slug: '{VenueSlug}') created by owner {OwnerId}", venue.Id, venue.Name, venue.Slug, venue.OwnerId);

                return Result.Success(VenueMapper.ToDto(venue));
            }
            catch (DbUpdateException ex) when (IsSlugCollision(ex) && attempt < _maxAttempts)
            {
                _logger.LogWarning(ex, "Slug collision detected for '{Slug}' on attempt {Attempt}. Retrying with fresh slug...", finalSlug, attempt);
                _dbContext.ChangeTracker.Clear();
            }
        }

        return Result.Failure<VenueDto>(new Error("Venue.SlugCollision", "Could not generate a unique slug for the venue. Please try again."));
    }

    private static bool IsSlugCollision(DbUpdateException exception)
    {
        if (exception.InnerException is Npgsql.PostgresException pgEx && pgEx.SqlState == "23505")
        {
            return pgEx.ConstraintName?.Contains("Slug", StringComparison.OrdinalIgnoreCase) == true;
        }

        return exception.InnerException?.Message.Contains("IX_venues_Slug", StringComparison.OrdinalIgnoreCase) == true
            || exception.Message.Contains("IX_venues_Slug", StringComparison.OrdinalIgnoreCase);
    }

    private static string GenerateSlug(string name)
    {
        var slug = name.Trim().ToLowerInvariant();
        slug = Regex.Replace(slug, @"[^a-z0-9\s-]", "");
        slug = Regex.Replace(slug, @"\s+", "-").Trim('-');

        if (string.IsNullOrWhiteSpace(slug))
        {
            var randomSuffix = RandomNumberGenerator.GetHexString(3).ToLowerInvariant();
            return $"venue-{randomSuffix}";
        }

        return slug;
    }
}
