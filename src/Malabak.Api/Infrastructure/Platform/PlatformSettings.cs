using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Globalization;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Api.Infrastructure.Platform;

public enum SettingType
{
    Integer = 0,
    Boolean = 1,
    Text = 2
}

/// <summary>A setting the platform knows: its type, default and allowed range. Unknown keys cannot be stored.</summary>
public sealed record SettingDefinition(string Key, SettingType Type, string Default, string Description, int? Min = null, int? Max = null, int? MaxLength = null);

public sealed record FlagDefinition(string Key, string Description, bool Public = false);

/// <summary>Typed settings and flags with their defaults (TASK-712).</summary>
public static class PlatformCatalog
{
    public static readonly IReadOnlyList<SettingDefinition> Settings =
    [
        new(PlatformSettingKeys.HoldMinutes, SettingType.Integer, "30", "مهلة تثبيت الحجز الافتراضية لحين رفع إشعار العربون (للملاعب الجديدة).", 5, 120),
        new(PlatformSettingKeys.ProofReviewMinutes, SettingType.Integer, "120", "مهلة مراجعة إشعار العربون الافتراضية للملعب (للملاعب الجديدة).", 15, 1440),
        new(PlatformSettingKeys.OtpMaxPerHour, SettingType.Integer, "5", "الحد الأقصى لرموز التحقق (OTP) لكل رقم هاتف في الساعة.", 1, 20),
        new(PlatformSettingKeys.OtpResendCooldownSeconds, SettingType.Integer, "60", "فترة الانتظار بالثواني قبل إعادة طلب رمز التحقق.", 30, 600),
        new(PlatformSettingKeys.RankingWinPoints, SettingType.Integer, "3", "نقاط تصنيف الفرق عند الفوز.", 0, 10),
        new(PlatformSettingKeys.RankingDrawPoints, SettingType.Integer, "1", "نقاط تصنيف الفرق عند التعادل.", 0, 10),
        new(PlatformSettingKeys.RankingLossPoints, SettingType.Integer, "0", "نقاط تصنيف الفرق عند الخسارة.", 0, 10),
        new(PlatformSettingKeys.MaintenanceEnabled, SettingType.Boolean, "false", "وضع الصيانة: إيقاف طلبات المستخدمين وعرض رسالة الصيانة في التطبيقات."),
        new(PlatformSettingKeys.MaintenanceMessage, SettingType.Text, "نعمل على تحسين الخدمة وهنرجع قريب.", "نص الرسالة المعروضة للمستخدمين أثناء فترة الصيانة.", MaxLength: 300),
    ];

    public static readonly IReadOnlyList<FlagDefinition> Flags =
    [
        new(FeatureFlagKeys.SponsoredHomeItems, "عناصر وإعلانات ممولة في الصفحة الرئيسية لتطبيق اللاعبين.", Public: true),
        new(FeatureFlagKeys.FeaturedVenues, "قسم الملاعب المميزة المختارة بواسطة الإدارة.", Public: true),
        new(FeatureFlagKeys.PaymentGateway, "الدفع الإلكتروني للعربون عبر بوابة دفع."),
        new(FeatureFlagKeys.Tournaments, "نظام البطولات والدوريات.", Public: true),
        new(FeatureFlagKeys.Reviews, "تقييمات ومراجعات الملاعب.", Public: true),
        new(FeatureFlagKeys.Referrals, "برنامج ومكافآت الإحالة.", Public: true),
    ];

    private static string BoolText(bool value) => value ? "true" : "false";

    public static SettingDefinition? Setting(string key) => Settings.FirstOrDefault(s => s.Key == key);

    public static FlagDefinition? Flag(string key) => Flags.FirstOrDefault(f => f.Key == key);

    /// <summary>Normalized value when valid for the definition, otherwise null.</summary>
    public static string? Normalize(SettingDefinition definition, string? value)
    {
        var raw = value?.Trim() ?? string.Empty;
        return definition.Type switch
        {
            SettingType.Integer => int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
                && (definition.Min is null || n >= definition.Min) && (definition.Max is null || n <= definition.Max)
                ? n.ToString(CultureInfo.InvariantCulture)
                : null,
            SettingType.Boolean => bool.TryParse(raw, out var b) ? BoolText(b) : null,
            _ => raw.Length > 0 && raw.Length <= (definition.MaxLength ?? 1000) ? raw : null
        };
    }
}

public sealed class PlatformSetting
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public DateTime UpdatedAtUtc { get; set; }
    public Guid? UpdatedBy { get; set; }
}

public sealed class FeatureFlag
{
    public string Key { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public Guid? UpdatedBy { get; set; }
}

public sealed class PlatformDbContext : DbContext
{
    public const string Schema = "platform";

    public DbSet<PlatformSetting> Settings => Set<PlatformSetting>();
    public DbSet<FeatureFlag> Flags => Set<FeatureFlag>();

