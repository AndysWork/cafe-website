import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Subscription } from 'rxjs';
import { MenuService, MenuItem, MenuCategory } from '../../services/menu.service';
import { CartService, Cart } from '../../services/cart.service';
import { Router } from '@angular/router';

@Component({
  selector: 'app-menu',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './menu.component.html',
  styleUrls: ['./menu.component.scss']
})
export class MenuComponent implements OnInit, OnDestroy {
  categories: MenuCategory[] = [];
  menuItems: MenuItem[] = [];
  filteredItems: MenuItem[] = [];
  selectedCategoryId: string | null = null;
  isLoading = false;
  errorMessage = '';
  searchQuery = '';
  cart: Cart = { items: [], subtotal: 0, tax: 0, total: 0, itemCount: 0 };

  // Track which items just got added (for animation)
  recentlyAdded: Set<string> = new Set();

  private menuRefreshSubscription?: Subscription;
  private cartSubscription?: Subscription;

  constructor(
    private menuService: MenuService,
    private cartService: CartService,
    private router: Router
  ) {}

  ngOnInit() {
    this.loadMenu();

    this.cartSubscription = this.cartService.cart$.subscribe(cart => {
      this.cart = cart;
    });

    this.menuRefreshSubscription = this.menuService.menuItemsRefresh$.subscribe((refresh) => {
      if (refresh) {
        this.loadMenu();
      }
    });
  }

  ngOnDestroy() {
    this.menuRefreshSubscription?.unsubscribe();
    this.cartSubscription?.unsubscribe();
  }

  loadMenu() {
    this.isLoading = true;
    this.errorMessage = '';

    Promise.all([
      this.menuService.getCategories().toPromise(),
      this.menuService.getMenuItems().toPromise()
    ])
    .then(([categories, items]) => {
      this.categories = categories || [];
      this.menuItems = items || [];
      this.applyFilters();
      this.isLoading = false;
    })
    .catch(error => {
      console.error('Error loading menu:', error);
      this.errorMessage = 'Failed to load menu. Please try again.';
      this.isLoading = false;
    });
  }

  applyFilters() {
    let items = this.menuItems;

    if (this.selectedCategoryId) {
      items = items.filter(item => item.categoryId === this.selectedCategoryId);
    }

    if (this.searchQuery.trim()) {
      const query = this.searchQuery.toLowerCase().trim();
      items = items.filter(item =>
        item.name.toLowerCase().includes(query) ||
        (item.description && item.description.toLowerCase().includes(query)) ||
        (item.categoryName && item.categoryName.toLowerCase().includes(query))
      );
    }

    this.filteredItems = items;
  }

  onSearchChange() {
    this.applyFilters();
  }

  clearSearch() {
    this.searchQuery = '';
    this.applyFilters();
  }

  filterByCategory(categoryId: string | null) {
    this.selectedCategoryId = categoryId;
    this.applyFilters();
  }

  getCartQuantity(itemId: string): number {
    const cartItem = this.cart.items.find(i => i.menuItemId === itemId);
    return cartItem ? cartItem.quantity : 0;
  }

  addToCart(item: MenuItem) {
    this.cartService.addItem({
      menuItemId: item.id,
      name: item.name,
      description: item.description,
      categoryName: item.categoryName,
      price: item.onlinePrice,
      imageUrl: item.imageUrl
    }, 1);

    // Trigger animation
    this.recentlyAdded.add(item.id);
    setTimeout(() => this.recentlyAdded.delete(item.id), 600);
  }

  increaseQuantity(item: MenuItem) {
    const current = this.getCartQuantity(item.id);
    this.cartService.updateQuantity(item.id, current + 1);
  }

  decreaseQuantity(item: MenuItem) {
    const current = this.getCartQuantity(item.id);
    if (current > 1) {
      this.cartService.updateQuantity(item.id, current - 1);
    } else {
      this.cartService.removeItem(item.id);
    }
  }

  goToCart() {
    this.router.navigate(['/cart']);
  }

  getCategoryItemCount(categoryId: string | null): number {
    if (!categoryId) return this.menuItems.length;
    return this.menuItems.filter(item => item.categoryId === categoryId).length;
  }

  trackByItemId(index: number, item: MenuItem): string {
    return item.id;
  }
}
