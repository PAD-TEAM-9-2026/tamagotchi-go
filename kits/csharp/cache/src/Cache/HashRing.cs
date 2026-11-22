using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Tamagotchi.Cache;

/// <summary>
/// The consistent-hashing ring of <c>contracts/cache.md</c>. Every node name gets <see cref="VirtualNodes"/> points: the
/// first 8 bytes of <c>MD5("{name}#{i}")</c> read as a big-endian unsigned 64-bit number. A key goes to the first point
/// that is greater than or equal to the first 8 bytes of <c>MD5(key)</c>, walking clockwise and wrapping around. Ties
/// between points go to the lower node name. The ring uses node names only, never addresses, so moving a node to another
/// host moves no keys.
/// </summary>
public sealed class HashRing
{
    public const int VirtualNodes = 128;

    private readonly ulong[] _points;
    private readonly string[] _owners;

    public HashRing(IEnumerable<string> nodeNames)
    {
        ArgumentNullException.ThrowIfNull(nodeNames);
        var names = nodeNames.ToArray();
        if (names.Length == 0)
        {
            throw new ArgumentException("A ring needs at least one node.", nameof(nodeNames));
        }

        if (names.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Node names cannot be empty.", nameof(nodeNames));
        }

        var duplicate = names.GroupBy(n => n, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new ArgumentException($"Node '{duplicate.Key}' is listed twice.", nameof(nodeNames));
        }

        var entries = names
            .SelectMany(name => Enumerable.Range(0, VirtualNodes).Select(i => (Point: Hash($"{name}#{i}"), Name: name)))
            .OrderBy(e => e.Point)
            .ThenBy(e => e.Name, StringComparer.Ordinal)
            .ToArray();

        _points = entries.Select(e => e.Point).ToArray();
        _owners = entries.Select(e => e.Name).ToArray();
        Nodes = names.OrderBy(n => n, StringComparer.Ordinal).ToArray();
    }

    /// <summary>The node names on the ring, in ordinal order.</summary>
    public IReadOnlyList<string> Nodes { get; }

    /// <summary>The name of the node that owns <paramref name="key"/>.</summary>
    public string NodeFor(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var hash = Hash(key);

        // First index whose point is >= hash. Equal points are ordered by name, so the lower name is found first.
        int lo = 0, hi = _points.Length;
        while (lo < hi)
        {
            var mid = lo + ((hi - lo) / 2);
            if (_points[mid] < hash)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        return _owners[lo == _points.Length ? 0 : lo];
    }

    /// <summary>The first 8 bytes of the MD5 of the UTF-8 text, as a big-endian unsigned number.</summary>
    public static ulong Hash(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Span<byte> digest = stackalloc byte[MD5.HashSizeInBytes];
#pragma warning disable CA5351 // MD5 only spreads keys over the ring (contracts/cache.md); it protects nothing.
        MD5.HashData(Encoding.UTF8.GetBytes(text), digest);
#pragma warning restore CA5351
        return BinaryPrimitives.ReadUInt64BigEndian(digest);
    }
}
