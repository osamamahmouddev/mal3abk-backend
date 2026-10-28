using Malabak.Modules.Identity.Application.Common;
using Malabak.Modules.Identity.Application.Sessions;
using Malabak.Modules.Identity.Infrastructure;
using Malabak.Modules.Identity.Infrastructure.Services;
using Malabak.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Malabak.UnitTests;

/// <summary>Builds a real SessionService over an in-memory IdentityDbContext (no Redis: the store falls back to the DB).</summary>
internal static class TestSessions
{
    public static SessionService Create(IdentityDbContext db, IClock clock, TestDevice? device = null, RecordingAlerts? alerts = null)
    {
        alerts ??= new RecordingAlerts();
        return new SessionService(db, device ?? new TestDevice(), Store(db), alerts, alerts, clock, NullLogger<SessionService>.Instance);
    }

    public static RedisSessionStore Store(IdentityDbContext db) =>
        new(db, new ServiceCollection().BuildServiceProvider(), NullLogger<RedisSessionStore>.Instance);
}

internal sealed class TestDevice : IDeviceContext
{
    public string? DeviceId { get; set; } = "device-1";
    public string? DeviceName { get; set; } = "Pixel 8";
    public string? IpAddress { get; set; } = "10.0.0.1";
    public string? UserAgent { get; set; } = "Malabak/1.0 (Android)";
}

internal sealed class RecordingAlerts : ISmsAlertService, IEmailSender
{
    public List<string> Messages { get; } = [];

    public Task SendSecurityAlertAsync(string phoneNumber, string message, CancellationToken cancellationToken = default)
    {
        Messages.Add(message);
        return Task.CompletedTask;
    }

    public Task SendAsync(string toAddress, string subject, string body, CancellationToken cancellationToken = default)
    {
        Messages.Add(body);
        return Task.CompletedTask;
    }
}
