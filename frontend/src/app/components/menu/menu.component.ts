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
import { OutletService } from '../../services/outlet.service';
import { Outlet } from '../../models/outlet.model';
import { UIStore } from '../../store/ui.store';
import { Router } from '@angular/router';
import { decodeHtmlEntities } from '../../utils/text-utils';

@Component({
  selector: 'app-menu',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './menu.component.html',
  styleUrls: ['./menu.component.scss']
})
export class MenuComponent implements OnInit, OnDestroy {
  private readonly checkoutDraftStorageKey = 'checkout_draft';
  private readonly orderStartHour = 9;
  private readonly orderCutoffHour = 22;

  categories: MenuCategory[] = [];
  visibleCategories: MenuCategory[] = [];
  menuItems: MenuItem[] = [];
  filteredItems: MenuItem[] = [];
  selectedCategoryId: string | null = null;
  isLoading = false;
  errorMessage = '';
  searchQuery = '';
  sortBy: string = 'default';
  dietaryFilter: 'all' | 'veg' | 'non-veg' | 'egg' = 'all';
  cart: Cart = { items: [], subtotal: 0, packagingCharges: 0, total: 0, itemCount: 0 };
  outlets: Outlet[] = [];
  selectedOutletId: string = '';

  // Track which items just got added (for animation)
  recentlyAdded: Set<string> = new Set();

  // Detail modal
  selectedMenuItem: MenuItem | null = null;
  showDetailModal = false;

  // Customization modal
  customizationItem: MenuItem | null = null;
  showCustomizationModal = false;
  selectedVariantName = '';
  selectedAddOnNames: Set<string> = new Set();
  customizationQuantity = 1;

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
  private orderingStatusTimer?: ReturnType<typeof setInterval>;
  private currentTime = new Date();

  constructor(
    private menuService: MenuService,
    private cartService: CartService,
    private favoriteService: FavoriteService,
    private outletService: OutletService,
    private authService: AuthService,
    private reviewService: CustomerReviewService,
    private loyaltyService: LoyaltyService,
    private uiStore: UIStore,
    private router: Router
  ) {}

