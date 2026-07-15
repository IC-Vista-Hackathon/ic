import { createReadStream, existsSync, statSync } from 'node:fs';
import { createServer, type Server } from 'node:http';
import { join, normalize, sep } from 'node:path';
import { validateSlug } from './paths';

const CONTENT_TYPES: Record<string, string> = {
  '.html': 'text/html; charset=utf-8',
  '.js': 'text/javascript; charset=utf-8',
  '.mjs': 'text/javascript; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
  '.json': 'application/json; charset=utf-8',
  '.webmanifest': 'application/manifest+json; charset=utf-8',
  '.svg': 'image/svg+xml',
  '.png': 'image/png',
  '.jpg': 'image/jpeg',
  '.ico': 'image/x-icon',
  '.map': 'application/json; charset=utf-8',
  '.woff2': 'font/woff2',
};

export interface StaticSite {
  server: Server;
  url: string;
  close: () => Promise<void>;
}

// Serves a built dist under /pay/{slug}/ with SPA fallback and correct content types —
// the same contract the production Blob-backed router must honor, so what the Playwright
// gate exercises matches what payers get.
export async function serveBundle(distDir: string, slug: string, port = 0): Promise<StaticSite> {
  const prefix = `/pay/${validateSlug(slug)}/`;
  const root = normalize(distDir);
  const rootWithSep = root.endsWith(sep) ? root : root + sep;
  const server = createServer((request, response) => {
    const requestUrl = new URL(request.url ?? '/', 'http://localhost');
    let pathname: string;
    try {
      pathname = decodeURIComponent(requestUrl.pathname);
    } catch {
      response.writeHead(400).end();
      return;
    }
    if (!pathname.startsWith(prefix)) {
      response.writeHead(404).end();
      return;
    }
    const relative = pathname.slice(prefix.length) || 'index.html';
    let filePath = normalize(join(root, relative));
    if (filePath !== root && !filePath.startsWith(rootWithSep)) {
      response.writeHead(403).end();
      return;
    }
    if (!existsSync(filePath) || statSync(filePath).isDirectory()) {
      filePath = join(root, 'index.html'); // SPA fallback
    }
    const ext = filePath.slice(filePath.lastIndexOf('.'));
    response.writeHead(200, { 'content-type': CONTENT_TYPES[ext] ?? 'application/octet-stream' });
    createReadStream(filePath).pipe(response);
  });

  await new Promise<void>(resolve => server.listen(port, resolve));
  const address = server.address();
  const boundPort = typeof address === 'object' && address ? address.port : port;
  return {
    server,
    url: `http://localhost:${boundPort}${prefix}`,
    close: () => new Promise<void>(resolve => server.close(() => resolve())),
  };
}
