import { readFile, readdir } from 'node:fs/promises';
import { join, relative, sep } from 'node:path';
import { BlobServiceClient } from '@azure/storage-blob';
import { DefaultAzureCredential } from '@azure/identity';

const CONTENT_TYPES: Record<string, string> = {
  html: 'text/html; charset=utf-8',
  js: 'text/javascript; charset=utf-8',
  mjs: 'text/javascript; charset=utf-8',
  css: 'text/css; charset=utf-8',
  json: 'application/json; charset=utf-8',
  webmanifest: 'application/manifest+json; charset=utf-8',
  svg: 'image/svg+xml',
  png: 'image/png',
  jpg: 'image/jpeg',
  ico: 'image/x-icon',
  map: 'application/json; charset=utf-8',
  woff2: 'font/woff2',
};

export interface PublishInputs {
  distDir: string;
  slug: string;
  revision: string;
  storageEndpoint: string;
  containerName?: string;
  // When false, the caller (e.g. the Worker's config publisher) owns the atomic
  // active.json flip and this step only uploads the immutable site tree.
  writeActive?: boolean;
}

export interface PublishResult {
  uploaded: number;
  activeBlob: string | null;
  sitePrefix: string;
}

// Publish the full built bundle (not just config) to Blob Storage under a revision
// prefix, then flip the active pointer last — the same atomic-last-write pattern the
// Worker already uses for config.json/active.json, extended to a whole dist tree.
export async function publishBundle({
  distDir,
  slug,
  revision,
  storageEndpoint,
  containerName = 'payer-experiences',
  writeActive = true,
}: PublishInputs): Promise<PublishResult> {
  const service = new BlobServiceClient(storageEndpoint, new DefaultAzureCredential());
  const container = service.getContainerClient(containerName);

  const sitePrefix = `billers/${slug}/revisions/${revision}/site`;
  const files = await walk(distDir);
  for (const file of files) {
    const rel = relative(distDir, file).split(sep).join('/');
    const blobName = `${sitePrefix}/${rel}`;
    const body = await readFile(file);
    const blob = container.getBlockBlobClient(blobName);
    try {
      await blob.uploadData(body, {
        blobHTTPHeaders: {
          blobContentType: contentTypeFor(rel),
          blobCacheControl: cacheControlFor(rel),
        },
        conditions: { ifNoneMatch: '*' },
      });
    } catch (error) {
      const statusCode = getStatusCode(error);
      if (statusCode !== 409 && statusCode !== 412) throw error;
      const existing = await blob.downloadToBuffer();
      if (!existing.equals(body)) {
        throw new Error(`Immutable site artifact already exists with different content: ${blobName}`);
      }
    }
  }

  if (!writeActive) {
    // Site tree is uploaded but not yet activated; the caller flips active.json last.
    return { uploaded: files.length, activeBlob: null, sitePrefix };
  }

  // Active pointer written last so a partially-uploaded revision is never served.
  const activeBlob = `billers/${slug}/active.json`;
  const pointer = JSON.stringify({ slug, revision, site_prefix: sitePrefix, entry: `${sitePrefix}/index.html` });
  await container.getBlockBlobClient(activeBlob).uploadData(Buffer.from(pointer), {
    blobHTTPHeaders: { blobContentType: 'application/json', blobCacheControl: 'no-cache, no-store, must-revalidate' },
  });

  return { uploaded: files.length, activeBlob, sitePrefix };
}

async function walk(dir: string): Promise<string[]> {
  const entries = await readdir(dir, { withFileTypes: true });
  const files: string[] = [];
  for (const entry of entries) {
    const full = join(dir, entry.name);
    if (entry.isDirectory()) files.push(...(await walk(full)));
    else files.push(full);
  }
  return files;
}

function contentTypeFor(rel: string): string {
  const ext = rel.slice(rel.lastIndexOf('.') + 1).toLowerCase();
  return CONTENT_TYPES[ext] ?? 'application/octet-stream';
}

function cacheControlFor(rel: string): string {
  const file = rel.split('/').pop() ?? rel;
  const stem = file.includes('.') ? file.slice(0, file.lastIndexOf('.')) : file;
  return rel.startsWith('assets/') && stem.includes('-')
    ? 'public, max-age=31536000, immutable'
    : 'no-cache, no-store, must-revalidate';
}

function getStatusCode(error: unknown): number | undefined {
  if (typeof error !== 'object' || error === null || !('statusCode' in error)) return undefined;
  const statusCode = error.statusCode;
  return typeof statusCode === 'number' ? statusCode : undefined;
}
