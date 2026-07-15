const CACHE = 'biller-payments-v3';
// Financial/PII endpoints are never cached: invoices, payments, payers all carry
// account data that must not persist offline on shared devices.
const NETWORK_ONLY = ['/invoices', '/payments', '/payers'];
self.addEventListener('install', event => event.waitUntil(Promise.all([
  caches.open(CACHE).then(cache => cache.addAll([self.registration.scope, new URL('config.json', self.registration.scope).href])),
  self.skipWaiting(),
])));
self.addEventListener('activate', event => event.waitUntil(
  caches.keys().then(keys => Promise.all(keys.filter(key => key !== CACHE).map(key => caches.delete(key)))).then(() => self.clients.claim())));
self.addEventListener('fetch', event => {
  if (event.request.method !== 'GET') return;
  const url = new URL(event.request.url);
  if (NETWORK_ONLY.some(prefix => url.pathname.startsWith(prefix))) return; // network-only, no offline fallback
  event.respondWith(fetch(event.request).then(response => {
    if (response.ok) {
      const copy = response.clone();
      caches.open(CACHE).then(cache => cache.put(event.request, copy));
    }
    return response;
  }).catch(() => caches.match(event.request)));
});
