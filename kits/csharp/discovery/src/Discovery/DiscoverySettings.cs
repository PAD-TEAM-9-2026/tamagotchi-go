using System.Net;

namespace Tamagotchi.Discovery;

/// <summary>Where Service Discovery is and which host this instance registers with.</summary>
public sealed record DiscoverySettings(Uri Url, string Host)
{
    public const string EnabledVariable = "SERVICE_DISCOVERY_ENABLED";
    public const string UrlVariable = "SERVICE_DISCOVERY_URL";
    public const string HostVariable = "INSTANCE_HOST";
    public const string DefaultUrl = "http://service-discovery:3009";

    /// <summary>
    /// The settings from the environment, or <see langword="null"/> when <c>SERVICE_DISCOVERY_ENABLED</c> is
    /// <c>false</c>. A blank value means the default: enabled, <see cref="DefaultUrl"/>, and the container hostname.
    /// </summary>
    /// <exception cref="InvalidOperationException">A value is set but not valid, so the service does not start.</exception>
    public static DiscoverySettings? FromEnvironment(Func<string, string?>? lookup = null, Func<string>? hostName = null)
    {
        lookup ??= Environment.GetEnvironmentVariable;

        var enabled = Read(lookup, EnabledVariable);
        if (enabled is not null and not ("true" or "false"))
        {
            throw new InvalidOperationException($"{EnabledVariable} must be 'true' or 'false'.");
        }

        if (enabled == "false")
        {
            return null;
        }

        var url = Read(lookup, UrlVariable) ?? DefaultUrl;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException($"{UrlVariable} must be an absolute http or https URL.");
        }

        return new DiscoverySettings(uri, Read(lookup, HostVariable) ?? (hostName ?? Dns.GetHostName)());
    }

    private static string? Read(Func<string, string?> lookup, string name)
    {
        var value = lookup(name)?.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }
}
