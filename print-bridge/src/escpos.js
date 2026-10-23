'use strict';

const ESC = 0x1b;
const GS = 0x1d;

const INIT = Buffer.from([ESC, 0x40]);
const CODEPAGE_PC437 = Buffer.from([ESC, 0x74, 0x00]);
const BOLD_ON = Buffer.from([ESC, 0x45, 0x01]);
const BOLD_OFF = Buffer.from([ESC, 0x45, 0x00]);
const DOUBLE_HEIGHT = Buffer.from([GS, 0x21, 0x01]);
const NORMAL_SIZE = Buffer.from([GS, 0x21, 0x00]);
const PARTIAL_CUT = Buffer.from([GS, 0x56, 0x42, 0x00]);
const BEEP = Buffer.from([ESC, 0x42, 0x03, 0x02]);

const REPLACEMENTS = {
  '\u20b9': 'Rs.',
  '\u2013': '-', '\u2014': '-', '\u2212': '-',
  '\u2018': "'", '\u2019': "'", '\u201c': '"', '\u201d': '"',
  '\u2026': '...', '\u2022': '*', '\u00b7': '*', '\u00d7': 'x',
  '\u2500': '-', '\u2550': '=', '\u00a0': ' '
};

/** Lines printed bold and double height: "*** TAKEAWAY ... ***" banners and the parcel token. */
const EMPHASIS_PATTERNS = [/^\s*\*{3}.*\*{3}\s*$/, /^\s*TOKEN:/];

/**
 * Converts text to printable ASCII. Control characters are removed so that
 * customer-entered text (names, notes) cannot inject printer commands.
 */
function sanitizeText(text) {
  const replaced = String(text ?? '')
    .replace(/\r\n?/g, '\n')
    .replace(/\t/g, '  ')
    .replace(/[\u20b9\u2013\u2014\u2212\u2018\u2019\u201c\u201d\u2026\u2022\u00b7\u00d7\u2500\u2550\u00a0]/g, ch => REPLACEMENTS[ch])
    .normalize('NFKD')
    .replace(/[\u0300-\u036f]/g, '');

  let output = '';
  for (const ch of replaced) {
    const code = ch.codePointAt(0);
    if (ch === '\n' || (code >= 0x20 && code <= 0x7e)) output += ch;
    else if (code > 0x7e) output += '?';
  }
  return output.replace(/\?{2,}/g, '?');
}

function buildPrintJob(text, { copies = 1, feedLines = 4, cut = true, beep = false } = {}) {
  const lines = sanitizeText(text).replace(/\n+$/, '').split('\n');
  const body = [];
  for (const line of lines) {
    const content = Buffer.from(`${line}\n`, 'ascii');
    if (EMPHASIS_PATTERNS.some(pattern => pattern.test(line))) {
      body.push(BOLD_ON, DOUBLE_HEIGHT, content, NORMAL_SIZE, BOLD_OFF);
    } else {
      body.push(content);
    }
  }

  const ticket = Buffer.concat([
    INIT,
    CODEPAGE_PC437,
    ...body,
    Buffer.from([ESC, 0x64, Math.min(Math.max(feedLines, 0), 255)]),
    cut ? PARTIAL_CUT : Buffer.alloc(0)
  ]);

  const parts = [];
  for (let i = 0; i < Math.max(1, copies); i++) parts.push(ticket);
  if (beep) parts.push(BEEP);
  return Buffer.concat(parts);
}

module.exports = { sanitizeText, buildPrintJob };
