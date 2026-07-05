import { Component, OnInit, AfterViewInit, OnDestroy, ViewChild, ElementRef, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, RouterModule } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../../environments/environment';
import { CustomerReviewsComponent } from '../customer-reviews/customer-reviews.component';
import { OutletService } from '../../services/outlet.service';
import { Outlet } from '../../models/outlet.model';
import { AnalyticsTrackingService } from '../../services/analytics-tracking.service';
import { HomeContentConfigService, HomeContentConfig } from '../../services/home-content-config.service';
import { LoyaltyService, LoyaltyAccount, Reward } from '../../services/loyalty.service';
import { OffersService, Offer } from '../../services/offers.service';
import { AuthService } from '../../services/auth.service';
import { OrderService, Order, OrderItem } from '../../services/order.service';
import { CartService } from '../../services/cart.service';
import { MenuService, MenuItem as ServiceMenuItem } from '../../services/menu.service';
import { UIStore } from '../../store/ui.store';

declare const L: any;

function loadLeaflet(): Promise<void> {
  return new Promise((resolve, reject) => {
    if (typeof L !== 'undefined') { resolve(); return; }
    // Load CSS
    const link = document.createElement('link');
    link.rel = 'stylesheet';
    link.href = 'https://unpkg.com/leaflet@1.9.4/dist/leaflet.css';
    document.head.appendChild(link);
    // Load JS
    const script = document.createElement('script');
    script.src = 'https://unpkg.com/leaflet@1.9.4/dist/leaflet.js';
    script.onload = () => resolve();
    script.onerror = () => reject(new Error('Failed to load Leaflet'));
    document.head.appendChild(script);
  });
}

interface Category {
  id: string;
  name: string;
}

interface SubCategory {
  id: string;
  categoryId: string;
  name: string;
}

interface MenuItem {
  id: string;
  categoryId: string;
  subCategoryId: string;
  categoryName?: string;
  catalogueName: string;
}

interface LoyaltyLevelPerk {
  tier: string;
  icon: string;
  pointsBand: string;
  perks: string[];
}

@Component({
  selector: 'app-home',
  standalone: true,
  imports: [CommonModule, RouterModule, CustomerReviewsComponent],
  templateUrl: './home.component.html',
  styleUrls: ['./home.component.scss']
})
export class HomeComponent implements OnInit, AfterViewInit, OnDestroy {
  private readonly checkoutDraftStorageKey = 'checkout_draft';

  @ViewChild('cafeMap', { static: false }) mapElementRef!: ElementRef<HTMLDivElement>;
  latestMenuItems: any[] = [];
  outlets: Outlet[] = [];
  currentYear = new Date().getFullYear();
  homeContentConfig: HomeContentConfig | null = null;
  announcementEnabled = false;
  announcementTitle = '';
  announcementMessage = '';
  loyaltyAccount: LoyaltyAccount | null = null;
  loyaltyRewards: Reward[] = [];
  activeOffers: Offer[] = [];
  loyaltyLoading = false;
  offersLoading = false;
  loyaltyLevelPerks: LoyaltyLevelPerk[] = [
    {
      tier: 'Bronze',
      icon: '🥉',
      pointsBand: '0+ pts',
      perks: ['Base rewards', 'Birthday bonus eligibility']
    },
    {
      tier: 'Silver',
      icon: '🥈',
      pointsBand: '1,000+ pts',
      perks: ['Faster points multiplier', 'Priority support during peak hours']
    },
    {
      tier: 'Gold',
      icon: '🥇',
      pointsBand: '3,000+ pts',
      perks: ['Higher rewards multiplier', 'Exclusive campaign access']
    },
    {
      tier: 'Platinum',
      icon: '💎',
      pointsBand: '7,000+ pts',
      perks: ['Top rewards multiplier', 'Premium member-only drops']
    }
  ];

