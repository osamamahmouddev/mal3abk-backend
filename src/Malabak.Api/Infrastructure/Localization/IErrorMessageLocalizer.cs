using System.Globalization;

namespace Malabak.Api.Infrastructure.Localization;

public interface IErrorMessageLocalizer
{
    string GetLocalizedMessage(string errorCode, string? defaultDescription = null, CultureInfo? culture = null);
    string GetLocalizedTitle(string errorTypeOrCode, CultureInfo? culture = null);
}
