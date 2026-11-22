namespace Tamagotchi.Cache;

/// <summary>One entry of <c>CACHE_NODES</c>: the name the ring uses, and the address the client connects to.</summary>
public sealed record CacheNode(string Name, string Endpoint);

/// <summary>Reads <c>CACHE_NODES</c>, for example <c>cache-0=cache-0:6379,cache-1=cache-1:6379</c>.</summary>
public static class CacheNodes
{
    public const string VariableName = "CACHE_NODES";

    /// <summary>
    /// The nodes in <paramref name="value"/>, in the order given. An empty or missing value means no cache: every read goes
    /// to the database.
    /// </summary>
    public static IReadOnlyList<CacheNode> Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        var nodes = new List<CacheNode>();
        foreach (var raw in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = raw.IndexOf('=');
            if (separator <= 0 || separator == raw.Length - 1)
            {
                throw new FormatException($"{VariableName} entry '{raw}' must look like name=host:port.");
            }

            var name = raw[..separator].Trim();
            var endpoint = raw[(separator + 1)..].Trim();
            if (name.Length == 0 || endpoint.Length == 0)
            {
                throw new FormatException($"{VariableName} entry '{raw}' must look like name=host:port.");
            }

            if (nodes.Any(n => string.Equals(n.Name, name, StringComparison.Ordinal)))
            {
                throw new FormatException($"{VariableName} lists node '{name}' twice.");
            }

            nodes.Add(new CacheNode(name, endpoint));
        }

        return nodes;
    }
}
