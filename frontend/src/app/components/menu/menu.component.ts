import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Subscription } from 'rxjs';
import { MenuService, MenuItem, MenuCategory } from '../../services/menu.service';
import { CartService, Cart } from '../../services/cart.service';
import { FavoriteService } from '../../services/favorite.service';
import { AuthService } from '../../services/auth.service';
import { CustomerReviewService } from '../../services/customer-review.service';
import { LoyaltyService } from '../../services/loyalty.service';
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
  private readonly checkoutDraftStorageKey = 'checkout_draft';

  categories: MenuCategory[] = [];
  menuItems: MenuItem[] = [];
  filteredItems: MenuItem[] = [];
  selectedCategoryId: string | null = null;
  isLoading = false;
  errorMessage = '';
  searchQuery = '';
  sortBy: string = 'default';
  dietaryFilter: 'all' | 'veg' | 'non-veg' | 'egg' = 'all';
  cart: Cart = { items: [], subtotal: 0, packagingCharges: 0, total: 0, itemCount: 0 };

  // Track which items just got added (for animation)
  recentlyAdded: Set<string> = new Set();

  // Detail modal
  selectedMenuItem: MenuItem | null = null;
  showDetailModal = false;

  // Favorites
  favoriteIds: Set<string> = new Set();

  // Social proof
  averageRating = 0;
  totalReviews = 0;

  checkoutDraft: { timestamp: string; cartItemCount: number; grandTotal: number } | null = null;
  loyaltyPointsAvailable = 0;

  // Unavailable item substitution panel
  expandedUnavailableSuggestions: Set<string> = new Set();

  private menuRefreshSubscription?: Subscription;
  private cartSubscription?: Subscription;
  private searchDebounceTimer?: ReturnType<typeof setTimeout>;

  constructor(
    private menuService: MenuService,
    private cartService: CartService,
    private favoriteService: FavoriteService,
    private authService: AuthService,
    private reviewService: CustomerReviewService,
    private loyaltyService: LoyaltyService,
    private uiStore: UIStore,
    private router: Router
  ) {}

  ngOnInit() {
    this.loadMenu();
    this.loadFavorites();
    this.loadSocialProof();
    this.loadCheckoutDraft();
    this.loadLoyaltyPreview();

    this.cartSubscription = this.cartService.cart$.subscribe(cart => {
      this.cart = cart;
    });

    this.menuRefreshSubscription = this.menuService.menuItemsRefresh$.subscribe((refresh) => {
      if (refresh) {
        this.loadMenu();
      }
    });
  }

  get checkoutDraftAgeMinutes(): number {
    if (!this.checkoutDraft?.timestamp) return 0;
    const ts = new Date(this.checkoutDraft.timestamp).getTime();
    if (Number.isNaN(ts)) return 0;
    return Math.max(0, Math.floor((Date.now() - ts) / 60000));
  }

  get estimatedCartEarnPoints(): number {
    return this.cart.items.reduce((sum, item) => {
      const points = Math.floor((item.price || 0) / 10);
      return sum + points * item.quantity;
    }, 0);
  }

  get maxRedeemValuePreview(): number {
    return Math.floor(this.loyaltyPointsAvailable * 0.25);
  }

  ngOnDestroy() {
    this.menuRefreshSubscription?.unsubscribe();
    this.cartSubscription?.unsubscribe();
    if (this.searchDebounceTimer) {
      clearTimeout(this.searchDebounceTimer);
    }
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

    if (this.dietaryFilter !== 'all') {
      items = items.filter(item => this.normalizeDietaryType(item.dietaryType) === this.dietaryFilter);
    }

    // Sorting
    switch (this.sortBy) {
      case 'price-low':
        items = [...items].sort((a, b) => this.getWebPrice(a) - this.getWebPrice(b));
        break;
      case 'price-high':
        items = [...items].sort((a, b) => this.getWebPrice(b) - this.getWebPrice(a));
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
    if (this.searchDebounceTimer) {
      clearTimeout(this.searchDebounceTimer);
    }
    this.searchDebounceTimer = setTimeout(() => {
      this.applyFilters();
    }, 250);
  }

  onSortChange() {
    this.applyFilters();
  }

  setDietaryFilter(filter: 'all' | 'veg' | 'non-veg' | 'egg') {
    this.dietaryFilter = filter;
    this.applyFilters();
  }

  clearSearch() {
    if (this.searchDebounceTimer) {
      clearTimeout(this.searchDebounceTimer);
    }
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
      price: this.getWebPrice(item),
      imageUrl: item.imageUrl,
      imageThumbnailUrl: item.imageThumbnailUrl,
      packagingCharge: item.packagingCharge || 0
    }, 1);

    // Trigger animation
    this.recentlyAdded.add(item.id);
    setTimeout(() => this.recentlyAdded.delete(item.id), 600);

    this.loadCheckoutDraft();
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

  resumeCheckoutFromDraft(): void {
    this.router.navigate(['/checkout']);
  }

  dismissCheckoutDraft(): void {
    this.checkoutDraft = null;
    localStorage.removeItem(this.checkoutDraftStorageKey);
  }

  openDetail(item: MenuItem) {
    this.selectedMenuItem = item;
    this.showDetailModal = true;
  }

  closeDetail() {
    this.showDetailModal = false;
    this.selectedMenuItem = null;
  }

  loadSocialProof() {
    this.reviewService.getAllReviews(1, 50).subscribe({
      next: (res) => {
        this.averageRating = Number.isFinite(res.averageRating) ? res.averageRating : 0;
        this.totalReviews = res.count || 0;
      },
      error: () => {
        this.averageRating = 0;
        this.totalReviews = 0;
      }
    });
  }

  toggleUnavailableSuggestions(itemId: string, event: Event) {
    event.stopPropagation();
    if (this.expandedUnavailableSuggestions.has(itemId)) {
      this.expandedUnavailableSuggestions.delete(itemId);
    } else {
      this.expandedUnavailableSuggestions.add(itemId);
    }
    this.expandedUnavailableSuggestions = new Set(this.expandedUnavailableSuggestions);
  }

  isUnavailableSuggestionsOpen(itemId: string): boolean {
    return this.expandedUnavailableSuggestions.has(itemId);
  }

  getSubstituteItems(item: MenuItem, limit = 3): MenuItem[] {
    const sameCategory = this.menuItems.filter(m =>
      m.id !== item.id &&
      m.isAvailable !== false &&
      m.categoryId === item.categoryId
    );

    const sameDiet = sameCategory.filter(m => (m.dietaryType || 'veg') === (item.dietaryType || 'veg'));
    const pool = sameDiet.length > 0 ? sameDiet : sameCategory;
    return pool.slice(0, limit);
  }

  addSubstituteToCart(substitute: MenuItem, event: Event) {
    event.stopPropagation();
    this.addToCart(substitute);
  }

  getCategoryItemCount(categoryId: string | null): number {
    if (!categoryId) return this.menuItems.length;
    return this.menuItems.filter(item => item.categoryId === categoryId).length;
  }

  getDietaryItemCount(filter: 'all' | 'veg' | 'non-veg' | 'egg'): number {
    if (filter === 'all') {
      return this.menuItems.length;
    }

    return this.menuItems.filter(item => this.normalizeDietaryType(item.dietaryType) === filter).length;
  }

  trackByItemId(index: number, item: MenuItem): string {
    return item.id;
  }

  trackByObjId(index: number, item: any): string { return item.id; }

  trackByIndex(index: number): number { return index; }

  getWebPrice(item: MenuItem): number {
    return item.webPrice || item.shopSellingPrice || item.onlinePrice || 0;
  }

  getListImageUrl(item: MenuItem): string | undefined {
    return item.imageThumbnailUrl || item.imageUrl;
  }

  getEstimatedEarnPoints(item: MenuItem): number {
    return Math.max(0, Math.floor(this.getWebPrice(item) / 10));
  }

  normalizeDietaryType(value?: string): 'veg' | 'non-veg' | 'egg' {
    const normalized = (value || 'veg').trim().toLowerCase();
    if (normalized === 'non-veg' || normalized === 'nonveg') return 'non-veg';
    if (normalized === 'egg') return 'egg';
    return 'veg';
  }

  // ── Favorites ──

  loadFavorites() {
    if (!this.authService.isLoggedIn()) return;
    this.favoriteService.getMyFavorites().subscribe({
      next: (ids) => this.favoriteIds = new Set(ids),
      error: () => this.uiStore.notify('Could not load favorites right now')
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

  private loadCheckoutDraft(): void {
    const raw = localStorage.getItem(this.checkoutDraftStorageKey);
    if (!raw) {
      this.checkoutDraft = null;
      return;
    }

    try {
      const parsed = JSON.parse(raw);
      this.checkoutDraft = {
        timestamp: parsed.timestamp || new Date().toISOString(),
        cartItemCount: Number(parsed.cartItemCount || 0),
        grandTotal: Number(parsed.grandTotal || 0)
      };
    } catch {
      this.checkoutDraft = null;
      localStorage.removeItem(this.checkoutDraftStorageKey);
    }
  }

  private loadLoyaltyPreview(): void {
    if (!this.authService.isLoggedIn()) {
      this.loyaltyPointsAvailable = 0;
      return;
    }

    this.loyaltyService.getLoyaltyAccount().subscribe({
      next: (account) => {
        this.loyaltyPointsAvailable = account?.currentPoints || 0;
      },
      error: () => {
        this.loyaltyPointsAvailable = 0;
      }
    });
  }
}
