self.addEventListener('install', event => {
  event.waitUntil(
    caches.open('yt-dlp-shell-v1').then(cache =>
      cache.addAll([
        '/',
        '/index.html',
        '/_framework/blazor.webassembly.js',
        '/css/app.css'
      ])
    )
  );
  self.skipWaiting();
});

self.addEventListener('activate', event => {
  event.waitUntil(clients.claim());
});

self.addEventListener('fetch', event => {
  event.respondWith(
    caches.match(event.request).then(resp => resp || fetch(event.request))
  );
});
