using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Azure;
using Azure.Core;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace Pronto.BillerExperience.Worker.Tests;

// A tiny in-memory stand-in for Azure Blob Storage shared by the publisher (writes) and the
// router (reads) so the end-to-end publish->serve contract can be exercised without Azurite.
// Only the surface the Worker publisher and Payer router actually call is implemented; each
// override honours just the conditions/paths those callers rely on.
public sealed class InMemoryBlobStore
{
    private readonly ConcurrentDictionary<string, StoredBlob> _blobs = new(StringComparer.Ordinal);
    private long _etag;

    public IReadOnlyDictionary<string, StoredBlob> Blobs => _blobs;

    public void Put(string name, byte[] content, string contentType) =>
        _blobs[name] = new StoredBlob(content, contentType, NextETag());

    public bool TryGet(string name, out StoredBlob blob) => _blobs.TryGetValue(name, out blob!);

    public StoredBlob Upload(string name, byte[] content, string contentType, BlobRequestConditions? conditions)
    {
        var exists = _blobs.TryGetValue(name, out var current);
        if (conditions is not null)
        {
            if (conditions.IfNoneMatch == ETag.All && exists)
            {
                throw Conflict(name);
            }
            if (conditions.IfMatch is { } ifMatch && (!exists || current!.ETag != ifMatch))
            {
                throw Conflict(name);
            }
        }
        var stored = new StoredBlob(content, contentType, NextETag());
        _blobs[name] = stored;
        return stored;
    }

    private ETag NextETag() => new($"\"etag-{Interlocked.Increment(ref _etag)}\"");

    private static RequestFailedException Conflict(string name) =>
        new(409, $"Blob '{name}' already exists.", BlobErrorCode.BlobAlreadyExists.ToString(), null);

    public static RequestFailedException NotFound(string name) =>
        new(404, $"Blob '{name}' not found.", BlobErrorCode.BlobNotFound.ToString(), null);

    public sealed record StoredBlob(byte[] Content, string ContentType, ETag ETag);
}

public sealed class InMemoryBlobContainerClient(InMemoryBlobStore store) : BlobContainerClient
{
    public override BlobClient GetBlobClient(string blobName) => new InMemoryBlobClient(store, blobName);
}

public sealed class InMemoryBlobClient(InMemoryBlobStore store, string blobName) : BlobClient
{
    public override string Name => blobName;

    public override Task<Response<BlobContentInfo>> UploadAsync(
        BinaryData content,
        BlobUploadOptions options,
        CancellationToken cancellationToken = default)
    {
        var stored = store.Upload(
            blobName,
            content.ToArray(),
            options?.HttpHeaders?.ContentType ?? "application/octet-stream",
            options?.Conditions);
        var info = BlobsModelFactory.BlobContentInfo(stored.ETag, DateTimeOffset.UtcNow, null, null, null, null, 0);
        return Task.FromResult(Response.FromValue(info, new FakeResponse(201)));
    }

    public override Task<Response<BlobDownloadResult>> DownloadContentAsync(CancellationToken cancellationToken)
    {
        if (!store.TryGet(blobName, out var blob))
        {
            throw InMemoryBlobStore.NotFound(blobName);
        }
        var details = BlobsModelFactory.BlobDownloadDetails(eTag: blob.ETag, contentType: blob.ContentType);
        var result = BlobsModelFactory.BlobDownloadResult(BinaryData.FromBytes(blob.Content), details);
        return Task.FromResult(Response.FromValue(result, new FakeResponse(200)));
    }

    public override Task<Response<BlobDownloadStreamingResult>> DownloadStreamingAsync(
        BlobDownloadOptions options = default!,
        CancellationToken cancellationToken = default)
    {
        if (!store.TryGet(blobName, out var blob))
        {
            throw InMemoryBlobStore.NotFound(blobName);
        }
        var details = BlobsModelFactory.BlobDownloadDetails(eTag: blob.ETag, contentType: blob.ContentType);
        var result = BlobsModelFactory.BlobDownloadStreamingResult(new MemoryStream(blob.Content), details);
        return Task.FromResult(Response.FromValue(result, new FakeResponse(200)));
    }
}

// Minimal Azure.Response so Response.FromValue can wrap fabricated model values.
public sealed class FakeResponse(int status) : Response
{
    public override int Status { get; } = status;
    public override string ReasonPhrase => string.Empty;
    public override Stream? ContentStream { get; set; }
    public override string ClientRequestId { get; set; } = string.Empty;

    public override void Dispose() { }
    protected override bool ContainsHeader(string name) => false;
    protected override IEnumerable<HttpHeader> EnumerateHeaders() => [];
    protected override bool TryGetHeader(string name, [NotNullWhen(true)] out string? value)
    {
        value = null;
        return false;
    }
    protected override bool TryGetHeaderValues(string name, [NotNullWhen(true)] out IEnumerable<string>? values)
    {
        values = null;
        return false;
    }
}