  testimonials = [
    {
      name: 'Priya Sharma',
      rating: 5,
      text: 'Best cafe in town! The momos are absolutely delicious and the ambiance is perfect for hanging out with friends.',
      image: '👩',
      date: '2 days ago'
    },
    {
      name: 'Rahul Verma',
      rating: 5,
      text: 'Amazing food quality and quick service. The burgers are chef\'s kiss! Highly recommended for families.',
      image: '👨',
      date: '1 week ago'
    },
    {
      name: 'Anjali Patel',
      rating: 5,
      text: 'Love their beverages collection! The cold coffee is my go-to drink. Great value for money.',
      image: '👩‍🦰',
      date: '3 days ago'
    },
    {
      name: 'Vikram Singh',
      rating: 5,
      text: 'Cozy atmosphere and delicious food. The pasta is incredible! Perfect spot for a date night.',
      image: '🧔',
      date: '5 days ago'
    }
  ];

  foodPreparation = [
    { step: 'Fresh Ingredients', icon: '🥬', description: 'We source fresh, quality ingredients daily' },
    { step: 'Expert Chefs', icon: '👨‍🍳', description: 'Prepared by experienced culinary experts' },
    { step: 'Hygienic Kitchen', icon: '✨', description: 'Maintaining highest hygiene standards' },
    { step: 'Served Hot', icon: '🔥', description: 'Delivered fresh and hot to your table' }
  ];

  menuCategories = [
    { name: 'Burgers', icon: '🍔', count: '15+ varieties', color: '#FF6B6B' },
    { name: 'Momos', icon: '🥟', count: '8+ varieties', color: '#4ECDC4' },
    { name: 'Beverages', icon: '🥤', count: '20+ drinks', color: '#45B7D1' },
    { name: 'Pasta', icon: '🍝', count: '12+ varieties', color: '#FFA07A' },
    { name: 'Sandwiches', icon: '🥪', count: '10+ varieties', color: '#98D8C8' },
    { name: 'Tea & Coffee', icon: '☕', count: '15+ flavors', color: '#D4A574' }
  ];

  // Real categories from API
  categories: Category[] = [];
  subCategories: SubCategory[] = [];
  menuItems: MenuItem[] = [];
  private categoryIdToName = new Map<string, string>();
  private categoryNameToCanonicalId = new Map<string, string>();
  categoryIcons: { [key: string]: string } = {
    'Starters': '🥗',
    'Burgers': '🍔',
    'Grilled Sandwiches': '🥪',
    'Pasta': '🍝',
    'Momos': '🥟',
    'Maggi': '🍜',
    'MTC Classic': '🍳',
    'Sides & More': '🍟',
    'Beverages': '🥤',
    'Tea': '☕'
  };
  categoryColors: string[] = ['#FF6B6B', '#4ECDC4', '#45B7D1', '#FFA07A', '#98D8C8', '#D4A574', '#F7DC6F', '#BB8FCE', '#85C1E2', '#F8B195'];

  stats = [
    { value: '...', label: 'Online Orders Served', icon: '😊' },
    { value: '...', label: 'Menu Items', icon: '🍽️' },
    { value: '...', label: 'Average Rating', icon: '⭐' },
    { value: '...', label: 'Years Serving', icon: '🎉' }
  ];

  experienceVideos = [
    { title: 'Customer Experience', thumbnail: '🎬', description: 'Watch our happy customers' },
    { title: 'Kitchen Tour', thumbnail: '🏪', description: 'Behind the scenes' },
    { title: 'Food Preparation', thumbnail: '👨‍🍳', description: 'How we make magic' }
  ];

  currentTestimonialIndex = 0;
  private testimonialIntervalId: ReturnType<typeof setInterval> | null = null;
  checkoutDraft: { timestamp: string; cartItemCount: number; grandTotal: number } | null = null;
  lastDeliveredOrder: Order | null = null;
  loadingLastOrder = false;
  isReorderingLastOrder = false;
  private latestMenuMap = new Map<string, ServiceMenuItem>();

  private analyticsTracking = inject(AnalyticsTrackingService);

  constructor(
    private http: HttpClient,
    private outletService: OutletService,
    private homeContentConfigService: HomeContentConfigService,
    private loyaltyService: LoyaltyService,
    private offersService: OffersService,
    private authService: AuthService,
    private orderService: OrderService,
    private cartService: CartService,
    private menuService: MenuService,
    private uiStore: UIStore,
    private router: Router
  ) {}

