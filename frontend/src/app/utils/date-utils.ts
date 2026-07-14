/**
 * Date utility functions to ensure all dates are in IST (Indian Standard Time)
 * IST is UTC+5:30
 */

const IST_OFFSET_MINUTES = 330; // 5 hours 30 minutes
const IST_OFFSET_MS = IST_OFFSET_MINUTES * 60_000;
const IST_TIME_ZONE = 'Asia/Kolkata';

function parseDateInput(value: Date | string): Date {
  if (value instanceof Date) {
    return new Date(value.getTime());
  }

  const raw = (value || '').trim();
  if (!raw) {
    return new Date(Number.NaN);
  }

  // Support epoch seconds/milliseconds encoded as strings.
  if (/^\d+$/.test(raw)) {
    const epoch = Number(raw);
    const ms = raw.length === 10 ? epoch * 1000 : epoch;
    return new Date(ms);
  }

  // Date-only strings are interpreted as IST midnight.
  const dateOnly = raw.match(/^(\d{4})-(\d{2})-(\d{2})$/);
  if (dateOnly) {
    const year = Number(dateOnly[1]);
    const month = Number(dateOnly[2]);
    const day = Number(dateOnly[3]);
    return new Date(Date.UTC(year, month - 1, day) - IST_OFFSET_MS);
  }

  // Datetime strings without timezone are interpreted as IST local time.
  const istLocal = raw.match(
    /^(\d{4})-(\d{2})-(\d{2})[ T](\d{2}):(\d{2})(?::(\d{2}))?(?:\.(\d{1,7}))?$/
  );
  if (istLocal) {
    const year = Number(istLocal[1]);
    const month = Number(istLocal[2]);
    const day = Number(istLocal[3]);
    const hour = Number(istLocal[4]);
    const minute = Number(istLocal[5]);
    const second = Number(istLocal[6] || 0);
    const fraction = istLocal[7] || '0';
    const millisecond = Number((fraction + '000').slice(0, 3));
    const utcMs = Date.UTC(year, month - 1, day, hour, minute, second, millisecond) - IST_OFFSET_MS;
    return new Date(utcMs);
  }

  // ISO strings with timezone (or browser-parseable strings) are parsed natively.
  return new Date(raw);
}

function getIstParts(date: Date): { year: number; month: number; day: number } {
  const parts = new Intl.DateTimeFormat('en-IN', {
    timeZone: IST_TIME_ZONE,
    year: 'numeric',
    month: '2-digit',
    day: '2-digit'
  }).formatToParts(date);

  const getPart = (type: Intl.DateTimeFormatPartTypes): number =>
    Number(parts.find(p => p.type === type)?.value || '0');

  return {
    year: getPart('year'),
    month: getPart('month'),
    day: getPart('day')
  };
}

/**
 * Get current date and time in IST
 */
export function getIstNow(): Date {
  return new Date();
}

/**
 * Get current IST date in YYYY-MM-DD format
 */
export function getIstDateString(): string {
  return getIstInputDate(getIstNow());
}

/**
 * Convert any date to IST
 */
export function convertToIst(date: Date | string): Date {
  return parseDateInput(date);
}

/**
 * Format date in IST timezone
 */
export function formatIstDate(date: Date | string, options?: Intl.DateTimeFormatOptions): string {
  const parsed = convertToIst(date);
  if (Number.isNaN(parsed.getTime())) return 'Invalid date';

  return new Intl.DateTimeFormat('en-IN', {
    timeZone: IST_TIME_ZONE,
    ...(options || {
      year: 'numeric',
      month: 'short',
      day: 'numeric'
    })
  }).format(parsed);
}

/**
 * Format date time in IST timezone
 */
export function formatIstDateTime(date: Date | string): string {
  const parsed = convertToIst(date);
  if (Number.isNaN(parsed.getTime())) return 'Invalid date';

  return new Intl.DateTimeFormat('en-IN', {
    timeZone: IST_TIME_ZONE,
    year: 'numeric',
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
    hour12: true
  }).format(parsed);
}

/**
 * Get IST date for input[type="date"] fields
 * Returns YYYY-MM-DD format in IST
 */
export function getIstInputDate(date?: Date | string): string {
  const parsed = date ? convertToIst(date) : getIstNow();
  if (Number.isNaN(parsed.getTime())) return '';

  const { year, month, day } = getIstParts(parsed);
  const monthStr = String(month).padStart(2, '0');
  const dayStr = String(day).padStart(2, '0');
  return `${year}-${monthStr}-${dayStr}`;
}

/**
 * Get start of day in IST
 */
export function getIstStartOfDay(date?: Date | string): Date {
  const parsed = date ? convertToIst(date) : getIstNow();
  if (Number.isNaN(parsed.getTime())) return new Date(Number.NaN);

  const { year, month, day } = getIstParts(parsed);
  return new Date(Date.UTC(year, month - 1, day) - IST_OFFSET_MS);
}

/**
 * Get end of day in IST
 */
export function getIstEndOfDay(date?: Date | string): Date {
  const parsed = date ? convertToIst(date) : getIstNow();
  if (Number.isNaN(parsed.getTime())) return new Date(Number.NaN);

  const { year, month, day } = getIstParts(parsed);
  return new Date(Date.UTC(year, month - 1, day, 23, 59, 59, 999) - IST_OFFSET_MS);
}

/**
 * Get IST timestamp
 */
export function getIstTimestamp(): number {
  return Date.now();
}

/**
 * Parse date string and ensure it's interpreted as IST
 */
export function parseIstDate(dateString: string): Date {
  return convertToIst(dateString);
}

/**
 * Get number of days between two IST dates
 */
export function getIstDaysDifference(date1: Date | string, date2: Date | string): number {
  const istDate1 = getIstStartOfDay(date1);
  const istDate2 = getIstStartOfDay(date2);
  if (Number.isNaN(istDate1.getTime()) || Number.isNaN(istDate2.getTime())) return 0;

  const diffTime = Math.abs(istDate2.getTime() - istDate1.getTime());
  return Math.round(diffTime / (1000 * 60 * 60 * 24));
}

/**
 * Extract YYYY-MM-DD date string from ISO date without timezone conversion
 * This is useful for populating input[type="date"] fields from backend dates
 */
export function extractIstDateString(dateString: string): string {
  return getIstInputDate(dateString);
}
