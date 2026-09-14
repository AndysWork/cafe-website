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
  status: string; // confirmed, preparing, ready, delivered
  createdAt: string;
  roundSubtotal: number;
  items: DineInRoundItem[];
}

export interface DineInBill {
  sessionId: string;
  outletId: string;
  outletName: string;
  tableNumber: string;
  customerName?: string;
  customerPhone?: string;
  status: 'active' | 'bill_requested' | 'paid' | 'cancelled';
  paymentStatus: 'unpaid' | 'pending' | 'paid';
  paymentMethod?: string;
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
  razorpayEnabled: boolean;
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
  paymentMethod: 'upi-qr' | 'razorpay' | 'cash_at_counter';
  upiReference?: string;
  razorpayOrderId?: string;
  razorpayPaymentId?: string;
  razorpaySignature?: string;
  notes?: string;
}
