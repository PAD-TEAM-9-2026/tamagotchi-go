using System.Collections;
using Microsoft.Extensions.Logging;

namespace Tamagotchi.Discovery;

/// <summary>
/// A log entry with a fixed message and named fields, so a JSON formatter that writes the state's values (as the
/// services' own formatter does) puts each field on the line, and any other formatter still prints the message.
/// </summary>
public sealed class DiscoveryLogState : IReadOnlyList<KeyValuePair<string, object?>>
{
    private readonly KeyValuePair<string, object?>[] _fields;

    public DiscoveryLogState(string message, (string Name, object? Value)[] fields)
    {
        Message = message;
        _fields = fields.Select(field => new KeyValuePair<string, object?>(field.Name, field.Value)).ToArray();
    }

    public string Message { get; }

    public int Count => _fields.Length;

    public KeyValuePair<string, object?> this[int index] => _fields[index];

    public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() =>
        ((IEnumerable<KeyValuePair<string, object?>>)_fields).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public override string ToString() => Message;
}

public static class DiscoveryLog
{
    public static void LogDiscovery(
        this ILogger logger, LogLevel level, string message, params (string Name, object? Value)[] fields)
    {
        if (logger.IsEnabled(level))
        {
            logger.Log(level, 0, new DiscoveryLogState(message, fields), null, static (state, _) => state.Message);
        }
    }
}