  ngOnInit() {
    this.startTestimonialRotation();
    // Load categories first, which will trigger menu items
    this.loadCategories();
    // Load outlets for display (public endpoint, no auth needed)
    this.loadOutlets();
    // Load real stats from public API
    this.loadPublicStats();
    this.loadOffersHighlights();
    this.loadLoyaltyHighlights();
    this.loadCheckoutDraft();

    if (this.isLoggedIn) {
      this.loadLatestMenuSnapshot();
      this.loadLastDeliveredOrder();
    }
  }

  get isLoggedIn(): boolean {
    return this.authService.isLoggedIn();
  }

  get loyaltyPerks(): string[] {
    return this.loyaltyAccount?.tierBenefits || [];
  }

  get checkoutDraftAgeMinutes(): number {
    if (!this.checkoutDraft?.timestamp) return 0;
    const ts = new Date(this.checkoutDraft.timestamp).getTime();
    if (Number.isNaN(ts)) return 0;
    return Math.max(0, Math.floor((Date.now() - ts) / 60000));
  }

  resumeCheckoutFromDraft(): void {
    this.router.navigate(['/checkout']);
  }

  dismissCheckoutDraft(): void {
    this.checkoutDraft = null;
    localStorage.removeItem(this.checkoutDraftStorageKey);
  }

