import { Injectable } from '@angular/core';
import { BehaviorSubject, Observable } from 'rxjs';

export interface CartItem {
  menuItemId: string;
  name: string;
  description?: string;
  categoryName?: string;
  price: number;
  quantity: number;
  imageUrl?: string;
}

export interface Cart {
  items: CartItem[];
  subtotal: number;
  tax: number;
  total: number;
  itemCount: number;
}

@Injectable({
  providedIn: 'root'
})
export class CartService {
  private cartSubject = new BehaviorSubject<Cart>(this.getEmptyCart());
  public cart$: Observable<Cart> = this.cartSubject.asObservable();

  constructor() {
    // Load cart from localStorage on initialization
    this.loadCartFromStorage();
  }

  private getEmptyCart(): Cart {
    return {
      items: [],
      subtotal: 0,
      tax: 0,
      total: 0,
      itemCount: 0
    };
  }

  private loadCartFromStorage(): void {
    const savedCart = localStorage.getItem('cart');
    if (savedCart) {
      try {
        const cart = JSON.parse(savedCart);
        this.cartSubject.next(this.calculateTotals(cart.items));
      } catch (error) {
        console.error('Error loading cart from storage:', error);
      }
    }
  }

  private saveCartToStorage(cart: Cart): void {
    localStorage.setItem('cart', JSON.stringify(cart));
  }

  private calculateTotals(items: CartItem[]): Cart {
    const subtotal = items.reduce((sum, item) => sum + (item.price * item.quantity), 0);
    const tax = subtotal * 0.10; // 10% tax
    const total = subtotal + tax;
    const itemCount = items.reduce((sum, item) => sum + item.quantity, 0);

    return {
      items,
      subtotal: Math.round(subtotal * 100) / 100,
      tax: Math.round(tax * 100) / 100,
      total: Math.round(total * 100) / 100,
      itemCount
    };
  }

  addItem(item: Omit<CartItem, 'quantity'>, quantity: number = 1): void {
    const currentCart = this.cartSubject.value;
    const existingItemIndex = currentCart.items.findIndex(i => i.menuItemId === item.menuItemId);

    let newItems: CartItem[];
    if (existingItemIndex > -1) {
      // Update quantity of existing item
      newItems = [...currentCart.items];
      newItems[existingItemIndex] = {
        ...newItems[existingItemIndex],
        quantity: newItems[existingItemIndex].quantity + quantity
      };
    } else {
      // Add new item
      newItems = [...currentCart.items, { ...item, quantity }];
    }

    const updatedCart = this.calculateTotals(newItems);
    this.cartSubject.next(updatedCart);
    this.saveCartToStorage(updatedCart);
  }

  updateQuantity(menuItemId: string, quantity: number): void {
    const currentCart = this.cartSubject.value;

    if (quantity <= 0) {
      this.removeItem(menuItemId);
      return;
    }

    const newItems = currentCart.items.map(item =>
      item.menuItemId === menuItemId ? { ...item, quantity } : item
    );

    const updatedCart = this.calculateTotals(newItems);
    this.cartSubject.next(updatedCart);
    this.saveCartToStorage(updatedCart);
  }

  removeItem(menuItemId: string): void {
    const currentCart = this.cartSubject.value;
    const newItems = currentCart.items.filter(item => item.menuItemId !== menuItemId);

    const updatedCart = this.calculateTotals(newItems);
    this.cartSubject.next(updatedCart);
    this.saveCartToStorage(updatedCart);
  }

  clearCart(): void {
    const emptyCart = this.getEmptyCart();
    this.cartSubject.next(emptyCart);
    localStorage.removeItem('cart');
  }

  getCart(): Cart {
    return this.cartSubject.value;
  }

  getItemCount(): number {
    return this.cartSubject.value.itemCount;
  }
}
