namespace Malabak.Modules.Identity.Application.Common;

public interface ISmsAlertService
{
    Task SendSecurityAlertAsync(string phoneNumber, string message, CancellationToken cancellationToken = default);
}
