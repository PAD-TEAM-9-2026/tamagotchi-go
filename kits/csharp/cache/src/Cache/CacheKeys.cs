using System.Security.Cryptography;
using System.Text;

namespace Tamagotchi.Cache;

/// <summary>
/// Keys of the form <c>{service}:v1:{resource}:{id}[:{query hash}]</c>. The query hash is the first 16 hex characters of
/// the SHA-256 of the normalized query. Anything specific to one user must include the user id in <c>id</c>. Tokens,
/// password hashes and other secrets are never cached, so they never appear in a key either.
/// </summary>
public static class CacheKeys
{
    public static string For(string service, string resource, string id, IEnumerable<KeyValuePair<string, string?>>? query = null)
    {
        RequirePart(service, nameof(service));
        RequirePart(resource, nameof(resource));
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        var key = $"{service}:v1:{resource}:{id}";
        var normalized = query is null ? string.Empty : NormalizeQuery(query);
        return normalized.Length == 0 ? key : $"{key}:{QueryHash(normalized)}";
    }

    /// <summary>
    /// Parameters with an empty value are dropped; the rest are sorted by name, then value (ordinal), escaped and joined as
    /// <c>name=value&amp;…</c>. So the same query in any order gives the same key.
    /// </summary>
    public static string NormalizeQuery(IEnumerable<KeyValuePair<string, string?>> query)
    {
        ArgumentNullException.ThrowIfNull(query);
        return string.Join('&', query
            .Where(p => !string.IsNullOrEmpty(p.Key) && !string.IsNullOrEmpty(p.Value))
            .Select(p => (Name: Uri.EscapeDataString(p.Key), Value: Uri.EscapeDataString(p.Value!)))
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .ThenBy(p => p.Value, StringComparer.Ordinal)
            .Select(p => $"{p.Name}={p.Value}"));
    }

    public static string QueryHash(string normalizedQuery) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedQuery)))[..16];

    private static void RequirePart(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Contains(':', StringComparison.Ordinal))
        {
            throw new ArgumentException($"Cache key part '{name}' must be non-empty and contain no ':'.", name);
        }
    }
}
