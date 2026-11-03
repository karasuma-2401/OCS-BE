using System.Text.RegularExpressions;

namespace OCS.Api.Common;

/// <summary>The version-one ASCII email identity policy, shared by every tracked account write.</summary>
public static partial class EmailIdentity
{
    public const int MaxLength = 254;

    // Kept verbatim in the immutable backfill migration and preflight SQL.
    public const string Pattern = "^[A-Za-z0-9!#$%&'*+/=?^_`{|}~-]+(?:\\.[A-Za-z0-9!#$%&'*+/=?^_`{|}~-]+)*@[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?(?:\\.[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?)+$";

    // The C collation makes the PostgreSQL ASCII operation match ToUpperInvariant exactly.
    public static string SqlNormalization => $$"""
        CASE WHEN length(btrim("Email", E' \t\n\r\013\f')) <= 254
            AND length(split_part(btrim("Email", E' \t\n\r\013\f'), '@', 1)) <= 64
            AND btrim("Email", E' \t\n\r\013\f') COLLATE "C" ~ '{{Pattern.Replace("'", "''", StringComparison.Ordinal)}}'
        THEN upper(btrim("Email", E' \t\n\r\013\f') COLLATE "C") ELSE NULL END
        """;

    /// <summary>Returns null for missing/unsupported legacy identities; never invents an address.</summary>
    public static string? Normalize(string? email)
    {
        if (email is null || email.Any(c => c > 127))
        {
            return null;
        }

        var trimmed = email.Trim(' ', '\t', '\n', '\r', '\v', '\f');
        if (trimmed.Length > MaxLength || trimmed.IndexOf('@') > 64 || !EmailPattern().IsMatch(trimmed))
        {
            return null;
        }

        return trimmed.ToUpperInvariant();
    }

    [GeneratedRegex(Pattern, RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex EmailPattern();
}
