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
  taxPercentage: number;
  deliveryRadius?: number;
  minimumOrderAmount?: number;
}
