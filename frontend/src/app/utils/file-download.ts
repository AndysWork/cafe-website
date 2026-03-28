/**
 * Shared file download utility — replaces duplicated Blob → createObjectURL → click → revokeObjectURL
 * pattern across 11 component instances.
 */

export type FileContentType = 'text/csv' | 'text/csv;charset=utf-8;' | 'text/plain' | 'application/octet-stream';

/**
 * Download string content as a file.
 * @param content The text content to download
 * @param filename The filename for the downloaded file
 * @param contentType MIME type (defaults to text/csv)
 */
export function downloadFile(content: string, filename: string, contentType: FileContentType = 'text/csv'): void {
  const blob = new Blob([content], { type: contentType });
  const url = window.URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = filename;
  a.click();
  window.URL.revokeObjectURL(url);
}

/**
 * Convert a 2D array to CSV string content.
 * Each cell is quoted to handle commas in values.
 */
export function toCsv(headers: string[], rows: (string | number)[][]): string {
  const headerLine = headers.join(',');
  const dataLines = rows.map(row => row.map(cell => `"${cell}"`).join(','));
  return [headerLine, ...dataLines].join('\n');
}
