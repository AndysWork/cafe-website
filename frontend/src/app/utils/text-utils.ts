export function decodeHtmlEntities(value: string | null | undefined, maxPasses: number = 5): string {
  const input = (value || '').trim();
  if (!input || !input.includes('&')) {
    return input;
  }

  if (typeof document === 'undefined') {
    return input;
  }

  const decoder = document.createElement('textarea');
  let decoded = input;

  for (let i = 0; i < maxPasses; i += 1) {
    decoder.innerHTML = decoded;
    const next = decoder.value;
    if (next === decoded) {
      break;
    }
    decoded = next;
    if (!decoded.includes('&')) {
      break;
    }
  }

  return decoded;
}

export function resolveWebSalePrice(
  webPrice?: number | null,
  shopSellingPrice?: number | null,
  onlinePrice?: number | null
): number {
  if (typeof webPrice === 'number' && Number.isFinite(webPrice) && webPrice > 0) {
    return webPrice;
  }

  if (typeof shopSellingPrice === 'number' && Number.isFinite(shopSellingPrice) && shopSellingPrice > 0) {
    return shopSellingPrice;
  }

  if (typeof onlinePrice === 'number' && Number.isFinite(onlinePrice) && onlinePrice > 0) {
    return onlinePrice;
  }

  return 0;
}
