using System.Text.RegularExpressions;

namespace GameControlMapper.Services;

/// <summary>
/// Defense-in-depth filter for production logs and diagnostic exports.
/// Input history is discarded rather than redacted because numeric values can
/// still reveal a user's key sequence or pointing behavior.
/// </summary>
public static partial class ProductionLogPrivacy
{
    public static bool ContainsInputHistory(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        return InputHistoryPattern().IsMatch(value);
    }

    public static string FilterDiagnosticText(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var safeLines = value.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')
            .Where(line => !ContainsInputHistory(line))
            .Select(FileLogSink.Redact);
        return string.Join(Environment.NewLine, safeLines);
    }

    [GeneratedRegex(
        @"virtual[\s_-]*key|vk[\s_-]*code|scan[\s_-]*code|key[\s_-]*(down|up)|mouse[\s_-]*(down|up)|pressed[\s_-]*keys?|handlepressedinput|handlereleasedinput|joystick.*(coordinate|center|scaled|\bx\s*[:=]|\by\s*[:=])|camera.*(delta|\bdx\b|\bdy\b)|touch.*(coordinate|\bx\s*[:=]|\by\s*[:=])|profile\s*point|scheduler.*frame|\bframe\s+\d+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InputHistoryPattern();
}
