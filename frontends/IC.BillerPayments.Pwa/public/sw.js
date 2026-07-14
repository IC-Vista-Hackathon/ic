const CACHE = 'biller-payments-v1';
self.addEventListener('install', event => event.waitUntil(caches.open(CACHE).then(cache => cache.addAll(['/', '/config.json']))));
self.addEventListener('activate', event => event.waitUntil(self.clients.claim()));
self.addEventListener('fetch', event => {
  if (event.request.method !== 'GET') return;
  event.respondWith(fetch(event.request).then(response => {
    const copy = response.clone(); caches.open(CACHE).then(cache => cache.put(event.request, copy)); return response;
  }).catch(() => caches.match(event.request)));
});
