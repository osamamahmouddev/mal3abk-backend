namespace Malabak.Modules.Teams.Application.Common;

/// <summary>The shape of a team invite code: six characters from the generator's unambiguous alphabet.</summary>
public static class TeamInviteCodes
{
    public const int Length = 6;
    public const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    /// <summary>The code upper-cased and trimmed, or null when it cannot be an invite code (wrong length or characters).</summary>
    public static string? Normalize(string? value)
    {
        var code = value?.Trim().ToUpperInvariant();
        return code is { Length: Length } && code.All(c => Alphabet.Contains(c, StringComparison.Ordinal)) ? code : null;
    }
}
