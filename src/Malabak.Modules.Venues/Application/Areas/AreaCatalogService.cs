using Malabak.Modules.Venues.Domain;
using Malabak.Modules.Venues.Infrastructure;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Reference;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Malabak.Modules.Venues.Application.Areas;

public sealed record CustomAreaDto(
    Guid Id,
    string GovernorateCode,
    string Code,
    string NameAr,
    string NameEn,
    bool IsActive,
    bool IsFocus,
    bool IsCustom,
    DateTime CreatedAtUtc);

/// <summary>
/// Manages standard and custom areas across all Egyptian governorates.
/// Allows admins to toggle active status (pause/resume areas), set operational focus zones (target areas),
/// and allows venue owners or admins to add missing areas.
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

    /// <summary>Ensures standard Egyptian areas from the reference catalog exist in the database.</summary>
    public async Task EnsureStandardAreasSeededAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var hasStandard = await _dbContext.CustomAreas.AnyAsync(a => !a.IsCustom, cancellationToken);
            if (hasStandard)
            {
                return;
            }

            var now = _clock.UtcNow;
            var toAdd = new List<CustomArea>();

            foreach (var gov in EgyptReferenceData.Governorates)
            {
                var existingNames = await _dbContext.CustomAreas
                    .Where(a => a.GovernorateCode == gov.Code)
                    .Select(a => a.NameAr)
                    .ToListAsync(cancellationToken);

                var existingSet = new HashSet<string>(existingNames, StringComparer.Ordinal);
                var order = 0;

                foreach (var area in gov.Areas)
                {
                    if (!existingSet.Contains(area.NameAr))
                    {
                        toAdd.Add(CustomArea.CreateStandard(gov.Code, area.Code, area.NameAr, area.NameEn, order++, now));
                    }
                }
            }

            if (toAdd.Count > 0)
            {
                _dbContext.CustomAreas.AddRange(toAdd);
                await _dbContext.SaveChangesAsync(cancellationToken);
                _logger.LogInformation("Seeded {Count} standard areas across Egyptian governorates", toAdd.Count);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not ensure standard areas are seeded");
        }
    }

    /// <summary>Governorates with active areas only, ordered by focus areas first.</summary>
    public async Task<IReadOnlyList<Governorate>> GovernoratesAsync(CancellationToken cancellationToken = default)
    {
        await EnsureStandardAreasSeededAsync(cancellationToken);

        List<CustomArea> areas;
        try
        {
            areas = await _dbContext.CustomAreas.AsNoTracking()
                .Where(a => a.IsActive)
                .OrderBy(a => a.IsFocus ? 0 : 1)
                .ThenBy(a => a.DisplayOrder)
                .ThenBy(a => a.NameAr)
                .ToListAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Areas could not be read from database; serving built-in catalog fallback");
            return EgyptReferenceData.Governorates;
        }

        if (areas.Count == 0)
        {
            return EgyptReferenceData.Governorates;
        }

        var byGovernorate = areas.ToLookup(a => a.GovernorateCode);
        return [.. EgyptReferenceData.Governorates.Select(g => byGovernorate.Contains(g.Code)
            ? g with { Areas = [.. byGovernorate[g.Code].Select(a => new LocalizedItem(a.Code, a.NameAr, a.NameEn, a.IsFocus))] }
            : g with { Areas = [] })];
    }

    /// <summary>Changes whenever any area is added, updated, toggled, or removed, so caches refresh.</summary>
    public async Task<string> VersionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var stats = await _dbContext.CustomAreas.AsNoTracking()
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    Count = g.Count(),
                    ActiveCount = g.Count(a => a.IsActive),
                    FocusCount = g.Count(a => a.IsFocus),
                    Latest = g.Max(a => a.CreatedAtUtc)
                })
                .FirstOrDefaultAsync(cancellationToken);

            return stats is null
                ? string.Empty
                : $"-a{stats.Count}-act{stats.ActiveCount}-f{stats.FocusCount}-{stats.Latest.Ticks}";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Added areas could not be read; catalog version is fallback");
            return string.Empty;
        }
    }

    /// <summary>List areas for admin management with governorate, search and status filters.</summary>
    public async Task<IReadOnlyList<CustomAreaDto>> ListAdminAreasAsync(
        string? governorateCode = null,
        string? search = null,
        string? status = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureStandardAreasSeededAsync(cancellationToken);

        var query = _dbContext.CustomAreas.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(governorateCode))
        {
            var gov = governorateCode.Trim().ToLowerInvariant();
            query = query.Where(a => a.GovernorateCode == gov);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLowerInvariant();
            query = query.Where(a =>
                a.NameAr.ToLower().Contains(s)
                || a.NameEn.ToLower().Contains(s)
                || a.Code.ToLower().Contains(s));
        }

        if (string.Equals(status, "active", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(a => a.IsActive);
        }
        else if (string.Equals(status, "inactive", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(a => !a.IsActive);
        }
        else if (string.Equals(status, "focus", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(a => a.IsFocus);
        }
        else if (string.Equals(status, "custom", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(a => a.IsCustom);
        }

        var list = await query
            .OrderBy(a => a.GovernorateCode)
            .ThenBy(a => a.IsFocus ? 0 : 1)
            .ThenBy(a => a.DisplayOrder)
            .ThenBy(a => a.NameAr)
            .ToListAsync(cancellationToken);

        return [.. list.Select(ToDto)];
    }

    /// <summary>Legacy alias for ListAdminAreasAsync with no filters.</summary>
    public Task<IReadOnlyList<CustomAreaDto>> ListAsync(CancellationToken cancellationToken = default) =>
        ListAdminAreasAsync(null, null, null, cancellationToken);

    public async Task<Result<CustomAreaDto>> AddAsync(
        string? governorateCode,
        string? nameAr,
        string? nameEn,
        bool isFocus,
        Guid? userId,
        CancellationToken cancellationToken = default)
    {
        await EnsureStandardAreasSeededAsync(cancellationToken);

        var governorate = EgyptReferenceData.Governorates.FirstOrDefault(g =>
            string.Equals(g.Code, governorateCode?.Trim(), StringComparison.OrdinalIgnoreCase));
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

        var builtIn = governorate.Areas.Any(a =>
            string.Equals(a.NameAr, arabic, StringComparison.OrdinalIgnoreCase)
            || string.Equals(a.NameEn, arabic, StringComparison.OrdinalIgnoreCase));

        var existsInDb = await _dbContext.CustomAreas.AnyAsync(
            a => a.GovernorateCode == governorate.Code && a.NameAr == arabic,
            cancellationToken);

        if (builtIn || existsInDb)
        {
            return Result.Failure<CustomAreaDto>(_alreadyListed);
        }

        var area = CustomArea.Create(governorate.Code, arabic, english, userId, _clock.UtcNow, isFocus);
        _dbContext.CustomAreas.Add(area);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(area));
    }

    public Task<Result<CustomAreaDto>> AddAsync(
        string? governorateCode,
        string? nameAr,
        string? nameEn,
        Guid userId,
        CancellationToken cancellationToken = default) =>
        AddAsync(governorateCode, nameAr, nameEn, false, userId, cancellationToken);

    public async Task<Result> SetActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken = default)
    {
        await EnsureStandardAreasSeededAsync(cancellationToken);

        var area = await _dbContext.CustomAreas.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (area is null)
        {
            return Result.Failure(_notFound);
        }

        area.SetActive(isActive);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> SetFocusAsync(Guid id, bool isFocus, CancellationToken cancellationToken = default)
    {
        await EnsureStandardAreasSeededAsync(cancellationToken);

        var area = await _dbContext.CustomAreas.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (area is null)
        {
            return Result.Failure(_notFound);
        }

        area.SetFocus(isFocus);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> RemoveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await EnsureStandardAreasSeededAsync(cancellationToken);

        var area = await _dbContext.CustomAreas.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (area is null)
        {
            return Result.Failure(_notFound);
        }

        _dbContext.CustomAreas.Remove(area);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private static CustomAreaDto ToDto(CustomArea a) =>
        new(a.Id, a.GovernorateCode, a.Code, a.NameAr, a.NameEn, a.IsActive, a.IsFocus, a.IsCustom, a.CreatedAtUtc);
}
