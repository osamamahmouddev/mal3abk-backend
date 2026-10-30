using System.Security.Cryptography;
using Malabak.Api.Infrastructure.Security;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Malabak.IntegrationTests.Infrastructure.Security;

/// <summary>
/// Builds one "node" of the encryption stack over a shared in-memory database, so tests can simulate
/// several API instances or a KEK change by creating more hosts against the same database name.
/// </summary>
internal sealed class EncryptionTestHost : IDisposable
{
    private readonly ServiceProvider _provider;

    public EncryptionTestHost(string databaseName, EncryptionOptions options, MutableClock clock)
    {
        Options = options;
        Clock = clock;

        var services = new ServiceCollection();
        services.AddDbContext<SecurityDbContext>(o => o
            .UseInMemoryDatabase(databaseName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
        _provider = services.BuildServiceProvider();

        var wrapped = Microsoft.Extensions.Options.Options.Create(options);
        KekProvider = new LocalKeyEncryptionKeyProvider(wrapped, NullLogger<LocalKeyEncryptionKeyProvider>.Instance);
        KeyRing = new DataKeyRing(_provider.GetRequiredService<IServiceScopeFactory>(), KekProvider, clock, wrapped, NullLogger<DataKeyRing>.Instance);
        Cipher = new AesGcmFieldCipher(KeyRing);
    }

    public EncryptionOptions Options { get; }
    public MutableClock Clock { get; }
    public LocalKeyEncryptionKeyProvider KekProvider { get; }
    public DataKeyRing KeyRing { get; }
    public IFieldCipher Cipher { get; }

    public static string NewKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(EncryptionOptions.KeySizeBytes));

    public static EncryptionOptions OptionsWith(string currentKekId, params (string Id, string Key)[] keks) => new()
    {
        CurrentKeyEncryptionKeyId = currentKekId,
        KeyEncryptionKeys = keks.ToDictionary(k => k.Id, k => k.Key, StringComparer.Ordinal),
        BlindIndexKey = NewKey(),
        KeyRingRefreshMinutes = 10
    };

    public SecurityDbContext CreateDbContext() => _provider.CreateScope().ServiceProvider.GetRequiredService<SecurityDbContext>();

    public IServiceScopeFactory ScopeFactory => _provider.GetRequiredService<IServiceScopeFactory>();

    public void Dispose()
    {
        KeyRing.Dispose();
        _provider.Dispose();
    }
}

internal sealed class MutableClock : IClock
{
    public DateTime UtcNow { get; set; } = new(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);
    public DateTime CairoNow => UtcNow.AddHours(3);
    public DateOnly CairoToday => DateOnly.FromDateTime(CairoNow);
    public TimeZoneInfo CairoTimeZone => TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");
}
