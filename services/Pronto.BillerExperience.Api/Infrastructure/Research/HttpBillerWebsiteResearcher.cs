using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using Pronto.BillerExperience.Api.Configuration;
using Pronto.BillerExperience.Contracts.V1.Research;
using Microsoft.Extensions.Options;

namespace Pronto.BillerExperience.Api.Infrastructure.Research;

public sealed partial class HttpBillerWebsiteResearcher(
    HttpClient httpClient,
    IDestinationAddressResolver addressResolver,
    IOptions<BillerExperienceOptions> options,
    ILogger<HttpBillerWebsiteResearcher> logger) : IBillerWebsiteResearcher
{
    private const string UnsafeTargetCode = "research.unsafe_target";
    private readonly ResearchOptions _options = options.Value.Research;

    public async Task<BillerResearchResponse> ResearchAsync(
        BillerResearchRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Website is null)
        {
            return new BillerResearchResponse(
                ResearchOutcome.Skipped, [], [], ["research.website_missing"], "research.website_missing");
        }

        var website = request.Website;
        var started = Stopwatch.GetTimestamp();
        ResearchTelemetry.Requests.Add(1);
        using var activity = ResearchTelemetry.Start(website.Host);

        try
        {
            var validation = await ValidateTargetAsync(website, website.Host, cancellationToken);
            if (validation is not null)
            {
                LogRejectedTarget(logger, validation, website.Host, Activity.Current?.TraceId.ToString());
                return Fail(validation, retryable: false, activity);
            }

            var pageLimit = Math.Clamp(request.MaxPages, 1, Math.Max(1, _options.MaxPages));
            var pending = new Queue<Uri>();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var facts = new List<ResearchFact>();
            var sources = new List<ResearchSource>();
            var warnings = new List<string>();
            pending.Enqueue(website);

            while (pending.Count > 0 && visited.Count < pageLimit)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var pageUri = pending.Dequeue();
                if (!visited.Add(Canonical(pageUri)))
                {
                    continue;
                }

                var page = await FetchPageAsync(pageUri, website.Host, cancellationToken);
                if (page.ErrorCode is not null)
                {
                    if (sources.Count == 0)
                    {
                        return Fail(page.ErrorCode, page.Retryable, activity);
                    }

                    warnings.Add(page.ErrorCode);
                    continue;
                }

                var retrievedAt = DateTimeOffset.UtcNow;
                var title = Extract(TitleRegex(), page.Html!, _options.MaxFactLength);
                var description = Extract(MetaDescriptionRegex(), page.Html!, _options.MaxFactLength);
                sources.Add(new ResearchSource(page.FinalUri!, title, retrievedAt));
                AddFact(facts, "page_title", title, page.FinalUri!);
                AddFact(facts, "page_description", description, page.FinalUri!);
                ResearchTelemetry.Pages.Add(1);

                foreach (var link in ExtractLinks(page.Html!, page.FinalUri!, website.Host)
                             .Take(Math.Max(0, _options.MaxLinksPerPage)))
                {
                    if (!visited.Contains(Canonical(link)))
                    {
                        pending.Enqueue(link);
                    }
                }
            }

            activity?.SetTag("research.pages", sources.Count);
            activity?.SetTag("research.facts", facts.Count);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return new BillerResearchResponse(
                warnings.Count == 0 ? ResearchOutcome.Completed : ResearchOutcome.Degraded,
                facts,
                sources,
                warnings);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogResearchFailure(logger, "research.timeout", website.Host, exception);
            return Fail("research.timeout", retryable: true, activity);
        }
        catch (OperationCanceledException exception)
        {
            LogResearchFailure(logger, "research.cancelled", website.Host, exception);
            return Fail("research.cancelled", retryable: false, activity);
        }
        catch (HttpRequestException exception)
        {
            LogResearchFailure(logger, "research.request_failed", website.Host, exception);
            return Fail("research.request_failed", retryable: true, activity);
        }
        catch (SocketException exception)
        {
            LogResearchFailure(logger, "research.dns_failed", website.Host, exception);
            return Fail("research.dns_failed", retryable: true, activity);
        }
        catch (Exception exception)
        {
            LogResearchFailure(logger, "research.unexpected_failure", website.Host, exception);
            return Fail("research.unexpected_failure", retryable: false, activity);
        }
        finally
        {
            ResearchTelemetry.Duration.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
    }

    private async Task<PageResult> FetchPageAsync(Uri uri, string allowedHost, CancellationToken cancellationToken)
    {
        for (var redirectCount = 0; redirectCount <= 5; redirectCount++)
        {
            var targetError = await ValidateTargetAsync(uri, allowedHost, cancellationToken);
            if (targetError is not null)
            {
                LogRejectedTarget(logger, targetError, allowedHost);
                return PageResult.Failure(targetError, false);
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _options.RequestTimeoutSeconds)));
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);

            var responseUri = response.RequestMessage?.RequestUri ?? uri;
            var responseTargetError = await ValidateTargetAsync(responseUri, allowedHost, cancellationToken);
            if (responseTargetError is not null)
            {
                LogRejectedTarget(logger, responseTargetError, allowedHost);
                return PageResult.Failure(responseTargetError, false);
            }

            if (IsRedirect(response.StatusCode))
            {
                if (response.Headers.Location is null || redirectCount == 5)
                {
                    LogPageFailure(logger, "research.invalid_redirect", allowedHost, (int)response.StatusCode);
                    return PageResult.Failure("research.invalid_redirect", false);
                }

                uri = response.Headers.Location.IsAbsoluteUri
                    ? response.Headers.Location
                    : new Uri(uri, response.Headers.Location);
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                LogPageFailure(logger, "research.http_error", allowedHost, (int)response.StatusCode);
                return PageResult.Failure("research.http_error", (int)response.StatusCode >= 500);
            }

            if (response.Content.Headers.ContentType?.MediaType is not { } mediaType ||
                !mediaType.Equals("text/html", StringComparison.OrdinalIgnoreCase))
            {
                LogPageFailure(logger, "research.unsupported_content_type", allowedHost, (int)response.StatusCode);
                return PageResult.Failure("research.unsupported_content_type", false);
            }

            if (response.Content.Headers.ContentLength > _options.MaxResponseBytes)
            {
                LogPageFailure(logger, "research.response_too_large", allowedHost, (int)response.StatusCode);
                return PageResult.Failure("research.response_too_large", false);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            var html = await ReadBoundedAsync(stream, Math.Max(1, _options.MaxResponseBytes), timeout.Token);
            if (html is null)
            {
                LogPageFailure(logger, "research.response_too_large", allowedHost, (int)response.StatusCode);
                return PageResult.Failure("research.response_too_large", false);
            }

            return PageResult.Success(responseUri, html);
        }

        return PageResult.Failure("research.invalid_redirect", false);
    }

    private async Task<string?> ValidateTargetAsync(Uri target, string allowedHost, CancellationToken cancellationToken)
    {
        if (!target.IsAbsoluteUri || target.Scheme != Uri.UriSchemeHttps)
        {
            return "research.https_required";
        }

        if (!target.Host.Equals(allowedHost, StringComparison.OrdinalIgnoreCase))
        {
            return "research.off_domain";
        }

        IReadOnlyList<IPAddress> addresses;
        if (IPAddress.TryParse(target.Host, out var literal))
        {
            addresses = new[] { literal };
        }
        else
        {
            addresses = await addressResolver.ResolveAsync(target.IdnHost, cancellationToken);
        }

        return addresses.Count == 0 || addresses.Any(IsUnsafeAddress) ? UnsafeTargetCode : null;
    }

    private static bool IsUnsafeAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any) ||
            address.Equals(IPAddress.None) || address.Equals(IPAddress.IPv6None) || address.IsIPv6LinkLocal ||
            address.IsIPv6Multicast || address.IsIPv6SiteLocal)
        {
            return true;
        }

        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return (bytes[0] & 0xfe) == 0xfc;
        }

        return bytes[0] is 0 or 10 or 127 ||
               (bytes[0] == 100 && bytes[1] is >= 64 and <= 127) ||
               (bytes[0] == 169 && bytes[1] == 254) ||
               (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) ||
               (bytes[0] == 192 && bytes[1] == 168) ||
               bytes[0] >= 224;
    }

    private static async Task<string?> ReadBoundedAsync(Stream stream, int maximumBytes, CancellationToken cancellationToken)
    {
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

    private static IEnumerable<Uri> ExtractLinks(string html, Uri baseUri, string allowedHost)
    {
        foreach (Match match in LinkRegex().Matches(html))
        {
            var value = WebUtility.HtmlDecode(match.Groups[1].Value);
            if (Uri.TryCreate(baseUri, value, out var uri) && uri.Scheme == Uri.UriSchemeHttps &&
                uri.Host.Equals(allowedHost, StringComparison.OrdinalIgnoreCase))
            {
                yield return new UriBuilder(uri) { Fragment = string.Empty }.Uri;
            }
        }
    }

    private static string? Extract(Regex regex, string html, int maxLength)
    {
        var match = regex.Match(html);
        if (!match.Success)
        {
            return null;
        }

        var captured = match.Groups["value"].Success ? match.Groups["value"].Value : match.Groups[1].Value;
        var text = WhitespaceRegex().Replace(WebUtility.HtmlDecode(captured), " ").Trim();
        return text.Length == 0 ? null : text[..Math.Min(text.Length, Math.Max(1, maxLength))];
    }

    private static void AddFact(List<ResearchFact> facts, string name, string? value, Uri source)
    {
        if (value is not null)
        {
            facts.Add(new ResearchFact(name, value, source, 0.9));
        }
    }

    private static string Canonical(Uri uri) => uri.GetComponents(UriComponents.SchemeAndServer | UriComponents.PathAndQuery, UriFormat.SafeUnescaped);
    private static bool IsRedirect(HttpStatusCode status) => (int)status is >= 300 and <= 399;

    private static BillerResearchResponse Fail(string code, bool retryable, Activity? activity)
    {
        ResearchTelemetry.Failures.Add(1, new KeyValuePair<string, object?>("error.type", code));
        activity?.SetTag("error.type", code);
        activity?.SetStatus(ActivityStatusCode.Error, code);
        return new BillerResearchResponse(ResearchOutcome.Failed, [], [], [code], code, retryable);
    }

    private sealed record PageResult(Uri? FinalUri, string? Html, string? ErrorCode, bool Retryable)
    {
        internal static PageResult Success(Uri uri, string html) => new(uri, html, null, false);
        internal static PageResult Failure(string code, bool retryable) => new(null, null, code, retryable);
    }

    [GeneratedRegex(@"<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline, matchTimeoutMilliseconds: 100)]
    private static partial Regex TitleRegex();

    [GeneratedRegex("""<meta\s+[^>]*(?:name\s*=\s*['"]description['"][^>]*content\s*=\s*['"](?<value>[^'"]*)['"]|content\s*=\s*['"](?<value>[^'"]*)['"][^>]*name\s*=\s*['"]description['"])[^>]*>""", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 100)]
    private static partial Regex MetaDescriptionRegex();

    [GeneratedRegex("""<a\s+[^>]*href\s*=\s*['"]([^'"]+)['"]""", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 100)]
    private static partial Regex LinkRegex();

    [GeneratedRegex(@"\s+", RegexOptions.None, matchTimeoutMilliseconds: 100)]
    private static partial Regex WhitespaceRegex();

    [LoggerMessage(2600, LogLevel.Error, "Website research failed with {ErrorCode} for host {Host}; trace {TraceId}")]
    private static partial void LogResearchFailure(ILogger logger, string errorCode, string host, Exception exception, string? traceId = null);

    [LoggerMessage(2601, LogLevel.Error, "Website research rejected target with {ErrorCode} for host {Host}; trace {TraceId}")]
    private static partial void LogRejectedTarget(ILogger logger, string errorCode, string host, string? traceId = null);

    [LoggerMessage(2602, LogLevel.Error, "Website research page failed with {ErrorCode} for host {Host}, status {StatusCode}; trace {TraceId}")]
    private static partial void LogPageFailure(ILogger logger, string errorCode, string host, int statusCode, string? traceId = null);
}
