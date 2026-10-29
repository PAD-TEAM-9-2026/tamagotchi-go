using System.Globalization;

namespace Tamagotchi.Discovery;

/// <summary>Where the Gateway is, how long a call to it may take, and the breaker rules for those calls.</summary>
public sealed record GatewaySettings(Uri Url, TimeSpan Timeout, BreakerSettings Breaker)
{
    public const string UrlVariable = "GATEWAY_URL";
    public const string TimeoutVariable = "GATEWAY_TIMEOUT_MS";
    public const string ThresholdVariable = "BREAKER_THRESHOLD";
    public const string MultiplierVariable = "BREAKER_WINDOW_MULTIPLIER";
    public const string ResetVariable = "BREAKER_RESET_SECONDS";

    public const string DefaultUrl = "http://gateway:3000";
    public const double DefaultTimeoutMs = 5000;
    public const int DefaultThreshold = 3;
    public const double DefaultMultiplier = 3.5;
    public const double DefaultResetSeconds = 30;

    /// <summary>
    /// The settings from the environment. A blank value means the default. The window is the timeout times the
    /// multiplier, 17.5 seconds by default.
    /// </summary>
    /// <exception cref="InvalidOperationException">A value is set but not valid, so the service does not start.</exception>
    public static GatewaySettings FromEnvironment(Func<string, string?>? lookup = null)
    {
        lookup ??= Environment.GetEnvironmentVariable;

        var url = Read(lookup, UrlVariable) ?? DefaultUrl;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException($"{UrlVariable} must be an absolute http or https URL.");
        }

        var timeoutMs = Positive(lookup, TimeoutVariable, DefaultTimeoutMs);
        var multiplier = Positive(lookup, MultiplierVariable, DefaultMultiplier);
        var resetSeconds = Positive(lookup, ResetVariable, DefaultResetSeconds);
        var threshold = Positive(lookup, ThresholdVariable, DefaultThreshold);
        if (threshold != Math.Floor(threshold))
        {
            throw new InvalidOperationException($"{ThresholdVariable} must be a whole number.");
        }

        return new GatewaySettings(
            uri,
            TimeSpan.FromMilliseconds(timeoutMs),
            new BreakerSettings(
                (int)threshold,
                TimeSpan.FromMilliseconds(timeoutMs * multiplier),
                TimeSpan.FromSeconds(resetSeconds)));
    }

    private static double Positive(Func<string, string?> lookup, string name, double fallback)
    {
        var text = Read(lookup, name);
        if (text is null)
        {
            return fallback;
        }

        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            || !double.IsFinite(value) || value <= 0)
        {
            throw new InvalidOperationException($"{name} must be a positive number.");
        }

        return value;
    }

    private static string? Read(Func<string, string?> lookup, string name)
    {
        var value = lookup(name)?.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }
}
