using System.Xml.Linq;
using Malabak.SharedKernel;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Api.Infrastructure.Security;

/// <summary>
/// Stores the Data Protection key ring in PostgreSQL so all instances share it and a redeploy doesn't
/// invalidate protected payloads. The framework interface is synchronous, hence the sync EF calls.
/// </summary>
public sealed class SecurityDbXmlRepository : IXmlRepository
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IClock _clock;

    public SecurityDbXmlRepository(IServiceScopeFactory scopeFactory, IClock clock)
    {
        _scopeFactory = scopeFactory;
        _clock = clock;
    }

    public IReadOnlyCollection<XElement> GetAllElements()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SecurityDbContext>();

        return db.DataProtectionKeys
            .AsNoTracking()
            .OrderBy(k => k.Id)
            .Select(k => k.Xml)
            .AsEnumerable()
            .Select(XElement.Parse)
            .ToList();
    }

    public void StoreElement(XElement element, string friendlyName)
    {
        ArgumentNullException.ThrowIfNull(element);

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SecurityDbContext>();

        db.DataProtectionKeys.Add(new DataProtectionKeyRecord
        {
            FriendlyName = friendlyName,
            Xml = element.ToString(SaveOptions.DisableFormatting),
            CreatedAtUtc = _clock.UtcNow
        });
        db.SaveChanges();
    }
}
