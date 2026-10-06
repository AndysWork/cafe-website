export interface DineInRoundItem {
  menuItemId: string;
  name: string;
  quantity: number;
  unitPrice: number;
  totalPrice: number;
  selectedVariantName?: string;
  selectedAddOnNames?: string[];
  preparationNotes?: string;
}

export interface DineInRound {
  roundNumber: number;
  orderId: string;
  status: string; // pending, confirmed, preparing, ready, served, delivered
  createdAt: string;
  roundSubtotal: number;
  items: DineInRoundItem[];
}

export interface DineInPaymentEntry {
  id: string;
  amount: number;
  paymentMethod: string;
  upiReference?: string;
  status: 'pending' | 'confirmed' | 'rejected';
  notes?: string;
  requestedAt: string;
  confirmedAt?: string;
  confirmedBy?: string;
}

export interface DineInVoidRequest {
  id: string;
  orderId: string;
  menuItemId: string;
  itemName: string;
  quantity: number;
  reason: string;
  status: 'pending' | 'approved' | 'rejected';
  requestedBy?: string;
  requestedAt: string;
  reviewedBy?: string;
  reviewedAt?: string;
  reviewNotes?: string;
}

export interface DineInTimelineEvent {
  id: string;
  eventType: string;
  message: string;
  actor?: string;
  createdAt: string;
  metadata?: Record<string, string>;
}

export interface DineInBill {
  sessionId: string;
  outletId: string;
  outletName: string;
  tableNumber: string;
  customerName?: string;
  customerPhone?: string;
  status: 'active' | 'bill_requested' | 'paid' | 'cancelled';
  paymentStatus: 'unpaid' | 'pending_verification' | 'pending_cash' | 'processing' | 'partially_paid' | 'paid';
  paymentMethod?: string;
  amountPaid: number;
  amountDue: number;
  paymentEntries: DineInPaymentEntry[];
  voidRequests: DineInVoidRequest[];
  timeline: DineInTimelineEvent[];
  rounds: DineInRound[];
  totalItemsCount: number;
  subtotal: number;
  discountAmount: number;
  couponCode?: string;
  loyaltyPointsUsed: number;
  loyaltyDiscountAmount: number;
  taxAmount: number;
  grandTotal: number;
  createdAt: string;
  billRequestedAt?: string;
  settledAt?: string;
  canAddItems: boolean;
  canRequestBill: boolean;
  upiQrString?: string;
  upiId?: string;
  payeeName?: string;
  invoiceNumber?: string;
  estimatedPointsToEarn?: number;
  canApplyCoupon?: boolean;
}

export interface StartDineInSessionRequest {
  tableNumber: string;
  outletId?: string;
  customerName?: string;
  customerPhone?: string;
}

export interface SettleDineInBillRequest {
  paymentMethod: 'upi-qr' | 'cash_at_counter';
  amount?: number;
  upiReference?: string;
  notes?: string;
}
