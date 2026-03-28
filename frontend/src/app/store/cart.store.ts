import { Injectable, computed, signal } from '@angular/core';
import { toObservable } from '@angular/core/rxjs-interop';
import { CartItem, Cart } from '../services/cart.service';

/**
 * Centralized Cart State Store using Angular Signals.
 * Single source of truth for shopping cart state.
 */
@Injectable({ providedIn: 'root' })
export class CartStore {
  // ── Private writable signal ──
  private readonly _cart = signal<Cart>(CartStore.emptyCart());

  // ── Public readonly signals ──
  readonly cart = this._cart.asReadonly();

  // ── Computed signals (derived state) ──
  readonly items = computed(() => this._cart().items);
  readonly itemCount = computed(() => this._cart().itemCount);
  readonly subtotal = computed(() => this._cart().subtotal);
  readonly total = computed(() => this._cart().total);
  readonly packagingCharges = computed(() => this._cart().packagingCharges);
  readonly isEmpty = computed(() => this._cart().items.length === 0);

  // ── Observable bridge (backward compatibility) ──
  readonly cart$ = toObservable(this._cart);

  // ── Constructor: hydrate from localStorage ──
  constructor() {
    this.hydrate();
  }

  // ── Actions ──

  addItem(item: Omit<CartItem, 'quantity'>, quantity: number = 1): void {
    const current = this._cart();
    const existingIndex = current.items.findIndex(i => i.menuItemId === item.menuItemId);

    let newItems: CartItem[];
    if (existingIndex > -1) {
      newItems = [...current.items];
      newItems[existingIndex] = {
        ...newItems[existingIndex],
        quantity: newItems[existingIndex].quantity + quantity
      };
    } else {
      newItems = [...current.items, { ...item, quantity }];
    }

    this.updateCart(newItems);
  }

  updateQuantity(menuItemId: string, quantity: number): void {
    if (quantity <= 0) {
      this.removeItem(menuItemId);
      return;
    }
    const newItems = this._cart().items.map(item =>
      item.menuItemId === menuItemId ? { ...item, quantity } : item
    );
    this.updateCart(newItems);
  }

  removeItem(menuItemId: string): CartItem | undefined {
    const removed = this._cart().items.find(i => i.menuItemId === menuItemId);
    const newItems = this._cart().items.filter(i => i.menuItemId !== menuItemId);
    this.updateCart(newItems);
    return removed;
  }

  clearCart(): void {
    this._cart.set(CartStore.emptyCart());
    localStorage.removeItem('cart');
  }

  // ── Helpers ──

  private updateCart(items: CartItem[]): void {
    const cart = CartStore.calculateTotals(items);
    this._cart.set(cart);
    localStorage.setItem('cart', JSON.stringify(cart));
  }

  private hydrate(): void {
    const saved = localStorage.getItem('cart');
    if (saved) {
      try {
        const parsed = JSON.parse(saved);
        this._cart.set(CartStore.calculateTotals(parsed.items ?? []));
      } catch {
        // corrupted data — ignore
      }
    }
  }

  static emptyCart(): Cart {
    return { items: [], subtotal: 0, packagingCharges: 0, total: 0, itemCount: 0 };
  }

  static calculateTotals(items: CartItem[]): Cart {
    const subtotal = items.reduce((sum, item) => sum + (item.price * item.quantity), 0);
    const packagingCharges = items.reduce((sum, item) => sum + ((item.packagingCharge || 0) * item.quantity), 0);
    const total = subtotal + packagingCharges;
    const itemCount = items.reduce((sum, item) => sum + item.quantity, 0);
    return {
      items,
      subtotal: Math.round(subtotal * 100) / 100,
      packagingCharges: Math.round(packagingCharges * 100) / 100,
      total: Math.round(total * 100) / 100,
      itemCount
    };
  }
}
