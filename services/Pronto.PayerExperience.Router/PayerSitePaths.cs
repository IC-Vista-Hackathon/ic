namespace Pronto.PayerExperience.Router;

// Pure request-mapping helpers, kept free of Blob/HTTP dependencies so the routing
// contract (slug extraction, asset paths, SPA fallback, content types) is unit-testable.
public static class PayerSitePaths
{
    // "/pay/acme/assets/index-abc.js" -> ("acme", "assets/index-abc.js").
    // "/pay/acme" or "/pay/acme/" -> ("acme", "index.html").
    public static (string Slug, string RelativePath)? Parse(string requestPath)
    {
        if (string.IsNullOrEmpty(requestPath)) return null;
        var trimmed = requestPath.TrimStart('/');
        if (!trimmed.StartsWith("pay/", StringComparison.Ordinal)) return null;

        var rest = trimmed["pay/".Length..];
        var slash = rest.IndexOf('/');
        var slug = slash < 0 ? rest : rest[..slash];
        if (slug.Length == 0) return null;

        var relative = slash < 0 ? string.Empty : rest[(slash + 1)..];
        relative = relative.Length == 0 ? "index.html" : relative.TrimEnd('/');
        return (slug, relative);
    }

    // A route with no file extension is a client-side route -> serve the SPA shell.
    public static bool IsSpaRoute(string relativePath) =>
        !Path.GetFileName(relativePath).Contains('.');

    public static string BlobName(string sitePrefix, string relativePath) =>
        $"{sitePrefix.TrimEnd('/')}/{relativePath}";

    public static string ContentType(string relativePath) =>
        Path.GetExtension(relativePath).ToLowerInvariant() switch
        {
            ".html" => "text/html; charset=utf-8",
            ".js" or ".mjs" => "text/javascript; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            ".json" => "application/json; charset=utf-8",
            ".webmanifest" => "application/manifest+json; charset=utf-8",
            ".svg" => "image/svg+xml",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".ico" => "image/x-icon",
            ".map" => "application/json; charset=utf-8",
            ".woff2" => "font/woff2",
            ".woff" => "font/woff",
            _ => "application/octet-stream",
        };

    // Only hashed build assets are content-addressed and immutable; stable PWA/runtime files
    // like sw.js, manifest.webmanifest, and config.json must revalidate across revision cutovers.
    public static string CacheControl(string relativePath) =>
        IsImmutableAsset(relativePath)
            ? "public, max-age=31536000, immutable"
            : "no-cache, no-store, must-revalidate";

    private static bool IsImmutableAsset(string relativePath) =>
        relativePath.StartsWith("assets/", StringComparison.OrdinalIgnoreCase)
        && Path.GetFileNameWithoutExtension(relativePath).Contains('-', StringComparison.Ordinal);
}
