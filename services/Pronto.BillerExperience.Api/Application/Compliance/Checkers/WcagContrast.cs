using System.Globalization;

namespace Pronto.BillerExperience.Api.Application.Compliance.Checkers;

/// <summary>
/// Deterministic WCAG 2.x relative-luminance and contrast-ratio math for six-digit hex colors.
/// </summary>
public static class WcagContrast
{
    /// <summary>WCAG 2.x AA minimum contrast ratio for normal-size text.</summary>
    public const double AaNormalText = 4.5;

    public static bool TryParseHex(string? value, out (double R, double G, double B) rgb)
    {
        rgb = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var span = value.Trim();
        if (span.Length != 7 || span[0] != '#')
        {
            return false;
        }

        if (!byte.TryParse(span.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r) ||
            !byte.TryParse(span.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g) ||
            !byte.TryParse(span.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
        {
            return false;
        }

        rgb = (r / 255d, g / 255d, b / 255d);
        return true;
    }

    public static double RelativeLuminance((double R, double G, double B) rgb) =>
        0.2126 * Linearize(rgb.R) + 0.7152 * Linearize(rgb.G) + 0.0722 * Linearize(rgb.B);

    public static double Ratio((double R, double G, double B) foreground, (double R, double G, double B) background)
    {
        var l1 = RelativeLuminance(foreground);
        var l2 = RelativeLuminance(background);
        var lighter = Math.Max(l1, l2);
        var darker = Math.Min(l1, l2);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double Linearize(double channel) =>
        channel <= 0.03928 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
}
