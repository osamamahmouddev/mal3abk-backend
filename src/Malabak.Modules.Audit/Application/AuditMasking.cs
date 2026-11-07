using System.Text.Json;
using System.Text.Json.Nodes;

namespace Malabak.Modules.Audit.Application;

/// <summary>
/// Serializes audit snapshots with secrets and personal data masked. Secrets (passwords, tokens, codes, keys) are fully
/// replaced; contact details and account numbers keep only their last characters so support can still tell values apart.
/// Matching is on property names, case-insensitive, at any depth.
/// </summary>
public static class AuditMasking
{
    public const string Masked = "***";

    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    private static readonly string[] _secretParts =
        ["password", "secret", "token", "otp", "totp", "pin", "apikey", "privatekey", "signature", "hash", "recoverycode", "ciphertext", "cookie"];

    private static readonly string[] _partialParts = ["phone", "email", "iban", "accountnumber", "walletnumber", "cardnumber", "nationalid", "accountdetails"];

    public static string? Serialize(object? snapshot)
    {
        if (snapshot is null)
        {
            return null;
        }

        var node = snapshot as JsonNode ?? JsonSerializer.SerializeToNode(snapshot, snapshot.GetType(), _json);
        if (node is null)
        {
            return null;
        }

        Mask(node);
        return node.ToJsonString(_json);
    }

    private static void Mask(JsonNode node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (name, value) in obj.ToList())
                {
                    var key = name.Replace("_", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
                    if (value is null)
                    {
                        continue;
                    }

                    if (_secretParts.Any(key.Contains))
                    {
                        obj[name] = Masked;
                    }
                    else if (_partialParts.Any(key.Contains) && value is JsonValue)
                    {
                        obj[name] = Partial(value.ToString());
                    }
                    else
                    {
                        Mask(value);
                    }
                }

                break;
            case JsonArray array:
                foreach (var item in array.OfType<JsonNode>())
                {
                    Mask(item);
                }

                break;
        }
    }

    /// <summary>Keeps the last 4 characters (2 for short values): "+201012345678" becomes "***5678".</summary>
    public static string Partial(string value)
    {
        if (value.Length <= 4)
        {
            return Masked;
        }

        var keep = value.Length <= 8 ? 2 : 4;
        return Masked + value[^keep..];
    }
}
