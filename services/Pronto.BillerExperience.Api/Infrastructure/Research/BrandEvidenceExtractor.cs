using System.Net;
using System.Text.RegularExpressions;
using Pronto.BillerExperience.Contracts.V1.Research;

namespace Pronto.BillerExperience.Api.Infrastructure.Research;

/// <summary>
/// Canonical <see cref="ResearchFact.Name"/> values for first-party brand evidence pulled from a
/// biller site. Shared by the crawler that produces them and the applicator that maps them onto a
/// draft, so the two never drift.
/// </summary>
public static class BrandEvidenceFacts
{
    public const string DisplayName = "brand_display_name";
    public const string PrimaryColor = "brand_primary_color";
    public const string SecondaryColor = "brand_secondary_color";
    public const string LogoUrl = "brand_logo_url";
    public const string FontFamily = "brand_font_family";
    public const string Tagline = "brand_tagline";
}

/// <summary>
/// Extracts first-party brand evidence — logo/wordmark URL, dominant colors, typography, display
/// name, and tagline — from a single fetched HTML page. Pure and deterministic: it parses only the
/// markup handed to it (no network), so it is exercised directly with local HTML fixtures. All
/// resolved URLs are constrained to the same origin as the page they came from.
/// </summary>
public static partial class BrandEvidenceExtractor
{
    private const int MaxValueLength = 500;

    public static IReadOnlyList<ResearchFact> Extract(string html, Uri pageUri)
    {
        if (string.IsNullOrEmpty(html))
        {
            return [];
        }

        var metas = ParseMeta(html);
        var links = ParseLinks(html);
        var facts = new List<ResearchFact>();

        AddDisplayName(facts, metas, html, pageUri);
        AddTagline(facts, metas, html, pageUri);
        AddLogo(facts, metas, links, html, pageUri);
        AddColors(facts, metas, html, pageUri);
        AddFont(facts, html, pageUri);

        return facts;
    }

    private static void AddDisplayName(List<ResearchFact> facts, MetaBag metas, string html, Uri pageUri)
    {
        var name = metas.Property("og:site_name") ?? metas.Property("og:title") ?? ExtractTitle(html);
        Add(facts, BrandEvidenceFacts.DisplayName, name, pageUri, 0.75);
    }

    private static void AddTagline(List<ResearchFact> facts, MetaBag metas, string html, Uri pageUri)
    {
        var tagline = metas.Property("og:description")
            ?? metas.Name("description")
            ?? ExtractFirst(HeadingRegex(), html);
        Add(facts, BrandEvidenceFacts.Tagline, tagline, pageUri, 0.7);
    }

    private static void AddLogo(List<ResearchFact> facts, MetaBag metas, List<LinkTag> links, string html, Uri pageUri)
    {
        // Preference order: apple-touch-icon and rel=icon are explicit brand marks; og:image is the
        // social card art; a logo-classed <img> is the on-page wordmark.
        var candidate =
            links.FirstOrDefault(link => link.Rel.Contains("apple-touch-icon", StringComparison.OrdinalIgnoreCase))?.Href
            ?? links.FirstOrDefault(link => Regex.IsMatch(link.Rel, @"\bicon\b", RegexOptions.IgnoreCase))?.Href
            ?? metas.Property("og:image")
            ?? ExtractLogoImage(html);

        var resolved = ResolveSameOrigin(candidate, pageUri);
        Add(facts, BrandEvidenceFacts.LogoUrl, resolved?.AbsoluteUri, pageUri, 0.85);
    }

    private static void AddColors(List<ResearchFact> facts, MetaBag metas, string html, Uri pageUri)
    {
        var ordered = new List<string>();

        // theme-color is the site's declared brand color, so it leads.
        if (NormalizeHex(metas.Name("theme-color")) is { } themed)
        {
            ordered.Add(themed);
        }

        // Then the most prominent non-neutral colors declared anywhere in the markup/CSS, by frequency.
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in HexColorRegex().Matches(html))
        {
            if (NormalizeHex(match.Value) is not { } hex || IsNeutral(hex))
            {
                continue;
            }

            counts[hex] = counts.TryGetValue(hex, out var existing) ? existing + 1 : 1;
        }

