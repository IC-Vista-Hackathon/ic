using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using Pronto.BillerExperience.Api.Configuration;
using Pronto.BillerExperience.Contracts.V1.Branding;
using Microsoft.Extensions.Options;

namespace Pronto.BillerExperience.Api.Infrastructure.Research;

/// <summary>Reads a biller's public website and infers its brand (colors, font, logo).</summary>
public interface IBrandScanner
{
    Task<BrandScanResponse> ScanAsync(BrandScanRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Fetches a biller's homepage and its linked/inline CSS over the same SSRF-hardened handler the
/// same-site researcher uses (<see cref="ResearchHttpHandler"/> re-validates every connection
/// against <see cref="ResearchAddressGuard"/>, so a public hostname can never be rebound to an
/// internal address). Only public HTTPS origins are read, response sizes are bounded, and the
/// number of stylesheets is capped, so a hostile page cannot amplify the scan.
/// </summary>
public sealed partial class HttpBrandScanner(
    HttpClient httpClient,
    IOptions<BillerExperienceOptions> options,
    ILogger<HttpBrandScanner> logger) : IBrandScanner
{
    private const int MaxStylesheets = 4;
    private const int MaxRedirects = 5;

    private static readonly HashSet<string> GenericFontFamilies = new(StringComparer.OrdinalIgnoreCase)
    {
        "serif", "sans-serif", "monospace", "cursive", "fantasy", "system-ui", "ui-sans-serif",
        "ui-serif", "ui-monospace", "ui-rounded", "-apple-system", "blinkmacsystemfont", "math",
        "emoji", "inherit", "initial", "unset", "revert", "none",
    };

    private readonly ResearchOptions _options = options.Value.Research;

    public async Task<BrandScanResponse> ScanAsync(BrandScanRequest request, CancellationToken cancellationToken = default)
    {
        var website = request.Website;
        if (website is null || !website.IsAbsoluteUri || website.Scheme != Uri.UriSchemeHttps)
        {
            return Failed("brand_scan.https_required");
        }

        try
        {
            var page = await FetchAsync(website, "text/html", cancellationToken);
            if (page.Content is null)
            {
                return Failed(page.ErrorCode ?? "brand_scan.unreachable");
            }

            var html = page.Content;
            var baseUri = page.FinalUri!;
            var warnings = new List<string>();

            var css = new StringBuilder();
            foreach (Match block in StyleBlockRegex().Matches(html))
            {
                css.Append(block.Groups[1].Value).Append('\n');
            }

            foreach (var stylesheet in ExtractStylesheetLinks(html, baseUri).Take(MaxStylesheets))
            {
                var sheet = await FetchAsync(stylesheet, "text/css", cancellationToken);
                if (sheet.Content is not null)
                {
                    css.Append(sheet.Content).Append('\n');
                }
                else
                {
                    warnings.Add("brand_scan.stylesheet_unreadable");
                }
            }

            var cssText = css.ToString();
            var themeColor = NormalizeHex(Extract(html, ThemeColorRegex()));
            var palette = BuildPalette(themeColor, cssText, html);
            var font = ExtractFontFamily(html, cssText);
            var logo = ExtractLogo(html, baseUri);

            if (palette.Count == 0 && font is null && logo is null)
            {
                warnings.Add("brand_scan.no_brand_signals");
            }

            return new BrandScanResponse(
                warnings.Count == 0 ? BrandScanOutcome.Completed : BrandScanOutcome.Degraded,
                palette.ElementAtOrDefault(0),
                palette.ElementAtOrDefault(1),
                palette.ElementAtOrDefault(2),
                font,
                logo,
                palette,
                warnings);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogScanFailure(logger, website.Host, exception);
            return Failed("brand_scan.unexpected_failure");
        }
    }

    private async Task<FetchResult> FetchAsync(Uri uri, string expectedMediaType, CancellationToken cancellationToken)
    {
        var current = uri;
        for (var redirect = 0; redirect <= MaxRedirects; redirect++)
        {
            if (!current.IsAbsoluteUri || current.Scheme != Uri.UriSchemeHttps)
            {
                return FetchResult.Failure("brand_scan.https_required");
            }

            using var message = new HttpRequestMessage(HttpMethod.Get, current);
            message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(expectedMediaType));
            message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*", 0.1));
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _options.RequestTimeoutSeconds)));

            HttpResponseMessage response;
            try
            {
                response = await httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            }
            catch (HttpRequestException)
            {
                return FetchResult.Failure("brand_scan.request_failed");
            }

            using (response)
            {
                if ((int)response.StatusCode is >= 300 and <= 399)
                {
                    if (response.Headers.Location is null || redirect == MaxRedirects)
                    {
                        return FetchResult.Failure("brand_scan.invalid_redirect");
                    }

                    current = response.Headers.Location.IsAbsoluteUri
                        ? response.Headers.Location
                        : new Uri(current, response.Headers.Location);
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    return FetchResult.Failure("brand_scan.http_error");
                }

                var mediaType = response.Content.Headers.ContentType?.MediaType;
                if (mediaType is not null && !mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase))
                {
                    return FetchResult.Failure("brand_scan.unsupported_content_type");
                }

