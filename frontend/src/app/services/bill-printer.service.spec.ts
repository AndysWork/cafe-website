import { TestBed } from '@angular/core/testing';
import { BillPrinterService } from './bill-printer.service';

describe('BillPrinterService', () => {
  let service: BillPrinterService;
  let writes: Uint8Array[];
  let originalBluetooth: PropertyDescriptor | undefined;

  const fakeDevice = (properties: Record<string, boolean>) => {
    const characteristic = {
      properties,
      writeValueWithResponse: jasmine.createSpy('withResponse').and.callFake(async (chunk: Uint8Array) => { writes.push(chunk); }),
      writeValueWithoutResponse: jasmine.createSpy('withoutResponse').and.callFake(async (chunk: Uint8Array) => { writes.push(chunk); })
    };
    const server = {
      getPrimaryServices: async () => [{ getCharacteristics: async () => [{ properties: { notify: true } }, characteristic] }]
    };
    const gatt = { connected: false, connect: jasmine.createSpy('connect').and.callFake(async () => { gatt.connected = true; return server; }), disconnect: () => { gatt.connected = false; } };
    return { name: 'PT-210', gatt, addEventListener: jasmine.createSpy('addEventListener') };
  };

  const installBluetooth = (device: unknown) => {
    Object.defineProperty(navigator, 'bluetooth', {
      configurable: true,
      value: { requestDevice: jasmine.createSpy('requestDevice').and.resolveTo(device) }
    });
  };

  beforeEach(() => {
    localStorage.removeItem('billPrinterSettings');
    originalBluetooth = Object.getOwnPropertyDescriptor(navigator, 'bluetooth');
    writes = [];
    TestBed.configureTestingModule({});
    service = TestBed.inject(BillPrinterService);
  });

  afterEach(() => {
    if (originalBluetooth) Object.defineProperty(navigator, 'bluetooth', originalBluetooth);
    else delete (navigator as any).bluetooth;
    localStorage.removeItem('billPrinterSettings');
  });

  it('defaults to Bluetooth on 58 mm paper with every trigger on, and persists changes', () => {
    expect(service.settings.mode).toBe('ble');
    expect(service.settings.paperWidth).toBe(32);
    expect(Object.values(service.settings.autoPrint).every(Boolean)).toBeTrue();

    service.updateSettings({ paperWidth: 48, autoPrint: { takeawayHandover: false } as any });
    const stored = JSON.parse(localStorage.getItem('billPrinterSettings')!);
    expect(stored.paperWidth).toBe(48);
    expect(stored.autoPrint.takeawayHandover).toBeFalse();
    expect(stored.autoPrint.takeawayPaid).toBeTrue();
  });

  it('connects to a BLE printer and writes the job in 20-byte chunks', async () => {
    installBluetooth(fakeDevice({ write: true }));
    expect(await service.connect()).toBe('PT-210');
    expect(service.status.connected).toBeTrue();

    await service.print([{ text: 'x'.repeat(50) }]);
    expect(writes.length).toBeGreaterThan(1);
    expect(writes.every(chunk => chunk.length <= 20)).toBeTrue();
    const total = writes.reduce((sum, chunk) => sum + chunk.length, 0);
    expect(total).toBeGreaterThan(50);
  });

  it('rejects a Bluetooth device without a writable channel', async () => {
    const device = fakeDevice({ notify: true });
    installBluetooth(device);
    await expectAsync(service.connect()).toBeRejectedWithError(/RawBT/);
    expect(service.status.connected).toBeFalse();
  });

  it('auto-prints only for enabled triggers on a ready printer', async () => {
    const build = jasmine.createSpy('build').and.returnValue([{ text: 'Bill' }]);
    expect(await service.autoPrint('takeawayPaid', build)).toBeFalse();

    installBluetooth(fakeDevice({ writeWithoutResponse: true }));
    await service.connect();
    expect(await service.autoPrint('takeawayPaid', build)).toBeTrue();

    service.updateSettings({ autoPrint: { takeawayPaid: false } as any });
    expect(await service.autoPrint('takeawayPaid', build)).toBeFalse();
    expect(build).toHaveBeenCalledTimes(1);
  });

  it('skips background auto-prints in RawBT mode because the app needs a tap', async () => {
    service.updateSettings({ mode: 'rawbt' });
    const build = jasmine.createSpy('build').and.returnValue([{ text: 'Bill' }]);
    expect(await service.autoPrint('dineInBillRequested', build, false)).toBeFalse();
    expect(build).not.toHaveBeenCalled();
  });

  it('refuses to print when printing is off on this device', async () => {
    service.updateSettings({ mode: 'off' });
    await expectAsync(service.print([{ text: 'Bill' }])).toBeRejectedWithError(/turned off/);
  });
});