  reorderLastOrder(): void {
    if (!this.lastDeliveredOrder || this.isReorderingLastOrder) return;

    this.isReorderingLastOrder = true;

    let added = 0;
    let substituted = 0;
    let skipped = 0;

    for (const item of this.lastDeliveredOrder.items || []) {
      const resolved = this.resolveReorderItem(item);
      if (!resolved) {
        skipped += 1;
        continue;
      }

      if (resolved.substituted) {
        substituted += 1;
      }

      this.cartService.addItem({
        menuItemId: resolved.menuItem.id,
        name: resolved.menuItem.name,
        description: resolved.menuItem.description,
        categoryName: resolved.menuItem.categoryName || item.categoryName,
        price: this.getMenuPrice(resolved.menuItem, item.price),
        imageUrl: resolved.menuItem.imageUrl,
        packagingCharge: resolved.menuItem.packagingCharge || 0
      }, item.quantity || 1);

      added += 1;
    }

    this.isReorderingLastOrder = false;

    if (added === 0) {
      this.uiStore.warning('No available items found for your last order.');
      return;
    }

    const parts: string[] = [`${added} item(s) added`];
    if (substituted > 0) parts.push(`${substituted} substituted`);
    if (skipped > 0) parts.push(`${skipped} skipped`);
    this.uiStore.success(`Reorder ready: ${parts.join(', ')}.`);
    this.router.navigate(['/cart']);
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

  private loadLastDeliveredOrder(): void {
    this.loadingLastOrder = true;
    this.orderService.getMyOrders().subscribe({
      next: (orders) => {
        this.lastDeliveredOrder = [...(orders || [])]
          .filter(o => o.status === 'delivered' && (o.items?.length || 0) > 0)
          .sort((a, b) => new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime())[0] || null;
        this.loadingLastOrder = false;
      },
      error: () => {
        this.lastDeliveredOrder = null;
        this.loadingLastOrder = false;
      }
    });
  }

  private loadLatestMenuSnapshot(): void {
    this.menuService.getMenuItems().subscribe({
      next: (items) => {
        this.latestMenuMap = new Map((items || []).map(item => [item.id, item]));
      },
      error: () => {
        this.latestMenuMap = new Map();
      }
    });
  }

  private resolveReorderItem(orderItem: OrderItem): { menuItem: ServiceMenuItem; substituted: boolean } | null {
    const direct = this.latestMenuMap.get(orderItem.menuItemId);
    if (direct && direct.isAvailable !== false) {
      return { menuItem: direct, substituted: false };
    }

    const availableItems = [...this.latestMenuMap.values()].filter(m => m.isAvailable !== false);
    if (!availableItems.length) return null;

    const sameCategory = availableItems.find(m =>
      !!orderItem.categoryId && !!m.categoryId && m.categoryId === orderItem.categoryId
    );
    if (sameCategory) return { menuItem: sameCategory, substituted: true };

    const seed = (orderItem.name || '').trim().toLowerCase();
    const seedWord = seed.split(' ').find(Boolean) || '';
    if (seedWord) {
      const similar = availableItems.find(m => (m.name || '').toLowerCase().includes(seedWord));
      if (similar) return { menuItem: similar, substituted: true };
    }

    return { menuItem: availableItems[0], substituted: true };
  }

  private getMenuPrice(menuItem: ServiceMenuItem, fallback: number): number {
    return menuItem.webPrice || menuItem.shopSellingPrice || menuItem.onlinePrice || fallback || 0;
  }

  private loadLoyaltyHighlights() {
    if (!this.authService.isLoggedIn()) {
      this.loyaltyAccount = null;
      this.loyaltyRewards = [];
      return;
    }

    this.loyaltyLoading = true;

    this.loyaltyService.getLoyaltyAccount().subscribe({
      next: (account) => {
        this.loyaltyAccount = account;
        this.loyaltyLoading = false;
      },
      error: () => {
        this.loyaltyAccount = null;
        this.loyaltyLoading = false;
      }
    });

    this.loadLoyaltyRewards();
  }

  private loadLoyaltyRewards() {
    this.loyaltyService.getAvailableRewards().subscribe({
      next: (rewards) => {
        this.loyaltyRewards = [...(rewards || [])]
          .filter(r => r.isActive)
          .sort((a, b) => {
            if (a.canRedeem === b.canRedeem) return a.pointsCost - b.pointsCost;
            return a.canRedeem ? -1 : 1;
          })
          .slice(0, 6);
      },
      error: () => {
        this.loyaltyRewards = [];
      }
    });
  }

  private loadOffersHighlights() {
    this.offersLoading = true;
    this.offersService.getActiveOffers().subscribe({
      next: (offers) => {
        const now = new Date();
        this.activeOffers = [...(offers || [])]
          .filter(offer => {
            if (!offer.isActive) return false;
            if (!offer.validTill) return true;
            return new Date(offer.validTill as any) >= now;
          })
          .sort((a, b) => new Date(b.validTill as any).getTime() - new Date(a.validTill as any).getTime())
          .slice(0, 6);
        this.offersLoading = false;
      },
      error: () => {
        this.activeOffers = [];
        this.offersLoading = false;
      }
    });
  }

  getOfferDiscountLabel(offer: Offer): string {
    if (offer.discountType === 'percentage') return `${offer.discountValue}% OFF`;
    if (offer.discountType === 'flat') return `₹${offer.discountValue} OFF`;
    return 'BOGO';
  }

  formatOfferValidTill(value: any): string {
    if (!value) return 'Limited period';
    const date = new Date(value);
    if (Number.isNaN(date.getTime())) return 'Limited period';
    return `Valid till ${date.toLocaleDateString('en-IN', { day: 'numeric', month: 'short' })}`;
  }

  ngAfterViewInit() {
    loadLeaflet()
      .then(() => setTimeout(() => this.initMap(), 300))
      .catch(err => console.error('Failed to load Leaflet:', err));
  }

  ngOnDestroy() {
    if (this.testimonialIntervalId) {
      clearInterval(this.testimonialIntervalId);
    }
    if (this.map) {
      this.map.remove();
      this.map = undefined;
    }
  }

  private map: any;

  private initMap() {
    try {
      if (this.map) return;

      if (typeof L === 'undefined') {
        console.error('Leaflet: L is not defined. CDN script may not have loaded.');
        return;
      }

      const mapEl = this.mapElementRef?.nativeElement;
      if (!mapEl) {
        console.error('Leaflet: Map container element not found');
        return;
      }


      // Kanchrapara, West Bengal center (midpoint of both outlets)
      const center = [22.9424, 88.4489];

      // Fix Leaflet default marker icon path issue with bundlers
      const iconDefault = L.icon({
        iconUrl: 'https://unpkg.com/leaflet@1.9.4/dist/images/marker-icon.png',
        iconRetinaUrl: 'https://unpkg.com/leaflet@1.9.4/dist/images/marker-icon-2x.png',
        shadowUrl: 'https://unpkg.com/leaflet@1.9.4/dist/images/marker-shadow.png',
        iconSize: [25, 41],
        iconAnchor: [12, 41],
        popupAnchor: [1, -34],
        shadowSize: [41, 41]
      });

      this.map = L.map(mapEl, {
        center,
        zoom: 15,
        scrollWheelZoom: false
      });

      L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
        attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors',
        maxZoom: 19
      }).addTo(this.map);

      // Outlet 1 — 107 KGR Path, Kanchrapara
      L.marker([22.947899914909794, 88.44490344042521], { icon: iconDefault })
        .addTo(this.map)
        .bindPopup(`
          <strong>🏪 Maa Tara Cafe</strong><br>
          📍 107 KGR Path, Kanchrapara<br>
          🕐 9:00 AM – 10:30 PM<br>
          📞 <a href="tel:+918240443533">+91-8240443533</a>
        `);

      // Outlet 2 — Bongaon Road, Kanchrapara
      L.marker([22.936861432289902, 88.45279946741576], { icon: iconDefault })
        .addTo(this.map)
        .bindPopup(`
          <strong>🏪 Maa Tara Cafe</strong><br>
          📍 Bongaon Road, Kanchrapara<br>
          🕐 9:00 AM – 10:00 PM<br>
          📞 <a href="tel:+918240443533">+91-8240443533</a>
        `);

      // Force Leaflet to recalculate container size
      setTimeout(() => {
        if (this.map) {
          this.map.invalidateSize();
        }
      }, 500);

    } catch (error) {
      console.error('Leaflet: Error initializing map:', error);
    }
  }

  loadOutlets() {
    this.outletService.getPublicOutlets().subscribe({
      next: (data) => {
        this.outlets = data || [];
      },
      error: (error) => {
        console.error('Error loading outlets:', error);
      }
    });
  }

  loadCategories() {
    this.http.get<Category[]>(`${environment.apiUrl}/categories`).subscribe({
      next: (data) => {
        const normalizedCategories = (data || []).map((category: any) => ({
          ...category,
          id: this.normalizeId(category.id ?? category._id),
          name: (category.name || category.categoryName || 'Category').toString().trim(),
        }));

        this.categoryIdToName.clear();
        this.categoryNameToCanonicalId.clear();

        for (const category of normalizedCategories) {
          if (!category.id || !category.name) continue;

          this.categoryIdToName.set(category.id, category.name);
          const normalizedName = this.normalizeCategoryName(category.name);
          if (!this.categoryNameToCanonicalId.has(normalizedName)) {
            this.categoryNameToCanonicalId.set(normalizedName, category.id);
          }
        }

        const seenNames = new Set<string>();
        this.categories = normalizedCategories.filter((category) => {
          const normalizedName = this.normalizeCategoryName(category.name);
          if (seenNames.has(normalizedName)) return false;
          seenNames.add(normalizedName);
          return true;
        });

        // Load menu items after categories are loaded
        this.loadMenuItems();
      },
      error: (error) => {
        console.error('Error loading categories:', error);
        console.error('Error details:', error.message, error.status);
        // Continue to load menu items even if categories fail
        this.loadMenuItems();
      }
    });
  }

  loadMenuItems() {
    this.http.get<any>(`${environment.apiUrl}/menu`).subscribe({
      next: (data) => {
        const allItems = Array.isArray(data) ? data : (data?.data || []);
        // Deduplicate menu items by category + name across outlets.
        const seen = new Set<string>();
        this.menuItems = allItems.filter((item: any) => {
          const rawCategoryId = this.normalizeId(item.categoryId ?? item.CategoryId ?? item.category?.id ?? item.category?._id);
          const categoryName = (item.categoryName || item.category?.name || this.categoryIdToName.get(rawCategoryId) || '').toString().trim();
          const name = (item.name || item.catalogueName || '').toLowerCase();
          const dedupeKey = `${this.normalizeCategoryName(categoryName || rawCategoryId)}::${name}`;

          if (!name || seen.has(dedupeKey)) return false;
          seen.add(dedupeKey);
          return true;
        }).map((item: any) => ({
          ...item,
          id: this.normalizeId(item.id ?? item._id),
          categoryId: this.getCanonicalCategoryId(item),
          categoryName: (item.categoryName || item.category?.name || this.categoryIdToName.get(this.normalizeId(item.categoryId ?? item.CategoryId ?? item.category?.id ?? item.category?._id)) || '').toString().trim(),
          subCategoryId: this.normalizeId(item.subCategoryId ?? item.SubCategoryId ?? item.subCategory?.id ?? item.subCategory?._id),
        }));

        // Update stats after menu items are loaded
        this.loadStats();

        // Get latest 6 menu items with real data
        this.loadHomeContentConfig();
      },
      error: (error) => {
        console.error('Error loading menu items:', error);
        console.error('Error details:', error.message, error.status);
        // Still try to load stats
        this.loadStats();
        this.loadHomeContentConfig();
      }
    });
  }

  loadHomeContentConfig() {
    this.homeContentConfigService.getPublicConfig().subscribe({
      next: (res) => {
        const config = res?.data || null;
        this.homeContentConfig = config;
        this.announcementEnabled = !!config?.announcementEnabled;
        this.announcementTitle = config?.announcementTitle || '';
        this.announcementMessage = config?.announcementMessage || '';
        this.applyLatestMenuItems(config);
      },
      error: () => {
        this.applyLatestMenuItems(null);
      }
    });
  }

  private applyLatestMenuItems(config: HomeContentConfig | null) {
    if (!this.menuItems || this.menuItems.length === 0) {
      this.latestMenuItems = [];
      return;
    }

    const selectedIdOrder = (config?.featuredMenuItemIds || []).map(id => this.normalizeId(id));
    const menuItemById = new Map(this.menuItems.map(item => [this.normalizeId((item as any).id), item]));

    const selectedItems = selectedIdOrder.length > 0
      ? selectedIdOrder
          .map(id => menuItemById.get(id))
          .filter((item): item is MenuItem => !!item)
          .slice(0, 6)
      : this.menuItems.slice(0, 6);

    const tags = ['NEW', 'POPULAR', 'BESTSELLER', 'CHEF SPECIAL', 'TRENDING', 'HOT'];
    this.latestMenuItems = selectedItems.map((item: any, index: number) => {
      const categoryName = this.getCategoryNameById(item.categoryId);
      return {
        name: item.name || item.catalogueName || item.description || 'Menu Item',
        category: categoryName || 'Special',
        price: `₹${item.webPrice || item.shopSellingPrice || item.onlinePrice || 99}`,
        image: this.getCategoryIcon(categoryName),
        tag: tags[index % tags.length]
      };
    });
  }

  loadPublicStats() {
    this.http.get<any>(`${environment.apiUrl}/public/stats`).subscribe({
      next: (res) => {
        if (res?.success && res.data) {
          const d = res.data;
          // Animate the stat values
          this.animateStatValue(0, d.totalOrders, 'Online Orders Served', '😊', '+');
          this.animateStatValue(1, d.menuItemCount, 'Menu Items', '🍽️', '+');
          this.animateStatValue(2, d.averageRating, 'Average Rating', '⭐', '⭐', true);
          this.animateStatValue(3, d.yearsServing, 'Years Serving', '🎉', '+');
        }
      },
      error: (err) => console.error('Error loading public stats:', err)
    });
  }

  animateStatValue(index: number, targetValue: number, label: string, icon: string, suffix: string = '', isRating: boolean = false) {
    const duration = 2000; // 2 seconds
    const steps = 60;
    const increment = targetValue / steps;
    let currentValue = 0;
    let step = 0;

    const timer = setInterval(() => {
      step++;
      currentValue += increment;

      if (step >= steps) {
        currentValue = targetValue;
        clearInterval(timer);
      }

      if (isRating) {
        this.stats[index] = {
          value: currentValue > 0 ? `${Math.round(currentValue)}${suffix}` : '...',
          label,
          icon
        };
      } else {
        this.stats[index] = {
          value: currentValue > 0 ? `${Math.floor(currentValue)}${suffix}` : '...',
          label,
          icon
        };
      }
    }, duration / steps);
  }

  loadStats() {
    // Kept as fallback for menu item count update
    // Real stats are now loaded from the public API
  }

  getCategoryIcon(categoryName: string): string {
    return this.categoryIcons[categoryName] || '🍽️';
  }

  getCategoryColor(index: number): string {
    return this.categoryColors[index % this.categoryColors.length];
  }

  getMenuItemCount(categoryId: string): number {
    const normalizedCategoryId = this.normalizeId(categoryId);
    if (!normalizedCategoryId) return 0;

    const categoryName = this.getCategoryNameById(normalizedCategoryId);
    const normalizedCategoryName = this.normalizeCategoryName(categoryName);

    return this.menuItems.filter(item =>
      this.normalizeId((item as any).categoryId) === normalizedCategoryId ||
      this.normalizeCategoryName((item as any).categoryName) === normalizedCategoryName
    ).length;
  }

  getGalleryItems() {
    // Get a sample of menu items for the gallery (5 items for 1 row)
    return this.menuItems.slice(0, 5);
  }

  getCategoryNameById(categoryId: string): string {
    const normalizedCategoryId = this.normalizeId(categoryId);
    if (this.categoryIdToName.has(normalizedCategoryId)) {
      return this.categoryIdToName.get(normalizedCategoryId) || '';
    }

    const category = this.categories.find(c => this.normalizeId((c as any).id) === normalizedCategoryId);
    return category ? category.name : '';
  }

  private getCanonicalCategoryId(item: any): string {
    const rawCategoryId = this.normalizeId(item.categoryId ?? item.CategoryId ?? item.category?.id ?? item.category?._id);
    const rawCategoryName = (item.categoryName || item.category?.name || this.categoryIdToName.get(rawCategoryId) || '').toString().trim();

    if (rawCategoryName) {
      const normalizedName = this.normalizeCategoryName(rawCategoryName);
      if (this.categoryNameToCanonicalId.has(normalizedName)) {
        return this.categoryNameToCanonicalId.get(normalizedName) || rawCategoryId;
      }
    }

    return rawCategoryId;
  }

  private normalizeCategoryName(name: string): string {
    return (name || '').trim().toLowerCase();
  }

  private normalizeId(value: any): string {
    if (!value) return '';
    if (typeof value === 'string') return value;

    if (typeof value === 'object') {
      if (typeof value.$oid === 'string') return value.$oid;
      if (typeof value.id === 'string') return value.id;
      if (typeof value._id === 'string') return value._id;
      if (value._id && typeof value._id === 'object' && typeof value._id.$oid === 'string') {
        return value._id.$oid;
      }
    }

    return String(value);
  }

  startTestimonialRotation() {
    this.testimonialIntervalId = setInterval(() => {
      this.currentTestimonialIndex = (this.currentTestimonialIndex + 1) % this.testimonials.length;
    }, 5000);
  }

  getStars(rating: number): string[] {
    return Array(rating).fill('⭐');
  }

  scrollToSection(sectionId: string) {
    this.analyticsTracking.trackFeatureUsage('Landing Section Scroll', sectionId);
    const element = document.getElementById(sectionId);
    element?.scrollIntoView({ behavior: 'smooth' });
  }

  trackCategoryClick(categoryName: string): void {
    this.analyticsTracking.trackFeatureUsage('Menu Category Browse', categoryName);
  }

  trackOutletPhoneClick(outletName: string): void {
    this.analyticsTracking.trackFeatureUsage('Outlet Phone Click', outletName);
  }

  trackMenuItemClick(itemName: string): void {
    this.analyticsTracking.trackFeatureUsage('Menu Item View', itemName);
  }

  formatTime(time: string): string {
    // Convert 24-hour format to 12-hour format with AM/PM
    if (!time) return '';
    const [hours, minutes] = time.split(':').map(Number);
    const period = hours >= 12 ? 'PM' : 'AM';
    const displayHours = hours % 12 || 12;
    return `${displayHours}:${minutes.toString().padStart(2, '0')} ${period}`;
  }

  getOutletLocation(outlet: Outlet): string {
    const parts = [outlet.address, outlet.city, outlet.state].filter(p => p);
    return parts.join(', ');
  }

  trackByIndex = (index: number): number => index;

  trackById = (index: number, item: any): string => item._id;

  trackByObjId = (index: number, item: any): string => this.normalizeId(item.id ?? item._id) || String(index);

  trackByName = (index: number, item: any): string => item.name;
}
