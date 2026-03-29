import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Subscription } from 'rxjs';
import { MenuService, MenuItem, MenuCategory } from '../../services/menu.service';
import { CartService, Cart } from '../../services/cart.service';
import { FavoriteService } from '../../services/favorite.service';
import { AuthService } from '../../services/auth.service';
import { UIStore } from '../../store/ui.store';
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
  sortBy: string = 'default';
  cart: Cart = { items: [], subtotal: 0, packagingCharges: 0, total: 0, itemCount: 0 };

  // Track which items just got added (for animation)
  recentlyAdded: Set<string> = new Set();

  // Detail modal
  selectedMenuItem: MenuItem | null = null;
  showDetailModal = false;

  // Favorites
  favoriteIds: Set<string> = new Set();

  private menuRefreshSubscription?: Subscription;
  private cartSubscription?: Subscription;

  constructor(
    private menuService: MenuService,
    private cartService: CartService,
    private favoriteService: FavoriteService,
    private authService: AuthService,
    private uiStore: UIStore,
    private router: Router
  ) {}

  ngOnInit() {
    this.loadMenu();
    this.loadFavorites();

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

    // Sorting
    switch (this.sortBy) {
      case 'price-low':
        items = [...items].sort((a, b) => a.onlinePrice - b.onlinePrice);
        break;
      case 'price-high':
        items = [...items].sort((a, b) => b.onlinePrice - a.onlinePrice);
        break;
      case 'name-az':
        items = [...items].sort((a, b) => a.name.localeCompare(b.name));
        break;
      case 'name-za':
        items = [...items].sort((a, b) => b.name.localeCompare(a.name));
        break;
    }

    this.filteredItems = items;
  }

  onSearchChange() {
    this.applyFilters();
  }

  onSortChange() {
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
      imageUrl: item.imageUrl,
      packagingCharge: item.packagingCharge || 0
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

  openDetail(item: MenuItem) {
    this.selectedMenuItem = item;
    this.showDetailModal = true;
  }

  closeDetail() {
    this.showDetailModal = false;
    this.selectedMenuItem = null;
  }

  getCategoryItemCount(categoryId: string | null): number {
    if (!categoryId) return this.menuItems.length;
    return this.menuItems.filter(item => item.categoryId === categoryId).length;
  }

  trackByItemId(index: number, item: MenuItem): string {
    return item.id;
  }

  trackByObjId(index: number, item: any): string { return item.id; }

  trackByIndex(index: number): number { return index; }

  // ── Favorites ──

  loadFavorites() {
    if (!this.authService.isLoggedIn()) return;
    this.favoriteService.getMyFavorites().subscribe({
      next: (ids) => this.favoriteIds = new Set(ids),
      error: () => {} // silently fail
    });
  }

  isFavorite(itemId: string): boolean {
    return this.favoriteIds.has(itemId);
  }

  toggleFavorite(item: MenuItem, event: Event) {
    event.stopPropagation();
    if (!this.authService.isLoggedIn()) {
      this.uiStore.warning('Please log in to save favorites');
      return;
    }
    this.favoriteService.toggleFavorite(item.id).subscribe({
      next: (res) => {
        if (res.isFavorite) {
          this.favoriteIds.add(item.id);
          this.uiStore.success(`Added "${item.name}" to favorites`);
        } else {
          this.favoriteIds.delete(item.id);
          this.uiStore.notify(`Removed "${item.name}" from favorites`);
        }
        // Force set re-render
        this.favoriteIds = new Set(this.favoriteIds);
      },
      error: () => this.uiStore.error('Failed to update favorite')
    });
  }
}
