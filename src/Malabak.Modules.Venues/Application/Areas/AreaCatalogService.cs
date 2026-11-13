using Malabak.Modules.Venues.Domain;
using Malabak.Modules.Venues.Infrastructure;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Reference;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Malabak.Modules.Venues.Application.Areas;

public sealed record CustomAreaDto(Guid Id, string GovernorateCode, string Code, string NameAr, string NameEn, DateTime CreatedAtUtc);

/// <summary>
/// The built-in governorate/area catalog plus the areas admins and venue owners added. Adding is open to venue owners so a
/// venue in a district we do not list yet can still be found by area; admins can remove what should not be there.
/// </summary>
public sealed class AreaCatalogService
{
    private static readonly Error _unknownGovernorate = Error.Validation("Area.UnknownGovernorate", "Pick one of the listed governorates.");
    private static readonly Error _nameRequired = Error.Validation("Area.NameRequired", "Give the area a name.");
    private static readonly Error _nameTooLong = Error.Validation("Area.NameTooLong", "The area name is too long.");
    private static readonly Error _alreadyListed = Error.Conflict("Area.AlreadyListed", "This area is already in the list.");
    private static readonly Error _notFound = Error.NotFound("Area.NotFound", "Area not found.");

    private readonly VenuesDbContext _dbContext;
    private readonly IClock _clock;
    private readonly ILogger<AreaCatalogService> _logger;

    public AreaCatalogService(VenuesDbContext dbContext, IClock clock, ILogger<AreaCatalogService>? logger = null)
    {
        _dbContext = dbContext;
        _clock = clock;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<AreaCatalogService>.Instance;
    }

    /// <summary>Governorates with the built-in areas and then the added ones, each group in its own order.</summary>
    public async Task<IReadOnlyList<Governorate>> GovernoratesAsync(CancellationToken cancellationToken = default)
    {
        List<CustomArea> custom;
        try
        {
            custom = await _dbContext.CustomAreas.AsNoTracking().OrderBy(a => a.CreatedAtUtc).ToListAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The catalog is public and forms depend on it: serve the built-in areas rather than fail.
            _logger.LogWarning(ex, "Added areas could not be read; serving the built-in catalog only");
            return EgyptReferenceData.Governorates;
        }

        if (custom.Count == 0)
        {
            return EgyptReferenceData.Governorates;
        }

        var byGovernorate = custom.ToLookup(a => a.GovernorateCode);
        return [.. EgyptReferenceData.Governorates.Select(g => byGovernorate.Contains(g.Code)
            ? g with { Areas = [.. g.Areas, .. byGovernorate[g.Code].Select(a => new LocalizedItem(a.Code, a.NameAr, a.NameEn))] }
            : g)];
    }

    /// <summary>Changes whenever an area is added or removed, so cached reference data refreshes.</summary>
    public async Task<string> VersionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var stats = await _dbContext.CustomAreas.AsNoTracking()
                .GroupBy(_ => 1)
                .Select(g => new { Count = g.Count(), Latest = g.Max(a => a.CreatedAtUtc) })
                .FirstOrDefaultAsync(cancellationToken);
            return stats is null ? string.Empty : $"-a{stats.Count}-{stats.Latest.Ticks}";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Added areas could not be read; the catalog version is the built-in one");
            return string.Empty;
        }
    }

    public async Task<IReadOnlyList<CustomAreaDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        var areas = await _dbContext.CustomAreas.AsNoTracking().OrderBy(a => a.GovernorateCode).ThenBy(a => a.NameAr).ToListAsync(cancellationToken);
        return [.. areas.Select(a => new CustomAreaDto(a.Id, a.GovernorateCode, a.Code, a.NameAr, a.NameEn, a.CreatedAtUtc))];
    }

    public async Task<Result<CustomAreaDto>> AddAsync(string? governorateCode, string? nameAr, string? nameEn, Guid userId, CancellationToken cancellationToken = default)
    {
        var governorate = EgyptReferenceData.Governorates.FirstOrDefault(g => string.Equals(g.Code, governorateCode?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (governorate is null)
        {
            return Result.Failure<CustomAreaDto>(_unknownGovernorate);
        }

        var arabic = nameAr?.Trim();
        if (string.IsNullOrWhiteSpace(arabic))
        {
            return Result.Failure<CustomAreaDto>(_nameRequired);
        }

        var english = nameEn?.Trim();
        if (arabic.Length > CustomArea.MaxNameLength || english?.Length > CustomArea.MaxNameLength)
        {
            return Result.Failure<CustomAreaDto>(_nameTooLong);
        }

        var builtIn = governorate.Areas.Any(a => string.Equals(a.NameAr, arabic, StringComparison.OrdinalIgnoreCase) || string.Equals(a.NameEn, arabic, StringComparison.OrdinalIgnoreCase));
        if (builtIn || await _dbContext.CustomAreas.AnyAsync(a => a.GovernorateCode == governorate.Code && a.NameAr == arabic, cancellationToken))
        {
            return Result.Failure<CustomAreaDto>(_alreadyListed);
        }

        var area = CustomArea.Create(governorate.Code, arabic, english, userId, _clock.UtcNow);
        _dbContext.CustomAreas.Add(area);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success(new CustomAreaDto(area.Id, area.GovernorateCode, area.Code, area.NameAr, area.NameEn, area.CreatedAtUtc));
    }

    public async Task<Result> RemoveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var area = await _dbContext.CustomAreas.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (area is null)
        {
            return Result.Failure(_notFound);
        }

        _dbContext.CustomAreas.Remove(area);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
