import { Component, OnInit, AfterViewInit, OnDestroy, ViewChild, ElementRef, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, RouterModule } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../../environments/environment';
import { CustomerReviewsComponent } from '../customer-reviews/customer-reviews.component';
import { TableReservationComponent } from '../table-reservation/table-reservation.component';
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
import { getIstIsoString } from '../../utils/date-utils';
import { HomePanel, HomePanelService } from '../../services/home-panel.service';
import { Subscription } from 'rxjs';

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
  name?: string;
  description?: string;
  imageUrl?: string;
  imageThumbnailUrl?: string;
  webPrice?: number;
  shopSellingPrice?: number;
  onlinePrice?: number;
  isAvailable?: boolean;
  isVisibleToCustomers?: boolean;
  isAddOnOnly?: boolean;
}

export interface LandingShowcaseItem {
  name: string;
  imageUrl: string;
  price: number | null;
  icon: string;
}

export function selectShowcaseItem(items: MenuItem[], search: string, icon: string): LandingShowcaseItem | null {
  const matches = items.filter(item =>
    item.isAvailable !== false && item.isVisibleToCustomers !== false && item.isAddOnOnly !== true &&
    (item.name || item.catalogueName || '').toLowerCase().includes(search.toLowerCase()));
  const item = matches.find(candidate => candidate.imageUrl || candidate.imageThumbnailUrl) || matches[0];
  if (!item) return null;
  const price = item.webPrice ?? item.shopSellingPrice ?? item.onlinePrice;
  return {
    name: item.name || item.catalogueName,
    imageUrl: item.imageThumbnailUrl || item.imageUrl || '',
    price: typeof price === 'number' && Number.isFinite(price) ? price : null,
    icon
  };
}

export function selectLandingSections(items: MenuItem[], configuredItems: MenuItem[], hasConfiguredSelection: boolean) {
  const isVisible = (item: MenuItem) =>
    item.isAvailable !== false && item.isVisibleToCustomers !== false && item.isAddOnOnly !== true;
  items = items.filter(isVisible);
  configuredItems = configuredItems.filter(isVisible);
  const nameOf = (item: MenuItem) => (item.name || item.catalogueName || '').toLowerCase();
  const configuredNames = new Set(configuredItems.map(nameOf));
  const candidates = items.filter(item => !configuredNames.has(nameOf(item)));
  const terms = [
    { search: 'breakfast', icon: '🍳' },
    { search: 'momo', icon: '🥟' },
    { search: 'burger', icon: '🍔' },
    { search: 'pasta', icon: '🍝' },
    { search: 'pizza', icon: '🍕' },
    { search: 'waffle', icon: '🧇' },
    { search: 'sandwich', icon: '🥪' },
    { search: 'coffee', icon: '☕' }
  ];
  const specialNames = new Set<string>();
  const specials = terms
    .map(({ search, icon }) => selectShowcaseItem(candidates, search, icon))
    .filter((item): item is LandingShowcaseItem => {
      if (!item || specialNames.has(item.name.toLowerCase())) return false;
      specialNames.add(item.name.toLowerCase());
      return true;
    });
  const imageItems = items.filter(item => item.imageUrl || item.imageThumbnailUrl);
  const seenCategories = new Set<string>();
  const variedItems = imageItems.filter(item => {
    const category = item.categoryId || item.categoryName || '';
    if (!category || seenCategories.has(category)) return false;
    seenCategories.add(category);
    return true;
  });
  const discoveryNames = new Set<string>();
  const discovery = (hasConfiguredSelection ? configuredItems : [...variedItems, ...imageItems])
    .filter(item => {
      const name = nameOf(item);
      if (!name || specialNames.has(name) || discoveryNames.has(name)) return false;
      discoveryNames.add(name);
      return true;
    }).slice(0, 6);
  return { specials, discovery };
}

interface LoyaltyLevelPerk {
  tier: string;
  icon: string;
  pointsBand: string;
  perks: string[];
}

interface PublicStatsResponse {
  success: boolean;
  data?: {
    totalOrders: number;
    menuItemCount: number;
    fiveStarReviewCount: number;
    yearsServing: number;
  };
}

