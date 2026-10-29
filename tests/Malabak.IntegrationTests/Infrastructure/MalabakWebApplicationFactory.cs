using Malabak.IntegrationTests.Infrastructure.Database;
using Malabak.Modules.Identity.Infrastructure;
using Malabak.Modules.Venues.Infrastructure;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Time;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Malabak.IntegrationTests.Infrastructure;

public class MalabakWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly FakeClock _fakeClock = new();

    public FakeClock Clock => _fakeClock;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseEnvironment("Development");

        builder.ConfigureTestServices(services =>
        {
            // Replace IClock with controllable FakeClock
            services.RemoveAll<IClock>();
            services.AddSingleton<IClock>(_fakeClock);
        });
    }

    public async Task ResetAllDatabasesAsync(CancellationToken cancellationToken = default)
    {
        using var scope = Services.CreateScope();

        var identityDb = scope.ServiceProvider.GetService<IdentityDbContext>();
        if (identityDb != null)
        {
            await DatabaseResetHelper.ResetDbContextAsync(identityDb, cancellationToken);
        }

        var venuesDb = scope.ServiceProvider.GetService<VenuesDbContext>();
        if (venuesDb != null)
        {
            await DatabaseResetHelper.ResetDbContextAsync(venuesDb, cancellationToken);
        }
    }
}
