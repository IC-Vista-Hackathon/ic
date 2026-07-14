using System.Diagnostics;
using System.Text.RegularExpressions;
using IC.BillerExperience.Api.Infrastructure.Publication;
using IC.BillerExperience.Contracts.V1.Experiences;
using Microsoft.AspNetCore.Mvc;

namespace IC.BillerExperience.Api.Controllers;

[ApiController]
[Route("public/experiences")]
public sealed partial class PublishedExperiencesController(
    IPublishedExperienceStore store,
    ILogger<PublishedExperiencesController> logger) : ControllerBase
{
    [HttpGet("{slug}")]
    [ProducesResponseType<BillerExperienceDefinition>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BillerExperienceDefinition>> Get(string slug, CancellationToken cancellationToken)
    {
        ValidateSlug(slug);
        var artifact = await store.GetActiveAsync(slug, cancellationToken);
        if (artifact is null)
        {
            LogPublishedExperienceMissing(logger, slug, Activity.Current?.TraceId.ToString());
            return NotFound();
        }
        Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
        LogPublishedExperienceServed(logger, slug, artifact.Revision, Activity.Current?.TraceId.ToString());
        return Ok(artifact.Definition);
    }

    [HttpGet("{slug}/manifest.webmanifest")]
    [Produces("application/manifest+json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetManifest(string slug, CancellationToken cancellationToken)
    {
        ValidateSlug(slug);
        var artifact = await store.GetActiveAsync(slug, cancellationToken);
        if (artifact is null)
        {
            LogPublishedExperienceMissing(logger, slug, Activity.Current?.TraceId.ToString());
            return NotFound();
        }
        var manifest = await store.GetManifestAsync(slug, artifact.Revision, cancellationToken);
        if (manifest is null)
        {
            LogManifestMissing(logger, slug, artifact.Revision, Activity.Current?.TraceId.ToString());
            return NotFound();
        }
        Response.Headers.CacheControl = "no-cache";
        return File(manifest.ToArray(), "application/manifest+json");
    }

    private void ValidateSlug(string slug)
    {
        if (!SlugRegex().IsMatch(slug))
        {
            LogInvalidSlug(logger, slug, Activity.Current?.TraceId.ToString());
            throw new ArgumentException("Slug must contain 3 to 63 lowercase letters, numbers, or hyphens.");
        }
    }

    [GeneratedRegex("^[a-z0-9](?:[a-z0-9-]{1,61}[a-z0-9])$")]
    private static partial Regex SlugRegex();

    [LoggerMessage(1300, LogLevel.Information, "Served published revision {Revision} for slug {Slug}; trace {TraceId}")]
    private static partial void LogPublishedExperienceServed(ILogger logger, string slug, string revision, string? traceId);

    [LoggerMessage(1398, LogLevel.Error, "No active published experience exists for slug {Slug}; trace {TraceId}")]
    private static partial void LogPublishedExperienceMissing(ILogger logger, string slug, string? traceId);

    [LoggerMessage(1399, LogLevel.Error, "Manifest for slug {Slug}, revision {Revision} is missing; trace {TraceId}")]
    private static partial void LogManifestMissing(ILogger logger, string slug, string revision, string? traceId);

    [LoggerMessage(1397, LogLevel.Error, "Rejected invalid published-experience slug {Slug}; trace {TraceId}")]
    private static partial void LogInvalidSlug(ILogger logger, string slug, string? traceId);
}
