import { MenuItem } from '../services/menu.service';

export function shopMenuPrice(item: MenuItem, variantName?: string): number {
  const variantPrice = variantName
    ? item.variants?.find(variant => variant.variantName === variantName)?.price
    : undefined;
  if (variantPrice !== undefined) return variantPrice;
  if ((item.shopSellingPrice || 0) > 0) return item.shopSellingPrice!;
  if ((item.webPrice || 0) > 0) return item.webPrice!;
  return item.onlinePrice || 0;
}
