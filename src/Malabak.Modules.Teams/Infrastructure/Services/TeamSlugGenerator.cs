using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Malabak.Modules.Teams.Domain;

namespace Malabak.Modules.Teams.Infrastructure.Services;

public sealed partial class TeamSlugGenerator : ITeamSlugGenerator
{
    [GeneratedRegex(@"[^a-z0-9\s-]", RegexOptions.Compiled)]
    private static partial Regex DisallowedCharactersRegex();

    [GeneratedRegex(@"\s+", RegexOptions.Compiled)]
    private static partial Regex WhitespaceRegex();

    public string GenerateSlug(string teamName)
    {
        if (string.IsNullOrWhiteSpace(teamName))
        {
            return $"team-{RandomNumberGenerator.GetHexString(3).ToLowerInvariant()}";
        }

        var lower = teamName.Trim().ToLowerInvariant();
        var cleaned = DisallowedCharactersRegex().Replace(lower, "");
        var slug = WhitespaceRegex().Replace(cleaned, "-").Trim('-');

        if (string.IsNullOrWhiteSpace(slug))
        {
            slug = "team";
        }

        var suffix = RandomNumberGenerator.GetHexString(2).ToLowerInvariant();
        return $"{slug}-{suffix}";
    }
}
