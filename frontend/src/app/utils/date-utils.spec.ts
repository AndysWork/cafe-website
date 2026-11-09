import { getIstHour, getStoredTimestampEpoch } from './date-utils';

describe('stored order timestamps', () => {
  it('uses the marked UTC instant without the legacy offset adjustment', () => {
    expect(getStoredTimestampEpoch('2026-11-07T18:30:00Z', true))
      .toBe(Date.parse('2026-11-07T18:30:00Z'));
  });

  it('retains the existing legacy wall-clock interpretation', () => {
    expect(getStoredTimestampEpoch('2026-11-08T00:00:00Z'))
      .toBe(Date.parse('2026-11-07T18:30:00Z'));
  });

  it('treats a date input as IST midnight', () => {
    expect(getStoredTimestampEpoch('2026-11-08'))
      .toBe(Date.parse('2026-11-07T18:30:00Z'));
  });

  it('computes the hour in IST rather than the browser timezone', () => {
    expect(getIstHour(Date.parse('2026-11-07T18:30:00Z'))).toBe(0);
    expect(getIstHour(Date.parse('2026-11-08T12:45:00Z'))).toBe(18);
  });
});