        foreach (var hex in counts.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key, StringComparer.Ordinal)
                     .Select(pair => pair.Key))
        {
            if (!ordered.Contains(hex, StringComparer.OrdinalIgnoreCase))
            {
                ordered.Add(hex);
            }
        }

        if (ordered.Count > 0)
        {
            Add(facts, BrandEvidenceFacts.PrimaryColor, ordered[0], pageUri, 0.8);
        }
        if (ordered.Count > 1)
        {
            Add(facts, BrandEvidenceFacts.SecondaryColor, ordered[1], pageUri, 0.7);
        }
    }

    private static void AddFont(List<ResearchFact> facts, string html, Uri pageUri)
    {
        foreach (Match match in FontFamilyRegex().Matches(html))
        {
            foreach (var raw in match.Groups["families"].Value.Split(','))
            {
                var family = raw.Trim().Trim('"', '\'', '`').Trim();
                if (family.Length == 0 || IsGenericFontFamily(family))
                {
                    continue;
                }

                Add(facts, BrandEvidenceFacts.FontFamily, family, pageUri, 0.65);
                return;
            }
        }
    }

    // A hex is "neutral" when its channels are close together (white/black/greys). Neutrals dominate
    // most stylesheets (borders, text, backgrounds), so excluding them keeps the brand hue on top.
    private static bool IsNeutral(string hex)
    {
        var red = Convert.ToInt32(hex.Substring(1, 2), 16);
        var green = Convert.ToInt32(hex.Substring(3, 2), 16);
        var blue = Convert.ToInt32(hex.Substring(5, 2), 16);
        var max = Math.Max(red, Math.Max(green, blue));
        var min = Math.Min(red, Math.Min(green, blue));
        return max - min <= 16;
    }

    private static bool IsGenericFontFamily(string family) => family.ToLowerInvariant() switch
    {
        "sans-serif" or "serif" or "monospace" or "cursive" or "fantasy" or "system-ui"
            or "inherit" or "initial" or "unset" or "-apple-system" or "blinkmacsystemfont"
            or "ui-sans-serif" or "ui-serif" or "ui-monospace" => true,
        _ => false,
    };

    // Expands #rgb to #rrggbb and lower-cases; returns null for anything that isn't a 3/6-digit hex.
    private static string? NormalizeHex(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var text = value.Trim();
        if (!text.StartsWith('#'))
        {
            return null;
        }

        var digits = text[1..];
        if (digits.Length == 3 && digits.All(Uri.IsHexDigit))
        {
            return $"#{digits[0]}{digits[0]}{digits[1]}{digits[1]}{digits[2]}{digits[2]}".ToLowerInvariant();
        }

        return digits.Length == 6 && digits.All(Uri.IsHexDigit) ? $"#{digits}".ToLowerInvariant() : null;
    }

    private static Uri? ResolveSameOrigin(string? value, Uri pageUri)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var decoded = WebUtility.HtmlDecode(value.Trim());
        if (decoded.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ||
            !Uri.TryCreate(pageUri, decoded, out var absolute))
        {
            return null;
        }

        if (absolute.Scheme != Uri.UriSchemeHttps ||
            !absolute.Host.Equals(pageUri.Host, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return absolute;
    }

    private static MetaBag ParseMeta(string html)
    {
        var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var byProperty = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match tag in MetaTagRegex().Matches(html))
        {
            var attributes = ReadAttributes(tag.Value);
            if (!attributes.TryGetValue("content", out var content) || string.IsNullOrWhiteSpace(content))
            {
                continue;
            }

            if (attributes.TryGetValue("name", out var name) && !byName.ContainsKey(name))
            {
                byName[name] = content;
            }
            if (attributes.TryGetValue("property", out var property) && !byProperty.ContainsKey(property))
            {
                byProperty[property] = content;
            }
        }

        return new MetaBag(byName, byProperty);
    }

    private static List<LinkTag> ParseLinks(string html)
    {
        var links = new List<LinkTag>();
        foreach (Match tag in LinkTagRegex().Matches(html))
        {
            var attributes = ReadAttributes(tag.Value);
            if (attributes.TryGetValue("rel", out var rel) && attributes.TryGetValue("href", out var href))
            {
                links.Add(new LinkTag(rel, href));
            }
        }

        return links;
    }

    private static string? ExtractLogoImage(string html)
    {
        foreach (Match tag in ImgTagRegex().Matches(html))
        {
            var attributes = ReadAttributes(tag.Value);
            var descriptor = string.Join(
                ' ',
                new[] { Value(attributes, "class"), Value(attributes, "id"), Value(attributes, "alt") });
            if (descriptor.Contains("logo", StringComparison.OrdinalIgnoreCase) &&
                attributes.TryGetValue("src", out var src))
            {
                return src;
            }
        }

        return null;

        static string Value(IReadOnlyDictionary<string, string> attributes, string key) =>
            attributes.TryGetValue(key, out var value) ? value : string.Empty;
    }

    private static Dictionary<string, string> ReadAttributes(string tag)
    {
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match attribute in AttributeRegex().Matches(tag))
        {
            var name = attribute.Groups["name"].Value;
            if (!attributes.ContainsKey(name))
            {
                attributes[name] = WebUtility.HtmlDecode(attribute.Groups["value"].Value);
            }
        }

        return attributes;
    }

    private static string? ExtractTitle(string html) => ExtractFirst(TitleRegex(), html);

    private static string? ExtractFirst(Regex regex, string html)
    {
        var match = regex.Match(html);
        return match.Success ? Clean(match.Groups["value"].Value) : null;
    }

    private static string? Clean(string value)
    {
        var text = WhitespaceRegex().Replace(WebUtility.HtmlDecode(value), " ").Trim();
        return text.Length == 0 ? null : text;
    }

    private static void Add(List<ResearchFact> facts, string name, string? value, Uri source, double confidence)
    {
        var cleaned = value is null ? null : Clean(value);
        if (string.IsNullOrEmpty(cleaned))
        {
            return;
        }

        facts.Add(new ResearchFact(name, cleaned[..Math.Min(cleaned.Length, MaxValueLength)], source, confidence));
    }

    private sealed record LinkTag(string Rel, string Href);

    private sealed class MetaBag(IReadOnlyDictionary<string, string> byName, IReadOnlyDictionary<string, string> byProperty)
    {
        public string? Name(string key) => byName.TryGetValue(key, out var value) ? value : null;
        public string? Property(string key) => byProperty.TryGetValue(key, out var value) ? value : null;
    }

    [GeneratedRegex(@"<title[^>]*>(?<value>.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline, matchTimeoutMilliseconds: 200)]
    private static partial Regex TitleRegex();

    [GeneratedRegex(@"<h1[^>]*>(?<value>.*?)</h1>", RegexOptions.IgnoreCase | RegexOptions.Singleline, matchTimeoutMilliseconds: 200)]
    private static partial Regex HeadingRegex();

    [GeneratedRegex(@"<meta\b[^>]*>", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 200)]
    private static partial Regex MetaTagRegex();

    [GeneratedRegex(@"<link\b[^>]*>", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 200)]
    private static partial Regex LinkTagRegex();

    [GeneratedRegex(@"<img\b[^>]*>", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 200)]
    private static partial Regex ImgTagRegex();

    [GeneratedRegex("""(?<name>[a-zA-Z_:][\w:.-]*)\s*=\s*("(?<value>[^"]*)"|'(?<value>[^']*)')""", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 200)]
    private static partial Regex AttributeRegex();

    [GeneratedRegex(@"#(?:[0-9a-fA-F]{6}|[0-9a-fA-F]{3})\b", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex HexColorRegex();

    [GeneratedRegex(@"font-family\s*:\s*(?<families>[^;}<]+)", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 200)]
    private static partial Regex FontFamilyRegex();

    [GeneratedRegex(@"\s+", RegexOptions.None, matchTimeoutMilliseconds: 200)]
    private static partial Regex WhitespaceRegex();
}
