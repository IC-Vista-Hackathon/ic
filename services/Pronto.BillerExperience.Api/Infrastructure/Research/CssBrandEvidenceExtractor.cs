using System.Globalization;
using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;

namespace Pronto.BillerExperience.Api.Infrastructure.Research;

public sealed record BrandStylesheet(Uri SourceUrl, string Content);

public static class BrandStylesheetDiscovery
{
    public static IReadOnlyList<Uri> Extract(string html, Uri pageUri)
    {
        var document = new HtmlParser().ParseDocument(html);
        return document.QuerySelectorAll("link[rel][href]")
            .Where(link => link.GetAttribute("rel")!
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Contains("stylesheet", StringComparer.OrdinalIgnoreCase))
            .Select(link => Resolve(link.GetAttribute("href"), pageUri))
            .OfType<Uri>()
            .DistinctBy(uri => uri.AbsoluteUri, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static Uri? Resolve(string? href, Uri pageUri) =>
        !string.IsNullOrWhiteSpace(href) &&
        Uri.TryCreate(pageUri, href, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps &&
        uri.Host.Equals(pageUri.Host, StringComparison.OrdinalIgnoreCase)
            ? new UriBuilder(uri) { Fragment = string.Empty }.Uri
            : null;
}

internal static partial class CssBrandEvidenceExtractor
{
    internal sealed record ColorEvidence(string Value, Uri SourceUrl, double Confidence);
    internal sealed record Palette(ColorEvidence? Primary, ColorEvidence? Secondary);

    private enum Role { General, Primary, Secondary }
    private sealed record Candidate(string Value, Uri SourceUrl, Role Role, int Score);
    private sealed record RankedCandidate(
        string Value,
        int Score,
        Candidate Best,
        IReadOnlySet<Role> Roles);

    internal static Palette Extract(
        string? themeColor,
        string html,
        Uri pageUri,
        IReadOnlyList<BrandStylesheet> stylesheets)
    {
        var candidates = new List<Candidate>();
        if (NormalizeColor(themeColor) is { } theme && !BrandEvidenceExtractor.IsNeutral(theme))
        {
            candidates.Add(new Candidate(theme, pageUri, Role.Primary, 10_000));
        }

        AddCssCandidates(candidates, html, pageUri);
        foreach (var stylesheet in stylesheets)
        {
            AddCssCandidates(candidates, stylesheet.Content, stylesheet.SourceUrl);
        }

        var ranked = candidates
            .GroupBy(candidate => candidate.Value, StringComparer.OrdinalIgnoreCase)
            .Select(group => new RankedCandidate(
                group.Key,
                group.Sum(candidate => candidate.Score),
                group.OrderByDescending(candidate => candidate.Score).First(),
                group.Select(candidate => candidate.Role).ToHashSet()))
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Value, StringComparer.Ordinal)
            .ToArray();

        var primary = ranked
            .Where(candidate => candidate.Roles.Contains(Role.Primary))
            .Concat(ranked.Where(candidate => !candidate.Roles.Contains(Role.Primary)))
            .FirstOrDefault();
        var secondary = ranked
            .Where(candidate => primary is null || !candidate.Value.Equals(primary.Value, StringComparison.OrdinalIgnoreCase))
            .Where(candidate => candidate.Roles.Contains(Role.Secondary))
            .Concat(ranked.Where(candidate =>
                (primary is null || !candidate.Value.Equals(primary.Value, StringComparison.OrdinalIgnoreCase)) &&
                !candidate.Roles.Contains(Role.Secondary)))
            .FirstOrDefault();

        return new Palette(ToEvidence(primary, 0.9), ToEvidence(secondary, 0.8));
    }

    private static ColorEvidence? ToEvidence(RankedCandidate? candidate, double confidence) => candidate is null
        ? null
        : new ColorEvidence(candidate.Value, candidate.Best.SourceUrl, confidence);

    private static void AddCssCandidates(List<Candidate> candidates, string css, Uri sourceUrl)
    {
        foreach (Match rule in CssRuleRegex().Matches(CommentRegex().Replace(css, string.Empty)))
        {
            var selector = rule.Groups["selector"].Value;
            var prominent = ProminentSelectorRegex().IsMatch(selector);
            foreach (Match declaration in CssDeclarationRegex().Matches(rule.Groups["body"].Value))
            {
                var property = declaration.Groups["property"].Value.Trim();
                var value = declaration.Groups["value"].Value;
                var role = GetRole(property);
                var score = role switch
                {
                    Role.Primary => 900,
                    Role.Secondary => 850,
                    _ when prominent => 250,
                    _ => 10
                };
                if (property.Contains("background", StringComparison.OrdinalIgnoreCase))
                {
                    score += 100;
                }

                foreach (Match color in ColorRegex().Matches(value))
                {
                    if (NormalizeColor(color.Value) is { } normalized &&
                        !BrandEvidenceExtractor.IsNeutral(normalized))
                    {
                        candidates.Add(new Candidate(normalized, sourceUrl, role, score));
                    }
                }
            }
        }
    }

    private static Role GetRole(string property)
    {
        if (!property.StartsWith("--", StringComparison.Ordinal))
        {
            return Role.General;
        }

        if (property.Contains("secondary", StringComparison.OrdinalIgnoreCase) ||
            property.Contains("accent", StringComparison.OrdinalIgnoreCase))
        {
            return Role.Secondary;
        }

        return property.Contains("primary", StringComparison.OrdinalIgnoreCase) ||
               property.Contains("brand", StringComparison.OrdinalIgnoreCase)
            ? Role.Primary
            : Role.General;
    }

    private static string? NormalizeColor(string? value)
    {
        if (BrandEvidenceExtractor.NormalizeHex(value) is { } hex)
        {
            return hex;
        }

        if (value is null)
        {
            return null;
        }

        var match = FunctionalColorRegex().Match(value.Trim());
        if (!match.Success)
        {
            return null;
        }

        var values = match.Groups["values"].Value
            .Split([',', ' ', '/'], StringSplitOptions.RemoveEmptyEntries);
        if (values.Length < 3)
        {
            return null;
        }

        return match.Groups["kind"].Value.StartsWith("rgb", StringComparison.OrdinalIgnoreCase)
            ? NormalizeRgb(values)
            : NormalizeHsl(values);
    }

    private static string? NormalizeRgb(string[] values)
    {
        var channels = new int[3];
        for (var index = 0; index < 3; index++)
        {
            var percentage = values[index].EndsWith('%');
            if (!double.TryParse(values[index].TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            {
                return null;
            }
            channels[index] = Math.Clamp((int)Math.Round(percentage ? parsed * 2.55 : parsed), 0, 255);
        }
        return $"#{channels[0]:x2}{channels[1]:x2}{channels[2]:x2}";
    }

    private static string? NormalizeHsl(string[] values)
    {
        if (!double.TryParse(values[0].TrimEnd("deg".ToCharArray()), NumberStyles.Float, CultureInfo.InvariantCulture, out var hue) ||
            !double.TryParse(values[1].TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out var saturation) ||
            !double.TryParse(values[2].TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out var lightness))
        {
            return null;
        }

        hue = ((hue % 360) + 360) % 360 / 360;
        saturation = Math.Clamp(saturation / 100, 0, 1);
        lightness = Math.Clamp(lightness / 100, 0, 1);
        var q = lightness < 0.5 ? lightness * (1 + saturation) : lightness + saturation - lightness * saturation;
        var p = 2 * lightness - q;
        var red = HueToRgb(p, q, hue + 1d / 3d);
        var green = HueToRgb(p, q, hue);
        var blue = HueToRgb(p, q, hue - 1d / 3d);
        return $"#{(int)Math.Round(red * 255):x2}{(int)Math.Round(green * 255):x2}{(int)Math.Round(blue * 255):x2}";
    }

    private static double HueToRgb(double p, double q, double value)
    {
        if (value < 0) value += 1;
        if (value > 1) value -= 1;
        if (value < 1d / 6d) return p + (q - p) * 6 * value;
        if (value < 0.5) return q;
        if (value < 2d / 3d) return p + (q - p) * (2d / 3d - value) * 6;
        return p;
    }

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline, matchTimeoutMilliseconds: 200)]
    private static partial Regex CommentRegex();

    [GeneratedRegex(@"(?<selector>[^{}]+)\{(?<body>[^{}]*)\}", RegexOptions.Singleline, matchTimeoutMilliseconds: 200)]
    private static partial Regex CssRuleRegex();

    [GeneratedRegex(@"(?<property>--?[\w-]+|[a-zA-Z][\w-]*)\s*:\s*(?<value>[^;]+)", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex CssDeclarationRegex();

    [GeneratedRegex(@"#(?:[0-9a-fA-F]{6}|[0-9a-fA-F]{3})\b|(?:rgb|hsl)a?\([^)]*\)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex ColorRegex();

    [GeneratedRegex(@"^(?<kind>rgb|rgba|hsl|hsla)\((?<values>[^)]*)\)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex FunctionalColorRegex();

    [GeneratedRegex(@"(?:^|[.#\s_-])(header|nav|hero|logo|brand|cta|button)(?:$|[.#\s_:-])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex ProminentSelectorRegex();
}
