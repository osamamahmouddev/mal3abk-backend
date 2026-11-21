using System.Net;
using System.Security.Cryptography;
using System.Text;
using Malabak.Modules.Teams.Application.Common;

namespace Malabak.Api.Endpoints;

/// <summary>
/// The web side of a shared team invite link (<c>/join/{code}</c>). A phone with the app opens the link in the app; any
/// other browser lands here and gets a button that hands the code to the app (<c>malabak://app/join/{code}</c>) plus the
/// code to type. The page reveals nothing about the team: it only echoes a code that has the invite-code shape, never
/// looks it up, runs no script, and cannot be framed. Joining always happens in the app, signed in, after the player
/// sees the team and confirms.
/// </summary>
public static class InviteLinkEndpoints
{
    public const string AppScheme = "malabak";
    public const string AppHost = "app";

    /// <summary>The page's only styles; the policy allows exactly this text by its hash.</summary>
    private const string _style =
        "body{margin:0;min-height:100vh;display:flex;align-items:center;justify-content:center;background:#F3F5F9;font-family:system-ui,-apple-system,\"Segoe UI\",Tahoma,sans-serif;color:#0F1B3D}" +
        "main{width:min(92vw,380px);background:#fff;border-radius:24px;padding:28px 22px;text-align:center;box-shadow:0 12px 32px rgba(15,27,61,.12)}" +
        "h1{font-size:22px;margin:0 0 8px}" +
        "p{color:#5B6478;line-height:1.6;margin:0 0 20px}" +
        ".code{display:inline-block;letter-spacing:6px;font-size:28px;font-weight:700;background:#0F1B3D;color:#F5B62B;padding:10px 18px;border-radius:14px;margin-bottom:22px;direction:ltr}" +
        "a{display:block;background:#D63B2F;color:#fff;text-decoration:none;font-weight:700;padding:14px;border-radius:14px}" +
        "small{display:block;margin-top:16px;color:#8A94AA}";

    private static readonly string _styleHash =
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(_style)));

    public static void MapInviteLinkEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/join/{code}", (string code, HttpContext http) =>
        {
            var normalized = TeamInviteCodes.Normalize(code);
            if (normalized is null)
            {
                return Results.NotFound();
            }

            var headers = http.Response.Headers;
            headers.ContentSecurityPolicy =
                $"default-src 'none'; style-src 'sha256-{_styleHash}'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";
            headers.CacheControl = "no-store";
            headers["X-Robots-Tag"] = "noindex, nofollow";
            headers["Referrer-Policy"] = "no-referrer";
            return Results.Content(Page(normalized), "text/html; charset=utf-8");
        })
        .AllowAnonymous()
        .ExcludeFromDescription();
    }

    /// <summary>The landing page for an already validated code (letters and digits only).</summary>
    public static string Page(string code)
    {
        var safe = WebUtility.HtmlEncode(code);
        return $$"""
            <!doctype html>
            <html lang="ar" dir="rtl">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <meta name="robots" content="noindex, nofollow">
            <title>ملعبك | دعوة فريق</title>
            <style>{{_style}}</style>
            </head>
            <body>
            <main>
            <h1>اتعزمت على فريق في ملعبك</h1>
            <p>افتح التطبيق وشوف الفريق قبل ما تنضم.</p>
            <div class="code">{{safe}}</div>
            <a href="{{AppScheme}}://{{AppHost}}/join/{{safe}}">افتح في تطبيق ملعبك</a>
            <small>لو التطبيق مش عندك، نزّله وادخل الكود من "انضم بكود" في صفحة فرقي.</small>
            </main>
            </body>
            </html>
            """;
    }
}