    public PlatformDbContext(DbContextOptions<PlatformDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.Entity<PlatformSetting>(b =>
        {
            b.ToTable("settings");
            b.HasKey(s => s.Key);
            b.Property(s => s.Key).HasMaxLength(100);
            b.Property(s => s.Value).HasMaxLength(1000).IsRequired();
        });
        modelBuilder.Entity<FeatureFlag>(b =>
        {
            b.ToTable("feature_flags");
            b.HasKey(f => f.Key);
            b.Property(f => f.Key).HasMaxLength(100);
        });
    }
}

public sealed record SettingDto(string Key, string Type, string Value, string Default, bool IsDefault, string Description, int? Min, int? Max, int? MaxLength, DateTime? UpdatedAtUtc, Guid? UpdatedBy);

public sealed record FlagDto(string Key, bool Enabled, string Description, bool Public, DateTime? UpdatedAtUtc, Guid? UpdatedBy);

public sealed record PlatformStatusDto(bool Maintenance, string? Message, IReadOnlyList<string> EnabledFeatures);

public static class PlatformErrors
{
    public static readonly Error UnknownSetting = new("Platform.UnknownSetting", "This setting does not exist.");
    public static readonly Error InvalidValue = new("Platform.InvalidValue", "The value is not valid for this setting (check its type and range).");
    public static readonly Error UnknownFlag = new("Platform.UnknownFlag", "This feature flag does not exist.");
}

/// <summary>
/// Settings and flags service. Reads are cached per instance for <see cref="CacheLifetime"/> (other instances see a change
/// within that time); writes are validated against the catalog and audited with before/after values.
/// </summary>
public sealed class PlatformService : IPlatformSettings, IFeatureFlags
{
    public static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<string, (string Value, DateTime At)> _settingCache;
    private readonly ConcurrentDictionary<string, (bool Value, DateTime At)> _flagCache;

    private readonly PlatformDbContext _dbContext;
    private readonly IAuditLog _audit;
    private readonly IClock _clock;
    private readonly ILogger<PlatformService>? _logger;

    public PlatformService(
        PlatformDbContext dbContext, IAuditLog audit, IClock clock, ILogger<PlatformService>? logger = null, PlatformSettingsCache? cache = null)
    {
        // The host shares one cache (singleton); a service built without one (tests) gets its own.
        cache ??= new PlatformSettingsCache();
        _settingCache = cache.Settings;
        _flagCache = cache.Flags;
        _dbContext = dbContext;
        _audit = audit;
        _clock = clock;
        _logger = logger;
    }

    public async Task<int> GetIntAsync(string key, CancellationToken cancellationToken = default) =>
        int.Parse(await ValueAsync(key, cancellationToken), CultureInfo.InvariantCulture);

    public async Task<bool> GetBoolAsync(string key, CancellationToken cancellationToken = default) =>
        await ValueAsync(key, cancellationToken) == "true";

    public Task<string> GetStringAsync(string key, CancellationToken cancellationToken = default) => ValueAsync(key, cancellationToken);

    public async Task<bool> IsEnabledAsync(string key, CancellationToken cancellationToken = default)
    {
        if (PlatformCatalog.Flag(key) is null)
        {
            return false;
        }

        if (_flagCache.TryGetValue(key, out var cached) && _clock.UtcNow - cached.At < CacheLifetime)
        {
            return cached.Value;
        }

        bool enabled;
        try
        {
            enabled = await _dbContext.Flags.AsNoTracking().Where(f => f.Key == key).Select(f => f.Enabled).FirstOrDefaultAsync(cancellationToken);
        }
        catch (Exception ex) when (IsStoreFailure(ex))
        {
            // A settings read must never take the API down: keep the last known value (or "off") until the store is back.
            _logger?.LogError(ex, "Could not read feature flag {Flag}; using the last known value", key);
            enabled = _flagCache.TryGetValue(key, out var last) && last.Value;
        }

        _flagCache[key] = (enabled, _clock.UtcNow);
        return enabled;
    }

    public async Task<IReadOnlyList<SettingDto>> ListSettingsAsync(CancellationToken cancellationToken = default)
    {
        var stored = await _dbContext.Settings.AsNoTracking().ToDictionaryAsync(s => s.Key, cancellationToken);
        return PlatformCatalog.Settings.Select(d =>
        {
            stored.TryGetValue(d.Key, out var row);
            return new SettingDto(d.Key, d.Type.ToString(), row?.Value ?? d.Default, d.Default, row is null || row.Value == d.Default, d.Description,
                d.Min, d.Max, d.MaxLength, row?.UpdatedAtUtc, row?.UpdatedBy);
        }).ToList();
    }

    public async Task<IReadOnlyList<FlagDto>> ListFlagsAsync(CancellationToken cancellationToken = default)
    {
        var stored = await _dbContext.Flags.AsNoTracking().ToDictionaryAsync(f => f.Key, cancellationToken);
        return PlatformCatalog.Flags.Select(d =>
        {
            stored.TryGetValue(d.Key, out var row);
            return new FlagDto(d.Key, row?.Enabled ?? false, d.Description, d.Public, row?.UpdatedAtUtc, row?.UpdatedBy);
        }).ToList();
    }

