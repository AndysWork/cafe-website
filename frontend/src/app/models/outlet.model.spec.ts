import { getOrderingUnavailableReason, OrderingChannel, OrderingSetting, Outlet } from './outlet.model';

describe('Outlet ordering availability', () => {
  const channels: { channel: OrderingChannel; setting: OrderingSetting }[] = [
    { channel: 'delivery', setting: 'acceptsOnlineOrders' },
    { channel: 'pickup', setting: 'acceptsTakeaway' },
    { channel: 'dine-in', setting: 'acceptsDineIn' },
    { channel: 'counter', setting: 'acceptsCounterSales' }
  ];
  let outlet: Outlet;

  beforeEach(() => {
    outlet = {
      id: 'outlet-1', outletCode: 'MT01', outletName: 'Main',
      address: '', city: '', state: '', isActive: true,
      settings: {
        openingTime: '08:00', closingTime: '22:00', taxPercentage: 5,
        acceptsOnlineOrders: true, acceptsTakeaway: true,
        acceptsDineIn: true, acceptsCounterSales: true
      }
    };
  });

  for (const { channel, setting } of channels) {
    it(`pauses ${channel} independently and permits it after re-enabling`, () => {
      expect(getOrderingUnavailableReason(outlet, channel)).toBe('');
      outlet.settings[setting] = false;
      expect(getOrderingUnavailableReason(outlet, channel)).toContain('temporarily paused');
      for (const other of channels.filter(item => item.channel !== channel)) {
        expect(getOrderingUnavailableReason(outlet, other.channel)).toBe('');
      }
      outlet.settings[setting] = true;
      expect(getOrderingUnavailableReason(outlet, channel)).toBe('');
    });

    it(`blocks ${channel} for an inactive or unselected outlet`, () => {
      outlet.isActive = false;
      expect(getOrderingUnavailableReason(outlet, channel)).toContain('unavailable');
      expect(getOrderingUnavailableReason(null, channel)).toContain('Select an outlet');
    });
  }
});
