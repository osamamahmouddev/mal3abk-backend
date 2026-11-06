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

        var baseSlug = GenerateSlug(command.Name);
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
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Venue {VenueId} ('{VenueName}') created by owner {OwnerId}", venue.Id, venue.Name, venue.OwnerId);

        return Result.Success(VenueMapper.ToDto(venue));
    }

    private static string GenerateSlug(string name)
    {
        var slug = name.Trim().ToLowerInvariant();
        slug = Regex.Replace(slug, @"[^a-z0-9\s-]", "");
        slug = Regex.Replace(slug, @"\s+", "-").Trim('-');

        return string.IsNullOrWhiteSpace(slug) ? "venue" : slug;
    }
}
