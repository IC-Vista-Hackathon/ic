using System.Text.Json;
using Azure;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Pronto.PayerExperience.Router;

public sealed partial class PayerSiteRouter(
    BlobContainerClient container,
    IMemoryCache cache,
    IOptions<RouterOptions> options,
    ILogger<PayerSiteRouter> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task HandleAsync(HttpContext context)
    {
        if (PayerSitePaths.Parse(context.Request.Path.Value ?? string.Empty) is not { } parsed)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        var (slug, relativePath) = parsed;

        var active = await ResolveActiveAsync(slug, context.RequestAborted);
        if (active is null)
        {
            LogNoActiveRevision(logger, slug);
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var sitePrefix = active.ResolveSitePrefix();
        if (!await TryServeAsync(context, sitePrefix, relativePath))
        {
            // Missing asset: fall back to the SPA shell for client-side routes, else 404.
            if (!PayerSitePaths.IsSpaRoute(relativePath)
                || !await TryServeAsync(context, sitePrefix, "index.html"))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
            }
        }
    }

    private async Task<bool> TryServeAsync(HttpContext context, string sitePrefix, string relativePath)
    {
        var blob = container.GetBlobClient(PayerSitePaths.BlobName(sitePrefix, relativePath));
        try
        {
            var download = await blob.DownloadStreamingAsync(cancellationToken: context.RequestAborted);
            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = PayerSitePaths.ContentType(relativePath);
            context.Response.Headers.CacheControl = PayerSitePaths.CacheControl(relativePath);
            await download.Value.Content.CopyToAsync(context.Response.Body, context.RequestAborted);
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status == StatusCodes.Status404NotFound)
        {
            return false;
        }
    }

    private async Task<ActiveRevision?> ResolveActiveAsync(string slug, CancellationToken cancellationToken)
    {
        if (cache.TryGetValue<ActiveRevision>(slug, out var cached))
        {
            return cached;
        }

        var pointer = container.GetBlobClient($"billers/{slug}/active.json");
        try
        {
            var download = await pointer.DownloadContentAsync(cancellationToken);
            var active = JsonSerializer.Deserialize<ActiveRevision>(download.Value.Content.ToString(), JsonOptions);
            if (active is not null)
            {
                cache.Set(slug, active, TimeSpan.FromSeconds(options.Value.ActivePointerCacheSeconds));
            }
            return active;
        }
        catch (RequestFailedException ex) when (ex.Status == StatusCodes.Status404NotFound)
        {
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "No active revision found for biller slug {Slug}.")]
    private static partial void LogNoActiveRevision(ILogger logger, string slug);
}
