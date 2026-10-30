import { CommonModule, DOCUMENT } from '@angular/common';
import { Component, HostListener, OnDestroy, OnInit, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { Subscription, distinctUntilChanged, interval, map } from 'rxjs';
import { MenuItem, MenuService } from '../../services/menu.service';
import { OutletService } from '../../services/outlet.service';
import { DeliveryPartner, DeliveryPartnerService } from '../../services/delivery-partner.service';
import { PhoneDeliveryOrder, PhoneOrderQuote, PhoneOrderRequest, PhoneOrderService } from '../../services/phone-order.service';
import { getOrderingUnavailableReason } from '../../models/outlet.model';
import { shopMenuPrice } from '../../utils/shop-menu-price';
import { decodeHtmlEntities } from '../../utils/text-utils';
import { OutletSelectorComponent } from '../outlet-selector/outlet-selector.component';

interface PhoneCartLine {
  item: MenuItem;
  quantity: number;
  variantName?: string;
  addOnNames: string[];
}

@Component({
  selector: 'app-phone-orders',
  standalone: true,
  imports: [CommonModule, FormsModule, OutletSelectorComponent],
  templateUrl: './phone-orders.component.html',
  styleUrls: ['./phone-orders.component.scss']
})
export class PhoneOrdersComponent implements OnInit, OnDestroy {
  private readonly service = inject(PhoneOrderService);
  private readonly menuService = inject(MenuService);
  private readonly partnerService = inject(DeliveryPartnerService);
  private readonly outlets = inject(OutletService);
  private readonly document = inject(DOCUMENT);
  private readonly subscriptions = new Subscription();
  private outletSubscriptions = new Subscription();
  private quoteSubscription?: Subscription;
  private lastPayload = '';
  private idempotencyKey = '';

  readonly displayText = decodeHtmlEntities;
  menuItems: MenuItem[] = [];
  cart: PhoneCartLine[] = [];
  orders: PhoneDeliveryOrder[] = [];
  partners: DeliveryPartner[] = [];
  selectedVariants: Record<string, string> = {};
  selectedAddOns: Partial<Record<string, string[]>> = {};
  assignments: Record<string, string> = {};
  search = '';
  customerName = '';
  phoneNumber = '';
  deliveryAddress = '';
  deliveryLocationUrl = '';
  preparationNotes = '';
  paymentMethod: PhoneOrderRequest['paymentMethod'] = 'cod';
  upiReference = '';
  quote: PhoneOrderQuote | null = null;
  quoteBusy = false;
  submitting = false;
  loading = false;
  busyOrderId = '';
  error = '';
  message = '';
  loadedOutletId = '';
  lastCreated: PhoneDeliveryOrder | null = null;

  ngOnInit(): void {
    this.subscriptions.add(this.outlets.selectedOutlet$.pipe(
      map(outlet => outlet?._id || outlet?.id || ''),
      distinctUntilChanged()
    ).subscribe(outletId => {
      this.invalidateQuote();
      if (this.hasDraft && this.loadedOutletId && outletId !== this.loadedOutletId) {
        this.error = 'Outlet changed. Return to the original outlet or clear this draft before starting another order.';
        return;
      }
      this.loadOutlet(outletId);
    }));
    this.subscriptions.add(interval(15000).subscribe(() => {
      this.refreshOrders();
      if (this.outletMatches) this.loadPartners();
    }));
  }

  ngOnDestroy(): void {
    this.quoteSubscription?.unsubscribe();
    this.outletSubscriptions.unsubscribe();
    this.subscriptions.unsubscribe();
  }

  @HostListener('window:beforeunload', ['$event'])
  onBeforeUnload(event: BeforeUnloadEvent): void {
    if (this.hasDraft) event.preventDefault();
  }

  get hasDraft(): boolean {
    return this.submitting || !!(this.cart.length || this.customerName || this.phoneNumber || this.deliveryAddress || this.deliveryLocationUrl);
  }

  get outletMatches(): boolean {
    return !!this.loadedOutletId && this.loadedOutletId === this.outlets.getSelectedOutletId();
  }

  get orderingError(): string {
    return getOrderingUnavailableReason(this.outlets.getSelectedOutlet(), 'delivery');
  }

  get filteredMenu(): MenuItem[] {
    const search = this.search.trim().toLowerCase();
    return this.menuItems.filter(item => item.isAvailable !== false && !item.isAddOnOnly
      && (!search || this.displayText(item.name).toLowerCase().includes(search)
        || this.displayText(item.categoryName || item.category || '').toLowerCase().includes(search)));
  }

  get validDetails(): boolean {
    const phoneDigits = this.phoneNumber.replace(/\D/g, '');
    return this.outletMatches && !this.orderingError && this.cart.length > 0
      && !!this.customerName.trim() && /^\+?[0-9 ()-]{10,20}$/.test(this.phoneNumber.trim())
      && phoneDigits.length >= 10 && phoneDigits.length <= 15
      && !!this.deliveryAddress.trim() && this.validMapsUrl;
  }

  get validMapsUrl(): boolean {
    try {
      const url = new URL(this.deliveryLocationUrl);
      return url.protocol === 'https:' && !url.username && !url.password && !url.port
        && (url.hostname === 'maps.app.goo.gl' || url.hostname === 'maps.google.com'
          || (['google.com', 'www.google.com', 'google.co.in', 'www.google.co.in'].includes(url.hostname)
            && url.pathname.startsWith('/maps'))
          || (url.hostname === 'goo.gl' && url.pathname.startsWith('/maps')));
    } catch {
      return false;
    }
  }

  get canSubmit(): boolean {
    return this.validDetails && !!this.quote && !this.quoteBusy && !this.submitting
      && (this.paymentMethod !== 'upi-qr' || !!this.upiReference.trim());
  }

  menuPrice(item: MenuItem): number {
    return shopMenuPrice(item, this.selectedVariants[item.id]);
  }

  linePrice(line: PhoneCartLine): number {
    return shopMenuPrice(line.item, line.variantName) + line.addOnNames.reduce((sum, name) =>
      sum + (line.item.addOns?.find(addOn => addOn.name === name)?.price || 0), 0);
  }

  get subtotal(): number {
    return this.cart.reduce((sum, line) => sum + this.linePrice(line) * line.quantity, 0);
  }

  toggleAddOn(itemId: string, name: string, event: Event): void {
    const names = new Set(this.selectedAddOns[itemId] || []);
    if ((event.target as HTMLInputElement).checked) names.add(name);
    else names.delete(name);
    this.selectedAddOns[itemId] = [...names].sort();
  }

  addItem(item: MenuItem): void {
    const variantName = this.selectedVariants[item.id] || undefined;
    const addOnNames = [...(this.selectedAddOns[item.id] || [])];
    const existing = this.cart.find(line => line.item.id === item.id
      && line.variantName === variantName && JSON.stringify(line.addOnNames) === JSON.stringify(addOnNames));
    if (existing && existing.quantity >= 100) {
      this.error = 'A single item selection cannot exceed 100 portions.';
      return;
    }
    if (existing) existing.quantity++;
    else this.cart.push({ item, quantity: 1, variantName, addOnNames });
    this.invalidateQuote();
  }

  changeQuantity(line: PhoneCartLine, change: number): void {
    line.quantity += change;
    if (line.quantity <= 0) this.cart = this.cart.filter(entry => entry !== line);
    this.invalidateQuote();
  }

  invalidateQuote(): void {
    this.quoteSubscription?.unsubscribe();
    this.quoteBusy = false;
    this.quote = null;
  }

  reviewTotal(): void {
    if (!this.validDetails || this.quoteBusy) {
      this.error = 'Add items, customer name, valid phone, delivery address and a Google Maps link, and select an available outlet.';
      return;
    }
    this.quoteBusy = true;
    this.error = '';
    this.quoteSubscription = this.service.quote({ ...this.request(), paymentMethod: 'cod', upiReference: undefined }).subscribe({
      next: quote => {
        this.quote = quote;
        this.quoteBusy = false;
      },
      error: (error: unknown) => {
        this.error = this.errorText(error, 'Could not calculate the delivery charge.');
        this.quoteBusy = false;
      }
    });
  }

  submit(): void {
    if (!this.canSubmit) {
      this.error = 'Review the total first and provide the prepaid UPI reference when applicable.';
      return;
    }
    const request = { ...this.request(), expectedTotal: this.quote!.total };
    const payload = JSON.stringify(request);
    if (payload !== this.lastPayload) {
      this.lastPayload = payload;
      this.idempotencyKey = crypto.randomUUID();
    }
    this.submitting = true;
    this.error = '';
    this.message = '';
    this.subscriptions.add(this.service.create(request, this.idempotencyKey).subscribe({
      next: result => {
        if (!result.order?.id) {
          this.error = 'Order creation is still in progress. Retry unchanged to check the same order; do not enter it again.';
          this.submitting = false;
          return;
        }
        this.lastCreated = result.order;
        this.submitting = false;
        this.clearDraft();
        this.message = result.message;
        this.refreshOrders();
      },
      error: (error: unknown) => {
        this.submitting = false;
        this.error = this.errorText(error, 'Order submission failed. Retry unchanged to avoid a duplicate order.');
        if (error instanceof HttpErrorResponse && error.status === 409) this.invalidateQuote();
      }
    }));
  }

  clearDraft(): void {
    this.cart = [];
    this.customerName = '';
    this.phoneNumber = '';
    this.deliveryAddress = '';
    this.deliveryLocationUrl = '';
    this.preparationNotes = '';
    this.paymentMethod = 'cod';
    this.upiReference = '';
    this.error = '';
    this.invalidateQuote();
    if (!this.outletMatches) this.loadOutlet(this.outlets.getSelectedOutletId() || '');
  }

  refreshOrders(): void {
    if (!this.outletMatches || this.loading) return;
    const outletId = this.loadedOutletId;
    this.loading = true;
    this.outletSubscriptions.add(this.service.getOrders(outletId).subscribe({
      next: result => {
        if (this.loadedOutletId === outletId) this.orders = result.orders;
        this.loading = false;
      },
      error: (error: unknown) => {
        this.error = this.errorText(error, 'Could not refresh phone orders.');
        this.loading = false;
      }
    }));
  }

  assign(order: PhoneDeliveryOrder): void {
    const partnerId = this.assignments[order.id];
    if (!partnerId) {
      this.error = 'Select a delivery partner first.';
      return;
    }
    this.busyOrderId = order.id;
    this.error = '';
    this.subscriptions.add(this.partnerService.assignDeliveryPartner({
      orderId: order.id, deliveryPartnerId: partnerId
    }, 'shop').subscribe({
      next: result => {
        this.message = result.message;
        this.busyOrderId = '';
        this.refreshOrders();
        this.loadPartners();
      },
      error: (error: unknown) => {
        this.error = this.errorText(error, 'Could not assign this delivery partner.');
        this.busyOrderId = '';
      }
    }));
  }

  isActive(order: PhoneDeliveryOrder): boolean {
    return !['delivered', 'cancelled'].includes(order.status);
  }

  getAssignablePartners(order: PhoneDeliveryOrder): DeliveryPartner[] {
    return this.partners.filter(partner => order.paymentMethod !== 'cod' || partner.codAllowed !== false);
  }

  hasAssignablePartners(order: PhoneDeliveryOrder): boolean {
    return this.getAssignablePartners(order).length > 0;
  }

  trackingUrl(order: PhoneDeliveryOrder): string {
    return new URL(order.trackingPath, this.document.location.origin).href;
  }

  async copyTracking(order: PhoneDeliveryOrder): Promise<void> {
    try {
      await navigator.clipboard.writeText(this.trackingUrl(order));
      this.message = 'Customer tracking link copied. Share it privately with the customer.';
    } catch {
      this.error = 'Could not copy the tracking link. Select and copy the link shown below the order.';
    }
  }

  private request(): PhoneOrderRequest {
    return {
      outletId: this.loadedOutletId,
      customerName: this.customerName.trim(),
      phoneNumber: this.phoneNumber.trim(),
      deliveryAddress: this.deliveryAddress.trim(),
      deliveryLocationUrl: this.deliveryLocationUrl.trim(),
      items: this.cart.map(line => ({
        menuItemId: line.item.id, quantity: line.quantity,
        selectedVariantName: line.variantName, selectedAddOnNames: line.addOnNames
      })),
      paymentMethod: this.paymentMethod,
      upiReference: this.paymentMethod === 'upi-qr' ? this.upiReference.trim() : undefined,
      preparationNotes: this.preparationNotes.trim() || undefined
    };
  }

  private loadOutlet(outletId: string): void {
    this.outletSubscriptions.unsubscribe();
    this.outletSubscriptions = new Subscription();
    this.loading = false;
    this.loadedOutletId = outletId;
    this.menuItems = [];
    this.orders = [];
    this.partners = [];
    this.lastCreated = null;
    if (!outletId) {
      this.error = 'Select an outlet before entering a phone order.';
      return;
    }
    this.error = '';
    this.outletSubscriptions.add(this.menuService.getMenuItems().subscribe({
      next: items => {
        if (this.loadedOutletId !== outletId) return;
        this.menuItems = items;
        this.selectedVariants = Object.fromEntries(items.flatMap(item =>
          item.variants?.length ? [[item.id, item.variants[0].variantName]] : []));
      },
      error: (error: unknown) => { this.error = this.errorText(error, 'Could not load menu items.'); }
    }));
    this.refreshOrders();
    this.loadPartners();
  }

  private loadPartners(): void {
    const outletId = this.loadedOutletId;
    this.outletSubscriptions.add(this.partnerService.getDeliveryPartners().subscribe({
      next: partners => {
        if (this.loadedOutletId === outletId) this.partners = partners.filter(partner => partner.status === 'available');
      },
      error: (error: unknown) => { this.error = this.errorText(error, 'Could not load delivery partners.'); }
    }));
  }

  private errorText(error: unknown, fallback: string): string {
    if (error instanceof HttpErrorResponse) return error.error?.error || error.error?.message || fallback;
    return error instanceof Error ? error.message : fallback;
  }
}