  ngOnInit() {
    this.currentTime = new Date();
    this.orderingStatusTimer = setInterval(() => {
      this.currentTime = new Date();
    }, 60000);

    this.initializeOutletAndMenu();
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

  initializeOutletAndMenu(): void {
    this.outletService.getPublicOutlets().subscribe({
      next: (outlets) => {
        this.outlets = (outlets || []).filter(o => o.isActive);

        const storedSelection = this.outletService.getSelectedOutlet();
        const storedSelectionKey = storedSelection ? this.getOutletKey(storedSelection) : '';
        const matchedStoredOutlet = this.outlets.find(o => this.getOutletKey(o) === storedSelectionKey);

        const defaultOutlet = matchedStoredOutlet || this.getPreferredDefaultOutlet(this.outlets);
        if (defaultOutlet) {
          this.selectedOutletId = this.getOutletKey(defaultOutlet);
          this.outletService.selectOutlet(defaultOutlet);
        }

        this.loadMenu();
      },
      error: () => {
        this.loadMenu();
      }
    });
  }

  onOutletChange(): void {
    const selected = this.outlets.find(o => this.getOutletKey(o) === this.selectedOutletId);
    if (selected) {
      this.outletService.selectOutlet(selected);
    } else {
      const fallbackOutlet = this.getPreferredDefaultOutlet(this.outlets);
      if (fallbackOutlet) {
        this.selectedOutletId = this.getOutletKey(fallbackOutlet);
        this.outletService.selectOutlet(fallbackOutlet);
      }
    }

    this.selectedCategoryId = null;
    this.searchQuery = '';
    this.loadMenu();
  }

  getOutletKey(outlet: Outlet): string {
    return outlet._id || outlet.id || '';
  }

  private getOutletMatchKeys(outlet: Outlet | null | undefined): string[] {
    if (!outlet) return [];

    return [outlet._id, outlet.id]
      .map(v => (v || '').toString().trim())
      .filter(v => !!v);
  }

  private getSelectedOutletMatchKeys(): string[] {
    const selectedFromStore = this.outletService.getSelectedOutlet();
    const keys = new Set<string>([
      ...this.getOutletMatchKeys(selectedFromStore),
      (this.selectedOutletId || '').toString().trim()
    ].filter(v => !!v));

    return Array.from(keys);
  }

  private getPreferredDefaultOutlet(outlets: Outlet[]): Outlet | null {
    if (!outlets || outlets.length === 0) return null;

    const kanchrapara = outlets.find(outlet => {
      const name = (outlet.outletName || '').trim().toLowerCase();
      const code = (outlet.outletCode || '').trim().toLowerCase();
      return name.includes('kanchrapara') || code.includes('kpa');
    });

    return kanchrapara || outlets[0] || null;
  }

  private getItemOutletId(item: MenuItem): string {
    return (item.outletId || '').toString().trim();
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
    if (this.orderingStatusTimer) {
      clearInterval(this.orderingStatusTimer);
    }
    if (this.searchDebounceTimer) {
      clearTimeout(this.searchDebounceTimer);
    }
  }

  get isOrderingOpen(): boolean {
    return this.isWithinOrderingWindow && this.isOnlineOrderingEnabledForSelection;
  }

  get orderingClosedMessage(): string {
    if (!this.isWithinOrderingWindow) {
      return 'Add to cart is available daily from 9:00 AM to 10:00 PM.';
    }

    return 'Online ordering is temporarily turned off by the outlet admin.';
  }

  get isWithinOrderingWindow(): boolean {
    const hour = this.currentTime.getHours();
    return hour >= this.orderStartHour && hour < this.orderCutoffHour;
  }

  get isOnlineOrderingEnabledForSelection(): boolean {
    if (this.selectedOutletId) {
      const selectedOutlet = this.outlets.find(o => this.getOutletKey(o) === this.selectedOutletId);
      return selectedOutlet?.settings?.acceptsOnlineOrders !== false;
    }

    if (this.outlets.length === 0) {
      return true;
    }

    return this.outlets.some(o => o.settings?.acceptsOnlineOrders !== false);
  }

  canOrderItem(item: MenuItem): boolean {
    if (!this.isWithinOrderingWindow) {
      return false;
    }

    const itemOutletId = this.getItemOutletId(item);
    if (!itemOutletId) {
      return this.isOnlineOrderingEnabledForSelection;
    }

    const itemOutlet = this.outlets.find(o => this.getOutletMatchKeys(o).includes(itemOutletId));
    return itemOutlet?.settings?.acceptsOnlineOrders !== false;
  }

  private canAddToCart(item?: MenuItem): boolean {
    if (!this.isWithinOrderingWindow) {
      this.uiStore.warning('Shop is closed for online ordering. Ordering is available from 9:00 AM to 10:00 PM.');
      return false;
    }

    if (item ? this.canOrderItem(item) : this.isOnlineOrderingEnabledForSelection) {
      return true;
    }

    this.uiStore.warning('Online ordering is currently turned off by the outlet admin.');
    return false;
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
      this.refreshVisibleCategories();
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
    let items = this.menuItems.filter(item => item.isAddOnOnly !== true);

    if (this.selectedOutletId) {
      const selectedMatchKeys = this.getSelectedOutletMatchKeys();
      items = items.filter(item => {
        const itemOutletId = this.getItemOutletId(item);
        return !itemOutletId || selectedMatchKeys.includes(itemOutletId);
      });
    }

    const selectedCategoryId = this.selectedCategoryId;
    if (selectedCategoryId) {
      items = items.filter(item => this.matchesSelectedCategory(item, selectedCategoryId));
    }

    if (this.searchQuery.trim()) {
      const query = this.searchQuery.toLowerCase().trim();
      items = items.filter(item =>
        this.getDisplayText(item.name).toLowerCase().includes(query) ||
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
        items = [...items].sort((a, b) => this.getDisplayText(a.name).localeCompare(this.getDisplayText(b.name)));
        break;
      case 'name-za':
        items = [...items].sort((a, b) => this.getDisplayText(b.name).localeCompare(this.getDisplayText(a.name)));
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
    return this.cart.items
      .filter(i => i.menuItemId === itemId)
      .reduce((sum, i) => sum + i.quantity, 0);
  }

  addToCart(item: MenuItem) {
    if (!this.canAddToCart(item)) return;

    if (this.hasCustomizations(item)) {
      this.openCustomization(item);
      return;
    }

    this.addToCartWithSelections(item, undefined, []);
  }

  private addToCartWithSelections(
    item: MenuItem,
    selectedVariant?: { variantName: string; price: number; quantity?: number },
    selectedAddOns: { name: string; price: number }[] = [],
    quantity: number = 1
  ) {
    const basePrice = selectedVariant?.price ?? this.getWebPrice(item);
    const addOnPrice = selectedAddOns.reduce((sum, a) => sum + (a.price || 0), 0);
    const unitPrice = basePrice + addOnPrice;

    this.cartService.addItem({
      menuItemId: item.id,
      name: this.getDisplayText(item.name),
      description: item.description,
      categoryName: item.categoryName,
      price: unitPrice,
      basePrice,
      selectedVariant,
      selectedAddOns,
      imageUrl: item.imageUrl,
      imageThumbnailUrl: item.imageThumbnailUrl,
      packagingCharge: item.packagingCharge || 0
    }, quantity);

    // Trigger animation
    this.recentlyAdded.add(item.id);
    setTimeout(() => this.recentlyAdded.delete(item.id), 600);

    this.loadCheckoutDraft();
  }

  increaseQuantity(item: MenuItem) {
    if (!this.canAddToCart(item)) return;

    const lineId = this.getPrimaryCartLineId(item.id);
    if (!lineId) return;

    const line = this.cart.items.find(i => (i.cartLineId || i.menuItemId) === lineId);
    if (!line) return;

    this.cartService.updateQuantity(lineId, line.quantity + 1);
  }

  decreaseQuantity(item: MenuItem) {
    const lineId = this.getPrimaryCartLineId(item.id);
    if (!lineId) return;

    const line = this.cart.items.find(i => (i.cartLineId || i.menuItemId) === lineId);
    if (!line) return;

    if (line.quantity > 1) {
      this.cartService.updateQuantity(lineId, line.quantity - 1);
    } else {
      this.cartService.removeItem(lineId);
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

  hasCustomizations(item: MenuItem): boolean {
    const hasVariants = !!(item.variants && item.variants.length > 0);
    const hasAddOns = !!(item.addOns && item.addOns.some(a => a.isActive !== false));
    return hasVariants || hasAddOns;
  }

  openCustomization(item: MenuItem) {
    if (!this.canAddToCart(item)) return;

    this.customizationItem = item;
    this.customizationQuantity = 1;
    this.selectedAddOnNames = new Set();
    this.selectedVariantName = item.variants && item.variants.length > 0
      ? item.variants[0].variantName
      : '';
    this.showCustomizationModal = true;
  }

  closeCustomization() {
    this.showCustomizationModal = false;
    this.customizationItem = null;
    this.selectedVariantName = '';
    this.selectedAddOnNames = new Set();
    this.customizationQuantity = 1;
  }

  toggleCustomizationAddOn(name: string) {
    if (this.selectedAddOnNames.has(name)) {
      this.selectedAddOnNames.delete(name);
    } else {
      this.selectedAddOnNames.add(name);
    }
    this.selectedAddOnNames = new Set(this.selectedAddOnNames);
  }

  getSelectedVariant(item: MenuItem | null): { variantName: string; price: number; quantity?: number } | undefined {
    if (!item || !item.variants || item.variants.length === 0) return undefined;
    const selected = item.variants.find(v => v.variantName === this.selectedVariantName) || item.variants[0];
    return {
      ...selected,
      price: this.getVariantWebPrice(item, selected)
    };
  }

  getVariantWebPrice(item: MenuItem | null, variant: { price: number }): number {
    if (!item) return Number(variant?.price || 0);

    const variantPrice = Number(variant?.price || 0);
    const webPrice = Number(item.webPrice || 0);
    const shopPrice = Number(item.shopSellingPrice || 0);

    if (variantPrice <= 0) {
      return 0;
    }

    // Variant prices are often authored against shop price; scale to web price when both exist.
    if (webPrice > 0 && shopPrice > 0) {
      const scaled = variantPrice * (webPrice / shopPrice);
      return Math.round(scaled * 100) / 100;
    }

    return Math.round(variantPrice * 100) / 100;
  }

  getSelectedAddOns(item: MenuItem | null): { name: string; price: number }[] {
    if (!item?.addOns || item.addOns.length === 0) return [];

    return item.addOns
      .filter(a => a.isActive !== false && this.selectedAddOnNames.has(a.name))
      .map(a => ({ name: a.name, price: a.price }));
  }

  getCustomizationUnitPrice(item: MenuItem | null): number {
    if (!item) return 0;
    const selectedVariant = this.getSelectedVariant(item);
    const basePrice = selectedVariant?.price ?? this.getWebPrice(item);
    const addOnPrice = this.getSelectedAddOns(item).reduce((sum, a) => sum + (a.price || 0), 0);
    return basePrice + addOnPrice;
  }

  confirmCustomization() {
    if (!this.canAddToCart(this.customizationItem ?? undefined)) return;
    if (!this.customizationItem) return;

    const item = this.customizationItem;
    const selectedVariant = this.getSelectedVariant(item);
    const selectedAddOns = this.getSelectedAddOns(item);
    this.addToCartWithSelections(item, selectedVariant, selectedAddOns, this.customizationQuantity);
    this.closeCustomization();
    this.closeDetail();
  }

  increaseCustomizationQuantity() {
    this.customizationQuantity += 1;
  }

  decreaseCustomizationQuantity() {
    if (this.customizationQuantity > 1) {
      this.customizationQuantity -= 1;
    }
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
      m.isAddOnOnly !== true &&
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
    const selectedMatchKeys = this.getSelectedOutletMatchKeys();

    const visibleItems = this.menuItems.filter(item => {
      if (item.isAddOnOnly === true) return false;

      if (!this.selectedOutletId) return true;
      const itemOutletId = this.getItemOutletId(item);
      return !itemOutletId || selectedMatchKeys.includes(itemOutletId);
    });

    if (!categoryId) return visibleItems.length;
    return visibleItems.filter(item => this.matchesSelectedCategory(item, categoryId)).length;
  }

  private refreshVisibleCategories(): void {
    const outletItems = this.getVisibleMenuItemsForSelectedOutlet();
    const results: MenuCategory[] = [];
    const seen = new Set<string>();

    // Prefer category records from the category endpoint when they map to selected outlet items.
    for (const category of this.categories) {
      const hasMatch = outletItems.some(item => this.matchesCategoryRecordToItem(category, item));
      if (!hasMatch) {
        continue;
      }

      const key = this.normalizeKey(category.id) || `name:${this.normalizeCategoryToken(category.name)}`;
      if (seen.has(key)) {
        continue;
      }

      seen.add(key);
      results.push(category);
    }

    // Add synthetic categories for outlet items that don't map to the category endpoint list.
    for (const item of outletItems) {
      const rawName = this.getDisplayText(item.categoryName || item.category || '').trim();
      if (!rawName) {
        continue;
      }

      const token = this.normalizeCategoryToken(rawName);
      const syntheticId = `name:${token}`;
      if (!token || seen.has(syntheticId)) {
        continue;
      }

      seen.add(syntheticId);
      results.push({ id: syntheticId, name: rawName });
    }

    this.visibleCategories = results;

    if (this.selectedCategoryId) {
      const selectedExists = this.visibleCategories.some(c => c.id === this.selectedCategoryId);
      if (!selectedExists) {
        this.selectedCategoryId = null;
      }
    }
  }

  private getVisibleMenuItemsForSelectedOutlet(): MenuItem[] {
    let items = this.menuItems.filter(item => item.isAddOnOnly !== true);

    if (!this.selectedOutletId) {
      return items;
    }

    const selectedMatchKeys = this.getSelectedOutletMatchKeys();
    items = items.filter(item => {
      const itemOutletId = this.getItemOutletId(item);
      return !itemOutletId || selectedMatchKeys.includes(itemOutletId);
    });

    return items;
  }

  private matchesCategoryRecordToItem(category: MenuCategory, item: MenuItem): boolean {
    const categoryId = this.normalizeKey(category.id);
    const itemCategoryId = this.normalizeKey(item.categoryId);
    if (categoryId && itemCategoryId && categoryId === itemCategoryId) {
      return true;
    }

    const categoryName = this.normalizeText(category.name);
    const itemCategoryName = this.normalizeText(item.categoryName);
    const itemCategoryRaw = this.normalizeText(item.category);

    return this.isCategoryNameMatch(categoryName, itemCategoryName)
      || this.isCategoryNameMatch(categoryName, itemCategoryRaw);
  }

  private matchesSelectedCategory(item: MenuItem, selectedCategoryId: string): boolean {
    const normalizedSelectedId = this.normalizeKey(selectedCategoryId);

    if (normalizedSelectedId.startsWith('name:')) {
      const selectedToken = normalizedSelectedId.slice('name:'.length);
      const itemTokens = [
        this.normalizeCategoryToken(this.normalizeText(item.categoryName)),
        this.normalizeCategoryToken(this.normalizeText(item.category)),
        this.normalizeCategoryToken(this.getCategoryNameById(item.categoryId))
      ].filter(Boolean);

      return itemTokens.some(token => token === selectedToken);
    }

    const normalizedItemCategoryId = this.normalizeKey(item.categoryId);

    if (normalizedItemCategoryId && normalizedItemCategoryId === normalizedSelectedId) {
      return true;
    }

    const selectedCategory = this.categories.find(c => this.normalizeKey(c.id) === normalizedSelectedId);
    const selectedCategoryName = this.normalizeText(selectedCategory?.name);
    if (!selectedCategoryName) {
      return false;
    }

    const itemCategoryName = this.normalizeText(item.categoryName);
    const itemCategoryRaw = this.normalizeText(item.category);
    const itemCategoryNameFromId = this.getCategoryNameById(item.categoryId);

    return this.isCategoryNameMatch(selectedCategoryName, itemCategoryName)
      || this.isCategoryNameMatch(selectedCategoryName, itemCategoryRaw)
      || this.isCategoryNameMatch(selectedCategoryName, itemCategoryNameFromId);
  }

  private getCategoryNameById(categoryId?: string | null): string {
    if (!categoryId) {
      return '';
    }

    const normalizedId = this.normalizeKey(categoryId);
    const category = this.categories.find(c => this.normalizeKey(c.id) === normalizedId);
    return this.normalizeText(category?.name);
  }

  private isCategoryNameMatch(selected: string, candidate: string): boolean {
    if (!selected || !candidate) {
      return false;
    }

    if (selected === candidate) {
      return true;
    }

    const selectedToken = this.normalizeCategoryToken(selected);
    const candidateToken = this.normalizeCategoryToken(candidate);

    if (!selectedToken || !candidateToken) {
      return false;
    }

    return selectedToken === candidateToken
      || selectedToken.includes(candidateToken)
      || candidateToken.includes(selectedToken);
  }

  private normalizeCategoryToken(value: string): string {
    return value.replace(/[^a-z0-9]/gi, '').toLowerCase();
  }

  private normalizeKey(value?: string | null): string {
    return (value || '').trim().toLowerCase();
  }

  private normalizeText(value?: string | null): string {
    return this.getDisplayText(value).trim().toLowerCase();
  }

  getDietaryItemCount(filter: 'all' | 'veg' | 'non-veg' | 'egg'): number {
    const selectedMatchKeys = this.getSelectedOutletMatchKeys();
    const visibleItems = this.menuItems.filter(item => {
      if (item.isAddOnOnly === true) return false;

      if (!this.selectedOutletId) return true;
      const itemOutletId = this.getItemOutletId(item);
      return !itemOutletId || selectedMatchKeys.includes(itemOutletId);
    });

    if (filter === 'all') {
      return visibleItems.length;
    }

    return visibleItems.filter(item => this.normalizeDietaryType(item.dietaryType) === filter).length;
  }

  trackByItemId(index: number, item: MenuItem): string {
    return item.id;
  }

  trackByObjId(index: number, item: any): string {
    return item?.id || item?._id || `${index}`;
  }

  trackByIndex(index: number): number { return index; }

  getWebPrice(item: MenuItem): number {
    const webPrice = Number(item.webPrice || 0);
    return Number.isFinite(webPrice) && webPrice > 0 ? webPrice : 0;
  }

  getDisplayText(value?: string | null): string {
    return decodeHtmlEntities(value);
  }

  getListImageUrl(item: MenuItem): string | undefined {
    return item.imageThumbnailUrl || item.imageUrl;
  }

  getEstimatedEarnPoints(item: MenuItem): number {
    return Math.max(0, Math.floor(this.getWebPrice(item) / 10));
  }

  getCustomizationEarnPoints(item: MenuItem | null): number {
    return Math.max(0, Math.floor(this.getCustomizationUnitPrice(item) / 10));
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
          this.uiStore.success(`Added "${this.getDisplayText(item.name)}" to favorites`);
        } else {
          this.favoriteIds.delete(item.id);
          this.uiStore.notify(`Removed "${this.getDisplayText(item.name)}" from favorites`);
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

  private getPrimaryCartLineId(itemId: string): string | null {
    const line = this.cart.items.find(i => i.menuItemId === itemId);
    if (!line) return null;
    return line.cartLineId || line.menuItemId;
  }
}
