namespace Malabak.Api.Endpoints;

/// <summary>
/// Lets <c>https://mal3abk.runasp.net/join/{code}</c> open the app directly (Android App Links / iOS Universal
/// Links), and serves a plain page for anyone who taps it without the app installed.
/// </summary>
public static class DeepLinkEndpoints
{
    public static void MapDeepLinkEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/.well-known/assetlinks.json", () => Results.Json(new object[]
        {
            new
            {
                relation = new[] { "delegate_permission/common.handle_all_urls" },
                target = new
                {
                    @namespace = "android_app",
                    package_name = "com.malabak.app",
                    sha256_cert_fingerprints = new[]
                    {
                        "54:73:0D:DC:94:FD:22:2D:8D:31:99:89:56:59:F7:A3:4D:E4:AD:C8:53:1B:8F:EC:D7:DD:C7:DE:02:C7:FA:36"
                    }
                }
            }
        })).AllowAnonymous();

        app.MapGet("/.well-known/apple-app-site-association", () => Results.Json(new
        {
            applinks = new
            {
                apps = Array.Empty<string>(),
                details = new[]
                {
                    new
                    {
                        appID = "SJA3535YVT.com.malabak.app",
                        paths = new[] { "/join/*" }
                    }
                }
            }
        }, contentType: "application/json")).AllowAnonymous();

        app.MapGet("/join/{code}", (string code) => Results.Content(JoinPageHtml(code), "text/html"))
            .WithName("TeamInviteJoinPage")
            .WithSummary("Bridge page for a shared team invite link: opens the app, or offers the stores")
            .AllowAnonymous();
    }

    private static string JoinPageHtml(string code)
    {
        var safeCode = System.Net.WebUtility.HtmlEncode(code);
        return $$"""
            <!DOCTYPE html>
            <html lang="ar" dir="rtl">
            <head>
                <meta charset="utf-8">
                <meta name="viewport" content="width=device-width, initial-scale=1">
                <title>دعوة فريق - ملعبك</title>
                <meta http-equiv="refresh" content="0; url=malabak://app/join/{{safeCode}}">
                <style>
                    body { font-family: -apple-system, system-ui, sans-serif; background: #0F1B3D; color: #fff;
                           display: flex; min-height: 100vh; align-items: center; justify-content: center;
                           margin: 0; padding: 24px; text-align: center; }
                    .card { max-width: 360px; }
                    h1 { font-size: 20px; margin-bottom: 8px; }
                    p { color: #C5CCDB; font-size: 14px; line-height: 1.6; }
                    a.btn { display: inline-block; margin-top: 16px; padding: 12px 24px; background: #D63B2F;
                            color: #fff; text-decoration: none; border-radius: 12px; font-weight: 700; }
                </style>
                <script>
                    location.href = "malabak://app/join/{{safeCode}}";
                </script>
            </head>
            <body>
                <div class="card">
                    <h1>بتفتح دعوة الفريق…</h1>
                    <p>لو التطبيق متثبت هيفتح دلوقتي. لو مفتحش، نزّل تطبيق ملعبك وافتح اللينك تاني.</p>
                    <a class="btn" href="malabak://app/join/{{safeCode}}">افتح التطبيق</a>
                </div>
            </body>
            </html>
            """;
    }
}
