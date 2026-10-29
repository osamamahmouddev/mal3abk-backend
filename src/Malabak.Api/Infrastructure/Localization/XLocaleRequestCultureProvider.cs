using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;

namespace Malabak.Api.Infrastructure.Localization;

public sealed class XLocaleRequestCultureProvider : RequestCultureProvider
{
    private const string _headerName = "X-Locale";

    public override Task<ProviderCultureResult?> DetermineProviderCultureResult(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        if (!httpContext.Request.Headers.TryGetValue(_headerName, out var values) || values.Count == 0)
        {
            return NullProviderCultureResult;
        }

        var headerValue = values.ToString().Trim();
        if (string.IsNullOrEmpty(headerValue))
        {
            return NullProviderCultureResult;
        }

        if (headerValue.StartsWith("ar", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<ProviderCultureResult?>(new ProviderCultureResult("ar-EG", "ar-EG"));
        }

        if (headerValue.StartsWith("en", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<ProviderCultureResult?>(new ProviderCultureResult("en-US", "en-US"));
        }

        return NullProviderCultureResult;
    }
}
