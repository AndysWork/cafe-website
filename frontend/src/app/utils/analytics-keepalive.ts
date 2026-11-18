export async function postAnalyticsKeepalive(url: string, payload: unknown): Promise<void> {
  const response = await fetch(url, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload),
    keepalive: true,
    credentials: 'omit'
  });
  if (!response.ok) {
    throw new Error(`Final analytics delivery failed (${response.status})`);
  }
}
