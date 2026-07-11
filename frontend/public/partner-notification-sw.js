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
    payload = { title: 'Delivery Alert', body: event.data.text() };
  }

  const title = payload.title || 'Delivery Alert';
  const options = {
    body: payload.body || 'A new delivery request is available.',
    icon: '/Logo.jpg',
    badge: '/favicon.ico',
    requireInteraction: true,
    vibrate: [300, 100, 300],
    data: payload.data || {},
    actions: payload.actions || [
      { action: 'accept', title: 'Accept' },
      { action: 'navigate', title: 'Navigate' },
      { action: 'call', title: 'Call' }
    ]
  };

  event.waitUntil(self.registration.showNotification(title, options));
});

self.addEventListener('notificationclick', event => {
  event.notification.close();

  const data = event.notification.data || {};
  const action = event.action || 'open';
  const orderId = encodeURIComponent(data.orderId || '');
  const address = encodeURIComponent(data.deliveryAddress || '');
  const phone = encodeURIComponent(data.phoneNumber || '');

  let target = '/partner/delivery/mobile';
  if (action === 'accept') {
    target += `?action=accept&orderId=${orderId}`;
  } else if (action === 'navigate') {
    target += `?action=navigate&address=${address}`;
  } else if (action === 'call') {
    target += `?action=call&phone=${phone}`;
  }

  event.waitUntil(
    self.clients.matchAll({ type: 'window', includeUncontrolled: true }).then(clients => {
      for (const client of clients) {
        if ('focus' in client && client.url.includes('/partner/delivery/mobile')) {
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
