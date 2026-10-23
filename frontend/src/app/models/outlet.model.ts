// Outlet model and related interfaces matching backend
export interface Outlet {
  _id?: string;
  id?: string;
  outletCode: string;
  outletName: string;
  address: string;
  city: string;
  state: string;
  phoneNumber?: string;
  email?: string;
  managerName?: string;
  isActive: boolean;
  settings: OutletSettings;
  createdBy?: string;
  createdDate?: Date;
  lastUpdatedBy?: string;
  lastUpdated?: Date;
}

export interface OutletSettings {
  openingTime: string;
  closingTime: string;
  acceptsOnlineOrders: boolean;
  acceptsDineIn: boolean;
  acceptsTakeaway: boolean;
  acceptsCounterSales: boolean;
  taxPercentage: number;
  deliveryRadius?: number;
  minimumOrderAmount?: number;
}

export type OrderingSetting = 'acceptsOnlineOrders' | 'acceptsTakeaway' | 'acceptsDineIn' | 'acceptsCounterSales';
export type OrderingChannel = 'delivery' | 'pickup' | 'dine-in' | 'counter';

export function getOrderingUnavailableReason(outlet: Outlet | null | undefined, channel: OrderingChannel): string {
  if (!outlet) return 'Select an outlet before placing an order.';
  if (!outlet.isActive) return 'This outlet is unavailable for new orders.';

  const channels: Record<OrderingChannel, { setting: OrderingSetting; label: string }> = {
    delivery: { setting: 'acceptsOnlineOrders', label: 'Online delivery' },
    pickup: { setting: 'acceptsTakeaway', label: 'Takeaway' },
    'dine-in': { setting: 'acceptsDineIn', label: 'Dine-in' },
    counter: { setting: 'acceptsCounterSales', label: 'Counter sales' }
  };
  const { setting, label } = channels[channel];
  return outlet.settings[setting] !== false ? '' : `${label} ordering is temporarily paused for this outlet.`;
}