@Component({
  selector: 'app-home',
  standalone: true,
  imports: [CommonModule, RouterModule, CustomerReviewsComponent, TableReservationComponent],
  templateUrl: './home.component.html',
  styleUrls: ['./home.component.scss']
})
export class HomeComponent implements OnInit, AfterViewInit, OnDestroy {
  private readonly checkoutDraftStorageKey = 'checkout_draft';
  private readonly primaryOutletCode = 'MTC007';
  private readonly homePanelService = inject(HomePanelService);
  private homePanelSubscription?: Subscription;
  private storyStatsSubscription?: Subscription;
  @ViewChild('specialsCarousel') specialsCarousel?: ElementRef<HTMLElement>;
  @ViewChild('categoryCarousel') categoryCarousel?: ElementRef<HTMLElement>;
  @ViewChild('visitCarousel') visitCarousel?: ElementRef<HTMLElement>;
  @ViewChild('homeDialog', { static: true }) homeDialog!: ElementRef<HTMLDialogElement>;
  activePanel: HomePanel | null = null;
  readonly panelTitles: Record<HomePanel, string> = {
    story: 'Our Story', offers: 'Current Offers', rewards: 'Loyalty Rewards', contact: 'Contact Us', reserve: 'Reserve a Table'
  };
  readonly zomatoRating = 4.2;
  storyStats: PublicStatsResponse['data'] | null = null;
  storyStatsLoading = true;
  storyStatsError = '';
  offersError = '';
  loyaltyError = '';
  private previousBodyOverflow = '';
  private specialsIntervalId?: ReturnType<typeof setInterval>;
  private visitIntervalId?: ReturnType<typeof setInterval>;
  readonly visitSlides = [
    { imageUrl: '/images/cafe/cafe-01.jpg', alt: 'Maa Tara Cafe outdoor seating under illuminated canopy' },
    { imageUrl: '/images/cafe/cafe-02.jpg', alt: 'Cafe seating and food cart beneath string lights' },
    { imageUrl: '/images/cafe/cafe-03.jpg', alt: 'Maa Tara Cafe entrance and heart-shaped photo spot' },
    { imageUrl: '/images/cafe/cafe-04.jpg', alt: 'Illuminated heart-shaped photo chair at Maa Tara Cafe' },
    { imageUrl: '/images/cafe/cafe-05.jpg', alt: 'Maa Tara Cafe blue food cart at night' }
  ];
  latestMenuItems: LandingShowcaseItem[] = [];
  bowlItem: LandingShowcaseItem | null = null;
  showcaseItems: LandingShowcaseItem[] = [];
  readonly quickCategories = [
    { name: 'Pizza', icon: '🍕', search: 'pizza' },
    { name: 'Burgers', icon: '🍔', search: 'burger' },
    { name: 'Momos', icon: '🥟', search: 'momo' },
    { name: 'Pasta', icon: '🍝', search: 'pasta' },
    { name: 'Protein Bowls', icon: '🥗', search: 'bowl' },
    { name: 'Tandoor', icon: '🍢', search: 'tandoor' },
    { name: 'Indian & Rice', icon: '🍚', search: 'rice' },
    { name: 'Breakfast', icon: '🍳', search: 'breakfast' },
    { name: 'Tea', icon: '🍵', search: 'tea' },
    { name: 'Coffee', icon: '☕', search: 'coffee' },
    { name: 'Mocktails', icon: '🍹', search: 'mocktail' },
    { name: 'Waffles', icon: '🧇', search: 'waffle' }
  ];
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
    this.homePanelSubscription = this.homePanelService.requests.subscribe(panel => this.openHomePanel(panel));
    // Load outlets first so menu/category requests are scoped to one outlet.
    this.loadOutlets();
    this.loadOffersHighlights();
    this.loadLoyaltyHighlights();
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
        imageThumbnailUrl: resolved.menuItem.imageThumbnailUrl,
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
          timestamp: parsed.timestamp || getIstIsoString(),
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
    this.loyaltyError = '';
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
      error: (error) => {
        console.error('Error loading loyalty account:', error);
        this.loyaltyError = 'Could not load your loyalty account. Please try again from the rewards page.';
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
          });
      },
      error: (error) => {
        console.error('Error loading loyalty rewards:', error);
        this.loyaltyError = 'Could not load available rewards. Please try again from the rewards page.';
        this.loyaltyRewards = [];
      }
    });
  }

  private loadOffersHighlights() {
    this.offersLoading = true;
    this.offersError = '';
    this.offersService.getActiveOffers().subscribe({
      next: (offers) => {
        const now = new Date();
        this.activeOffers = [...(offers || [])]
          .filter(offer => {
            if (!offer.isActive) return false;
            if (!offer.validTill) return true;
            return new Date(offer.validTill) >= now;
          })
          .sort((a, b) => new Date(b.validTill).getTime() - new Date(a.validTill).getTime());
        this.offersLoading = false;
      },
      error: (error) => {
        console.error('Error loading active offers:', error);
        this.offersError = 'Could not load current offers. Please try again later.';
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
    return `Valid till ${date.toLocaleDateString('en-IN', { day: 'numeric', month: 'short', timeZone: 'Asia/Kolkata' })}`;
  }

  ngAfterViewInit() {
    this.startSpecialsCarousel();
    this.startVisitCarousel();
  }

  ngOnDestroy() {
    this.homePanelSubscription?.unsubscribe();
    this.storyStatsSubscription?.unsubscribe();
    this.closeHomePanel();
    if (this.specialsIntervalId) {
      clearInterval(this.specialsIntervalId);
    }
    if (this.visitIntervalId) {
      clearInterval(this.visitIntervalId);
    }
    if (this.testimonialIntervalId) {
      clearInterval(this.testimonialIntervalId);
    }
  }

  loadOutlets() {
    this.outletService.getPublicOutlets().subscribe({
      next: (data) => {
        this.outlets = (data || []).filter(outlet =>
          (outlet.outletCode || '').trim().toUpperCase() === this.primaryOutletCode
        );
        this.ensureCustomerOutletSelected(this.outlets);
        this.loadCategories();
      },
      error: (error) => {
        console.error('Error loading outlets:', error);
        // Fall back to existing behavior if outlet list is unavailable.
        this.loadCategories();
      }
    });
  }

  private ensureCustomerOutletSelected(outlets: Outlet[]): void {
    if (!outlets || outlets.length === 0) {
      return;
    }

    const selectedOutletId = this.outletService.getSelectedOutletId();
    if (selectedOutletId && outlets.some(outlet => this.normalizeId(outlet._id ?? outlet.id) === selectedOutletId)) {
      return;
    }

    const preferredOutlet = outlets.find(outlet => outlet.isActive) || outlets[0];
    if (preferredOutlet) {
      this.outletService.selectOutlet(preferredOutlet);
    }
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

        this.bowlItem = selectShowcaseItem(this.menuItems, 'bowl', '🥗');
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
      this.showcaseItems = [];
      return;
    }

    const selectedIdOrder = (config?.featuredMenuItemIds || []).map(id => this.normalizeId(id));
    const availableItems = this.menuItems.filter(item =>
      item.isAvailable !== false && item.isVisibleToCustomers !== false && item.isAddOnOnly !== true);
    const availableById = new Map(availableItems.map(item => [this.normalizeId(item.id), item]));
    const configuredItems = selectedIdOrder.length > 0
      ? selectedIdOrder
          .map(id => availableById.get(id))
          .filter((item): item is MenuItem => !!item)
      : [];
    const sections = selectLandingSections(availableItems, configuredItems, selectedIdOrder.length > 0);
    this.showcaseItems = sections.specials;
    this.latestMenuItems = sections.discovery.map((item) => {
      const categoryName = this.getCategoryNameById(item.categoryId);
      const price = item.webPrice ?? item.shopSellingPrice ?? item.onlinePrice;
      return {
        name: item.name || item.catalogueName || item.description || 'Menu Item',
        price: typeof price === 'number' && Number.isFinite(price) ? price : null,
        imageUrl: item.imageThumbnailUrl || item.imageUrl || '',
        icon: this.getCategoryIcon(categoryName)
      };
    });
  }

  startSpecialsCarousel(): void {
    this.specialsIntervalId = setInterval(() => {
      this.advanceSpecials();
      this.advanceCategories();
    }, 3000);
  }

  advanceSpecials(): void {
    this.advanceCarousel(this.specialsCarousel?.nativeElement, '.special-tile', 12);
  }

  advanceCategories(): void {
    this.advanceCarousel(this.categoryCarousel?.nativeElement, 'button', 0, true);
  }

  startVisitCarousel(): void {
    this.visitIntervalId = setInterval(() => this.advanceVisitArtwork(), 5000);
  }

  advanceVisitArtwork(): void {
    this.advanceCarousel(this.visitCarousel?.nativeElement, '.visit-art-slide', 0);
  }

  private advanceCarousel(carousel: HTMLElement | undefined, selector: string, gap: number, loopCategories = false): void {
    if (!carousel || this.activePanel || document.hidden || carousel.matches(':hover') ||
        carousel.contains(document.activeElement) ||
        window.matchMedia('(prefers-reduced-motion: reduce)').matches) return;
    const card = carousel.querySelector<HTMLElement>(selector);
    const maxScroll = carousel.scrollWidth - carousel.clientWidth;
    if (!card || maxScroll <= 1) return;
    if (loopCategories && window.matchMedia('(max-width: 600px)').matches) {
      const repeat = carousel.querySelector<HTMLElement>('.category-repeat button');
      const loopWidth = repeat ? repeat.offsetLeft - card.offsetLeft : 0;
      if (loopWidth > 0) {
        if (carousel.scrollLeft >= loopWidth) {
          carousel.scrollTo({ left: carousel.scrollLeft % loopWidth, behavior: 'instant' });
        }
        carousel.scrollBy({ left: card.offsetWidth, behavior: 'auto' });
        return;
      }
    }
    if (carousel.scrollLeft >= maxScroll - 1) {
      carousel.scrollTo({ left: 0, behavior: 'instant' });
    } else {
      carousel.scrollBy({ left: card.offsetWidth + gap, behavior: 'auto' });
    }
  }

  openHomePanel(panel: HomePanel): void {
    if (!this.activePanel) {
      this.previousBodyOverflow = document.body.style.overflow;
      document.body.style.overflow = 'hidden';
    }
    this.activePanel = panel;
    if (panel === 'story') this.loadPublicStats();
    if (!this.homeDialog.nativeElement.open) this.homeDialog.nativeElement.showModal();
  }

  closeHomePanel(): void {
    if (!this.activePanel) return;
    this.homeDialog.nativeElement.close();
    this.activePanel = null;
    document.body.style.overflow = this.previousBodyOverflow;
  }

  onPanelBackdropClick(event: MouseEvent): void {
    if (event.target === event.currentTarget) this.closeHomePanel();
  }

  browseMenu(search = ''): void {
    this.analyticsTracking.trackFeatureUsage('Menu Browse', search || 'all');
    this.router.navigate(['/menu'], { queryParams: search ? { search } : {} });
  }

  loadPublicStats() {
    this.storyStatsSubscription?.unsubscribe();
    this.storyStats = null;
    this.storyStatsLoading = true;
    this.storyStatsError = '';
    this.storyStatsSubscription = this.http.get<PublicStatsResponse>(`${environment.apiUrl}/public/stats`, {
      headers: { 'ngsw-bypass': 'true', 'Cache-Control': 'no-cache' }
    }).subscribe({
      next: (res) => {
        this.storyStatsLoading = false;
        if (res?.success && res.data) {
          const d = res.data;
          this.storyStats = d;
        } else {
          this.storyStatsError = 'Cafe statistics are unavailable. Please try again.';
          console.error('Public stats response did not contain statistics.');
        }
      },
      error: (err) => {
        console.error('Error loading public stats:', err);
        this.storyStatsLoading = false;
        this.storyStatsError = 'Could not load cafe statistics. Please try again.';
      }
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
          value: currentValue > 0 ? `${currentValue.toFixed(1)}${suffix}` : '...',
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
