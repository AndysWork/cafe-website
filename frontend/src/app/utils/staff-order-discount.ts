export function staffOrderDiscount(gross: number): number {
  return Math.round((Math.max(0, gross) * 0.2 + Number.EPSILON) * 100) / 100;
}