    public async Task<Result<SettingDto>> UpdateSettingAsync(string key, string? value, Guid adminId, string? reason, CancellationToken cancellationToken = default)
    {
        var definition = PlatformCatalog.Setting(key);
        if (definition is null)
        {
            return Result.Failure<SettingDto>(PlatformErrors.UnknownSetting);
        }

        var normalized = PlatformCatalog.Normalize(definition, value);
        if (normalized is null)
        {
            return Result.Failure<SettingDto>(PlatformErrors.InvalidValue);
        }

        var row = await _dbContext.Settings.FirstOrDefaultAsync(s => s.Key == key, cancellationToken);
        var before = row?.Value ?? definition.Default;
        if (row is null)
        {
            row = new PlatformSetting { Key = key };
            _dbContext.Settings.Add(row);
        }

        row.Value = normalized;
        row.UpdatedAtUtc = _clock.UtcNow;
        row.UpdatedBy = adminId;
        await _dbContext.SaveChangesAsync(cancellationToken);
        _settingCache.TryRemove(key, out _);

        await _audit.RecordAsync(new AuditEntry("platform.setting_changed", "PlatformSetting", key, adminId, AuditActorType.Admin,
            new { Value = before }, new { Value = normalized }, reason), cancellationToken);
        return Result.Success((await ListSettingsAsync(cancellationToken)).First(s => s.Key == key));
    }

    public async Task<Result<FlagDto>> UpdateFlagAsync(string key, bool enabled, Guid adminId, string? reason, CancellationToken cancellationToken = default)
    {
        if (PlatformCatalog.Flag(key) is null)
        {
            return Result.Failure<FlagDto>(PlatformErrors.UnknownFlag);
        }

        var row = await _dbContext.Flags.FirstOrDefaultAsync(f => f.Key == key, cancellationToken);
        var before = row?.Enabled ?? false;
        if (row is null)
        {
            row = new FeatureFlag { Key = key };
            _dbContext.Flags.Add(row);
        }

        row.Enabled = enabled;
        row.UpdatedAtUtc = _clock.UtcNow;
        row.UpdatedBy = adminId;
        await _dbContext.SaveChangesAsync(cancellationToken);
        _flagCache.TryRemove(key, out _);

        await _audit.RecordAsync(new AuditEntry("platform.flag_changed", "FeatureFlag", key, adminId, AuditActorType.Admin,
            new { Enabled = before }, new { Enabled = enabled }, reason), cancellationToken);
        return Result.Success((await ListFlagsAsync(cancellationToken)).First(f => f.Key == key));
    }

    /// <summary>What clients need before anything else: maintenance state and which public features are on.</summary>
    public async Task<PlatformStatusDto> StatusAsync(CancellationToken cancellationToken = default)
    {
        var maintenance = await GetBoolAsync(PlatformSettingKeys.MaintenanceEnabled, cancellationToken);
        var enabled = new List<string>();
        foreach (var key in PlatformCatalog.Flags.Where(f => f.Public).Select(f => f.Key))
        {
            if (await IsEnabledAsync(key, cancellationToken))
            {
                enabled.Add(key);
            }
        }

        return new PlatformStatusDto(maintenance, maintenance ? await GetStringAsync(PlatformSettingKeys.MaintenanceMessage, cancellationToken) : null, enabled);
    }

    /// <summary>Test hook: forget cached values.</summary>
    private async Task<string> ValueAsync(string key, CancellationToken cancellationToken)
    {
        var definition = PlatformCatalog.Setting(key) ?? throw new ArgumentException($"Unknown platform setting '{key}'.", nameof(key));
        if (_settingCache.TryGetValue(key, out var cached) && _clock.UtcNow - cached.At < CacheLifetime)
        {
            return cached.Value;
        }

        string value;
        try
        {
            var stored = await _dbContext.Settings.AsNoTracking().Where(s => s.Key == key).Select(s => s.Value).FirstOrDefaultAsync(cancellationToken);
            value = stored is not null && PlatformCatalog.Normalize(definition, stored) is { } valid ? valid : definition.Default;
        }
        catch (Exception ex) when (IsStoreFailure(ex))
        {
            // Same rule as flags: fall back to the last known value or the code default (e.g. maintenance stays off).
            _logger?.LogError(ex, "Could not read platform setting {Setting}; using the last known value or the default", key);
            value = _settingCache.TryGetValue(key, out var last) ? last.Value : definition.Default;
        }

        _settingCache[key] = (value, _clock.UtcNow);
        return value;
    }

    private static bool IsStoreFailure(Exception ex) =>
        ex is System.Data.Common.DbException or TimeoutException or InvalidOperationException && ex is not ObjectDisposedException;
}

/// <summary>
/// Process-wide cache of settings and flags (30 s per entry), registered as a singleton so every request shares it. Each
/// instance is independent, so tests never see another test's values.
/// </summary>
public sealed class PlatformSettingsCache
{
    public ConcurrentDictionary<string, (string Value, DateTime At)> Settings { get; } = new();
    public ConcurrentDictionary<string, (bool Value, DateTime At)> Flags { get; } = new();
}
