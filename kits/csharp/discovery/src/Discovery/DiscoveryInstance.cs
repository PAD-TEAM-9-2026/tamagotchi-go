using System.Reflection;

namespace Tamagotchi.Discovery;

/// <summary>
/// This process as Service Discovery knows it: one UUIDv7 made at startup, the version it runs and when it started.
/// The same id goes into the registration and into every <c>/status</c> answer.
/// </summary>
public sealed class DiscoveryInstance
{
    public DiscoveryInstance(Guid id, string version, DateTimeOffset startedAt)
    {
        Id = id;
        Version = version;
        StartedAt = startedAt;
    }

    public Guid Id { get; }

    public string Version { get; }

    public DateTimeOffset StartedAt { get; }

    public static DiscoveryInstance Create(TimeProvider time)
    {
        var now = time.GetUtcNow();
        return new DiscoveryInstance(Guid.CreateVersion7(now), EntryVersion(), now);
    }

    /// <summary>
    /// The version of the entry assembly, which the service sets from its <c>VERSION</c> file (see the README), without
    /// the <c>+commit</c> part the build may add. <c>0.0.0</c> when there is none.
    /// </summary>
    public static string EntryVersion(Assembly? assembly = null)
    {
        var informational = (assembly ?? Assembly.GetEntryAssembly())
            ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        if (string.IsNullOrWhiteSpace(informational))
        {
            return "0.0.0";
        }

        var plus = informational.IndexOf('+', StringComparison.Ordinal);
        return plus >= 0 ? informational[..plus] : informational;
    }
}
