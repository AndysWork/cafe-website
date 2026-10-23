export type ReceiptLineStyle = 'normal' | 'bold' | 'large';

export interface ReceiptLine {
  text: string;
  style?: ReceiptLineStyle;
}

const ESC = 0x1b;
const GS = 0x1d;

const REPLACEMENTS: Record<string, string> = {
  '\u20b9': 'Rs.',
  '\u2013': '-', '\u2014': '-', '\u2212': '-',
  '\u2018': "'", '\u2019': "'", '\u201c': '"', '\u201d': '"',
  '\u2026': '...', '\u2022': '*', '\u00b7': '*', '\u00d7': 'x', '\u00a0': ' '
};

/**
 * Converts text to printable ASCII for ESC/POS printers. Control characters are
 * dropped so customer-entered names and notes cannot inject printer commands.
 */
export function sanitizePrinterText(text: string | null | undefined): string {
  const replaced = String(text ?? '')
    .replace(/\r\n?/g, '\n')
    .replace(/\t/g, '  ')
    .replace(/[\u20b9\u2013\u2014\u2212\u2018\u2019\u201c\u201d\u2026\u2022\u00b7\u00d7\u00a0]/g, ch => REPLACEMENTS[ch])
    .normalize('NFKD')
    .replace(/[\u0300-\u036f]/g, '');

  let output = '';
  for (const ch of replaced) {
    const code = ch.codePointAt(0)!;
    if (ch === '\n' || (code >= 0x20 && code <= 0x7e)) output += ch;
    else if (code > 0x7e) output += '?';
  }
  return output.replace(/\?{2,}/g, '?');
}

/** Encodes receipt lines as an ESC/POS job: initialise, print, feed and cut. */
export function encodeEscPos(lines: ReceiptLine[], options: { feedLines?: number; cut?: boolean } = {}): Uint8Array {
  const bytes: number[] = [ESC, 0x40, ESC, 0x74, 0x00];
  const pushText = (text: string) => {
    for (const ch of sanitizePrinterText(text).replace(/\n/g, ' ')) bytes.push(ch.charCodeAt(0));
    bytes.push(0x0a);
  };

  for (const line of lines) {
    if (line.style === 'bold' || line.style === 'large') bytes.push(ESC, 0x45, 0x01);
    if (line.style === 'large') bytes.push(GS, 0x21, 0x01);
    pushText(line.text);
    if (line.style === 'large') bytes.push(GS, 0x21, 0x00);
    if (line.style === 'bold' || line.style === 'large') bytes.push(ESC, 0x45, 0x00);
  }

  bytes.push(ESC, 0x64, Math.min(Math.max(options.feedLines ?? 4, 0), 255));
  if (options.cut !== false) bytes.push(GS, 0x56, 0x42, 0x00);
  return Uint8Array.from(bytes);
}
