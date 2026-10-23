import { encodeEscPos, sanitizePrinterText } from './escpos';

describe('escpos', () => {
  it('converts rupee signs and typography to printable ASCII', () => {
    expect(sanitizePrinterText('₹120 – “Chai” café…')).toBe('Rs.120 - "Chai" cafe...');
  });

  it('drops control characters so text cannot inject printer commands', () => {
    expect(sanitizePrinterText('Tea\x1b@\x1dV\x00 ok')).toBe('Tea@V ok');
    expect(sanitizePrinterText('চা Tea')).toBe('? Tea');
    expect(sanitizePrinterText(null)).toBe('');
  });

  it('initialises, styles, feeds and cuts', () => {
    const bytes = Array.from(encodeEscPos([{ text: 'A', style: 'large' }, { text: 'B' }]));
    expect(bytes.slice(0, 5)).toEqual([0x1b, 0x40, 0x1b, 0x74, 0x00]);
    expect(bytes.slice(5, 14)).toEqual([0x1b, 0x45, 0x01, 0x1d, 0x21, 0x01, 0x41, 0x0a, 0x1d]);
    expect(bytes).toContain(0x42);
    expect(bytes.slice(-7)).toEqual([0x1b, 0x64, 4, 0x1d, 0x56, 0x42, 0x00]);
  });

  it('can skip the cut for printers without a cutter', () => {
    const bytes = Array.from(encodeEscPos([{ text: 'A' }], { cut: false, feedLines: 2 }));
    expect(bytes.slice(-3)).toEqual([0x1b, 0x64, 2]);
  });
});
