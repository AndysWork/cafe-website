/**
 * Date utility functions to ensure all dates are in IST (Indian Standard Time)
 * IST is UTC+5:30
 */

const IST_OFFSET_MINUTES = 330; // 5 hours 30 minutes

/**
 * Get current date and time in IST
 */
export function getIstNow(): Date {
  const now = new Date();
  const utc = now.getTime() + (now.getTimezoneOffset() * 60000);
  return new Date(utc + (IST_OFFSET_MINUTES * 60000));
}

/**
 * Get current IST date in YYYY-MM-DD format
 */
export function getIstDateString(): string {
  const istNow = getIstNow();
  return istNow.toISOString().split('T')[0];
}

/**
 * Convert any date to IST
 */
export function convertToIst(date: Date | string): Date {
  const dateObj = typeof date === 'string' ? new Date(date) : date;
  const utc = dateObj.getTime() + (dateObj.getTimezoneOffset() * 60000);
  return new Date(utc + (IST_OFFSET_MINUTES * 60000));
}

/**
 * Format date in IST timezone
 */
export function formatIstDate(date: Date | string, options?: Intl.DateTimeFormatOptions): string {
  const istDate = convertToIst(date);
  return istDate.toLocaleDateString('en-IN', options || {
    year: 'numeric',
    month: 'short',
    day: 'numeric'
  });
}

/**
 * Format date time in IST timezone
 */
export function formatIstDateTime(date: Date | string): string {
  const istDate = convertToIst(date);
  return istDate.toLocaleString('en-IN', {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit'
  });
}

/**
 * Get IST date for input[type="date"] fields
 * Returns YYYY-MM-DD format in IST
 */
export function getIstInputDate(date?: Date | string): string {
  const istDate = date ? convertToIst(date) : getIstNow();
  const year = istDate.getFullYear();
  const month = String(istDate.getMonth() + 1).padStart(2, '0');
  const day = String(istDate.getDate()).padStart(2, '0');
  return `${year}-${month}-${day}`;
}

/**
 * Get start of day in IST
 */
export function getIstStartOfDay(date?: Date | string): Date {
  const istDate = date ? convertToIst(date) : getIstNow();
  istDate.setHours(0, 0, 0, 0);
  return istDate;
}

/**
 * Get end of day in IST
 */
export function getIstEndOfDay(date?: Date | string): Date {
  const istDate = date ? convertToIst(date) : getIstNow();
  istDate.setHours(23, 59, 59, 999);
  return istDate;
}

/**
 * Get IST timestamp
 */
export function getIstTimestamp(): number {
  return getIstNow().getTime();
}

/**
 * Parse date string and ensure it's interpreted as IST
 */
export function parseIstDate(dateString: string): Date {
  // If date string is in YYYY-MM-DD format, create date in IST
  if (/^\d{4}-\d{2}-\d{2}$/.test(dateString)) {
    const [year, month, day] = dateString.split('-').map(Number);
    const istDate = new Date();
    const utc = Date.UTC(year, month - 1, day);
    return new Date(utc + (IST_OFFSET_MINUTES * 60000));
  }
  // For other formats, convert to IST
  return convertToIst(dateString);
}

/**
 * Get number of days between two IST dates
 */
export function getIstDaysDifference(date1: Date | string, date2: Date | string): number {
  const istDate1 = convertToIst(date1);
  const istDate2 = convertToIst(date2);
  const diffTime = Math.abs(istDate2.getTime() - istDate1.getTime());
  return Math.ceil(diffTime / (1000 * 60 * 60 * 24));
}
