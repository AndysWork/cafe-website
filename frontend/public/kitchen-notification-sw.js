self.addEventListener('install', event => {
  event.waitUntil(self.skipWaiting());
});

self.addEventListener('activate', event => {
  event.waitUntil(self.clients.claim());
});

self.addEventListener('push', event => {
  if (!event.data) {
    return;
  }

  let payload = {};
  try {
    payload = event.data.json();
  } catch {
    payload = { title: 'Kitchen Alert', body: event.data.text() };
  }

  const data = payload.data || {};
  const title = payload.title || 'New Kitchen Order';
  const options = {
    body: payload.body || 'A new order is waiting in the kitchen queue.',
    icon: '/Logo.jpg',
    badge: '/favicon.ico',
    tag: data.orderId ? `kitchen-order-${data.orderId}` : 'kitchen-order',
    renotify: true,
    requireInteraction: true,
    silent: false,
    vibrate: [400, 120, 400, 120, 400],
    data,
    actions: payload.actions || [
      { action: 'open_kitchen', title: 'Open Kitchen' },
      { action: 'view_dashboard', title: 'Dashboard' }
    ]
  };

  event.waitUntil(self.registration.showNotification(title, options));
});

self.addEventListener('notificationclick', event => {
  event.notification.close();

  const data = event.notification.data || {};
  const action = event.action || 'open_kitchen';

  let target = '/kitchen/display';
  if (action === 'view_dashboard') {
    target = '/kitchen/dashboard';
  }

  if (data.orderId) {
    const separator = target.includes('?') ? '&' : '?';
    target = `${target}${separator}orderId=${encodeURIComponent(data.orderId)}`;
  }

  event.waitUntil(
    self.clients.matchAll({ type: 'window', includeUncontrolled: true }).then(clients => {
      for (const client of clients) {
        if ('focus' in client && (client.url.includes('/kitchen/display') || client.url.includes('/kitchen/dashboard'))) {
          client.focus();
          if ('navigate' in client) {
            return client.navigate(target);
          }
          return Promise.resolve(client);
        }
      }

      if (self.clients.openWindow) {
        return self.clients.openWindow(target);
      }

      return Promise.resolve();
    })
  );
});