                if (response.Content.Headers.ContentLength > _options.MaxResponseBytes)
                {
                    return FetchResult.Failure("brand_scan.response_too_large");
                }

                var text = await ReadBoundedAsync(response, Math.Max(1, _options.MaxResponseBytes), timeout.Token);
                return text is null
                    ? FetchResult.Failure("brand_scan.response_too_large")
                    : FetchResult.Success(response.RequestMessage?.RequestUri ?? current, text);
            }
        }

        return FetchResult.Failure("brand_scan.invalid_redirect");
    }

    private static async Task<string?> ReadBoundedAsync(HttpResponseMessage response, int maximumBytes, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var memory = new MemoryStream(Math.Min(maximumBytes, 81920));
        var buffer = new byte[81920];
        var total = 0;
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                return Encoding.UTF8.GetString(memory.GetBuffer(), 0, total);
            }

            total += read;
            if (total > maximumBytes)
            {
                return null;
            }

            memory.Write(buffer, 0, read);
        }
    }

    private static IEnumerable<Uri> ExtractStylesheetLinks(string html, Uri baseUri)
    {
        foreach (Match tag in LinkTagRegex().Matches(html))
        {
            var rel = Attribute(tag.Value, "rel");
            if (rel is null || !rel.Contains("stylesheet", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var href = Attribute(tag.Value, "href");
            if (href is not null
                && Uri.TryCreate(baseUri, System.Net.WebUtility.HtmlDecode(href), out var uri)
                && uri.Scheme == Uri.UriSchemeHttps
                && !IsFontProviderHost(uri.Host))
            {
                yield return uri;
            }
        }
    }

    private static bool IsFontProviderHost(string host) =>
        host.Equals("fonts.googleapis.com", StringComparison.OrdinalIgnoreCase)
        || host.Equals("fonts.gstatic.com", StringComparison.OrdinalIgnoreCase);

    private static Uri? ExtractLogo(string html, Uri baseUri)
    {
        var appleTouch = new List<string>();
        var icons = new List<string>();
        var maskIcons = new List<string>();
        foreach (Match tag in LinkTagRegex().Matches(html))
        {
            var rel = Attribute(tag.Value, "rel");
            var href = Attribute(tag.Value, "href");
            if (rel is null || href is null)
            {
                continue;
            }

            if (rel.Contains("apple-touch-icon", StringComparison.OrdinalIgnoreCase))
            {
                appleTouch.Add(href);
            }
            else if (rel.Contains("mask-icon", StringComparison.OrdinalIgnoreCase))
            {
                maskIcons.Add(href);
            }
            else if (rel.Contains("icon", StringComparison.OrdinalIgnoreCase))
            {
                icons.Add(href);
            }
        }

        var ogImage = Extract(html, OgImageRegex());
        var candidates = appleTouch
            .Concat(ogImage is null ? Array.Empty<string>() : [ogImage])
            .Concat(icons)
            .Concat(maskIcons)
            .Append("/favicon.ico");

        foreach (var candidate in candidates)
        {
            if (Uri.TryCreate(baseUri, System.Net.WebUtility.HtmlDecode(candidate), out var uri)
                && uri.Scheme == Uri.UriSchemeHttps)
            {
                return uri;
            }
        }

        return null;
    }

    private static string? ExtractFontFamily(string html, string cssText)
    {
        var googleFont = Extract(html, GoogleFontRegex());
        if (googleFont is not null)
        {
            var family = System.Net.WebUtility.UrlDecode(googleFont).Replace('+', ' ').Split(':')[0].Trim();
            if (family.Length > 0 && !GenericFontFamilies.Contains(family))
            {
                return family;
            }
        }

        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (Match declaration in FontFamilyRegex().Matches(cssText))
        {
            var first = declaration.Groups[1].Value.Split(',')[0].Trim().Trim('\'', '"', '`').Trim();
            if (first.Length is > 0 and <= 64 && !GenericFontFamilies.Contains(first) && !first.StartsWith("var(", StringComparison.OrdinalIgnoreCase))
            {
                counts[first] = counts.GetValueOrDefault(first) + 1;
            }
        }

        return counts.Count == 0 ? null : counts.OrderByDescending(pair => pair.Value).First().Key;
    }

    private static List<string> BuildPalette(string? themeColor, string cssText, string html)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        void Tally(string? hex)
        {
            var normalized = NormalizeHex(hex);
            if (normalized is not null && !IsNeutral(normalized))
            {
                counts[normalized] = counts.GetValueOrDefault(normalized) + 1;
            }
        }

        foreach (Match match in HexColorRegex().Matches(cssText))
        {
            Tally(match.Value);
        }

        foreach (Match match in RgbColorRegex().Matches(cssText))
        {
            Tally(RgbToHex(match));
        }

        foreach (Match match in HexColorRegex().Matches(html))
        {
            Tally(match.Value);
        }

        var ranked = counts
            .OrderByDescending(pair => pair.Value)
            .Select(pair => pair.Key)
            .ToList();

        var palette = new List<string>();
        void Add(string? hex)
        {
            if (hex is not null && !palette.Any(existing => AreSimilar(existing, hex)))
            {
                palette.Add(hex);
            }
        }

        Add(themeColor is not null && !IsNeutral(themeColor) ? themeColor : null);
        foreach (var color in ranked)
        {
            if (palette.Count >= 3)
            {
                break;
            }

            Add(color);
        }

        return palette;
    }

    private static string? NormalizeHex(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var match = HexColorRegex().Match(value);
        if (!match.Success)
        {
            return null;
        }

        var hex = match.Value[1..];
        if (hex.Length == 3)
        {
            hex = string.Concat(hex[0], hex[0], hex[1], hex[1], hex[2], hex[2]);
        }

        return "#" + hex.ToLowerInvariant();
    }

    private static string? RgbToHex(Match match)
    {
        if (!int.TryParse(match.Groups[1].Value, out var r)
            || !int.TryParse(match.Groups[2].Value, out var g)
            || !int.TryParse(match.Groups[3].Value, out var b)
            || r > 255 || g > 255 || b > 255)
        {
            return null;
        }

        return $"#{r:x2}{g:x2}{b:x2}";
    }

    private static bool IsNeutral(string hex)
    {
        var r = Convert.ToInt32(hex.Substring(1, 2), 16);
        var g = Convert.ToInt32(hex.Substring(3, 2), 16);
        var b = Convert.ToInt32(hex.Substring(5, 2), 16);
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        return max - min < 18 || (r > 240 && g > 240 && b > 240) || (r < 16 && g < 16 && b < 16);
    }

    private static bool AreSimilar(string first, string second)
    {
        var a = Convert.ToInt32(first[1..], 16);
        var b = Convert.ToInt32(second[1..], 16);
        return Math.Abs(((a >> 16) & 0xFF) - ((b >> 16) & 0xFF)) < 24
            && Math.Abs(((a >> 8) & 0xFF) - ((b >> 8) & 0xFF)) < 24
            && Math.Abs((a & 0xFF) - (b & 0xFF)) < 24;
    }

    private static string? Extract(string html, Regex regex)
    {
        var match = regex.Match(html);
        return match.Success ? match.Groups["value"].Value.Trim() : null;
    }

    private static string? Attribute(string tag, string name)
    {
        var match = Regex.Match(
            tag,
            $"""{name}\s*=\s*(?:"(?<value>[^"]*)"|'(?<value>[^']*)'|(?<value>[^\s"'>]+))""",
            RegexOptions.IgnoreCase,
            TimeSpan.FromMilliseconds(100));
        return match.Success ? match.Groups["value"].Value : null;
    }

    private static BrandScanResponse Failed(string code) =>
        new(BrandScanOutcome.Failed, null, null, null, null, null, [], [code], code);

    private sealed record FetchResult(Uri? FinalUri, string? Content, string? ErrorCode)
    {
        internal static FetchResult Success(Uri uri, string content) => new(uri, content, null);
        internal static FetchResult Failure(string code) => new(null, null, code);
    }

    [GeneratedRegex(@"<style[^>]*>(.*?)</style>", RegexOptions.IgnoreCase | RegexOptions.Singleline, matchTimeoutMilliseconds: 200)]
    private static partial Regex StyleBlockRegex();

    [GeneratedRegex(@"<link\b[^>]*>", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 200)]
    private static partial Regex LinkTagRegex();

    [GeneratedRegex("""<meta\s+[^>]*name\s*=\s*['"]theme-color['"][^>]*content\s*=\s*['"](?<value>#[0-9a-fA-F]{3,6})['"]""", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 100)]
    private static partial Regex ThemeColorRegex();

    [GeneratedRegex("""<meta\s+[^>]*property\s*=\s*['"]og:image['"][^>]*content\s*=\s*['"](?<value>[^'"]+)['"]""", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 100)]
    private static partial Regex OgImageRegex();

    [GeneratedRegex(@"fonts\.googleapis\.com/css2?\?[^""'>]*family=(?<value>[^""'&>:]+)", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 100)]
    private static partial Regex GoogleFontRegex();

    [GeneratedRegex(@"font-family\s*:\s*([^;{}]+)", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 100)]
    private static partial Regex FontFamilyRegex();

    [GeneratedRegex(@"#(?:[0-9a-fA-F]{6}|[0-9a-fA-F]{3})\b", RegexOptions.None, matchTimeoutMilliseconds: 100)]
    private static partial Regex HexColorRegex();

    [GeneratedRegex(@"rgba?\(\s*(\d{1,3})\s*,\s*(\d{1,3})\s*,\s*(\d{1,3})", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 100)]
    private static partial Regex RgbColorRegex();

    [LoggerMessage(2650, LogLevel.Warning, "Brand scan failed for host {Host}")]
    private static partial void LogScanFailure(ILogger logger, string host, Exception exception);
}
