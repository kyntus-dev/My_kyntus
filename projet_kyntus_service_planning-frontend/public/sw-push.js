/* Kyntus desktop Web Push — no confidential payloads. */
self.addEventListener('push', (event) => {
  let data = {};
  try {
    data = event.data ? event.data.json() : {};
  } catch {
    data = { body: event.data ? event.data.text() : '' };
  }

  const title = typeof data.title === 'string' && data.title.trim() ? data.title : 'Kyntus';
  const body = typeof data.body === 'string' ? data.body : '';
  const tag = typeof data.tag === 'string' && data.tag.trim() ? data.tag : 'kyntus';
  const url = typeof data.url === 'string' && data.url.startsWith('/') ? data.url : '/';

  event.waitUntil(
    self.registration.showNotification(title, {
      body,
      tag,
      data: { url },
      icon: '/favicon.ico',
      badge: '/favicon.ico',
    }),
  );
});

self.addEventListener('notificationclick', (event) => {
  event.notification.close();
  const target = event.notification.data && event.notification.data.url
    ? event.notification.data.url
    : '/';

  event.waitUntil(
    (async () => {
      const windows = await self.clients.matchAll({ type: 'window', includeUncontrolled: true });
      for (const client of windows) {
        if ('focus' in client) {
          await client.focus();
          if ('navigate' in client && typeof client.navigate === 'function') {
            await client.navigate(target);
          }
          return;
        }
      }
      if (self.clients.openWindow) {
        await self.clients.openWindow(target);
      }
    })(),
  );
});
