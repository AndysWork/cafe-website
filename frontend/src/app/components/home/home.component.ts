import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../../environments/environment';
import { CustomerReviewsComponent } from '../customer-reviews/customer-reviews.component';
import { OutletService } from '../../services/outlet.service';
import { AuthService } from '../../services/auth.service';
import { Outlet } from '../../models/outlet.model';

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
  catalogueName: string;
}

@Component({
  selector: 'app-home',
  standalone: true,
  imports: [CommonModule, RouterModule, CustomerReviewsComponent],
  templateUrl: './home.component.html',
  styleUrls: ['./home.component.scss']
})
export class HomeComponent implements OnInit {
  latestMenuItems: any[] = [];
  outlets: Outlet[] = [];
  currentYear = new Date().getFullYear();

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

  constructor(
    private http: HttpClient,
    private outletService: OutletService,
    private authService: AuthService
  ) {}

  ngOnInit() {
    this.startTestimonialRotation();
    // Load categories first, which will trigger menu items
    this.loadCategories();
    // Load outlets for display (only if logged in - endpoint requires auth)
    if (this.authService.isLoggedIn()) {
      this.loadOutlets();
    }
    // Load real stats from public API
    this.loadPublicStats();
  }

  loadOutlets() {
    console.log('Loading outlets from API');
    this.outletService.getActiveOutlets().subscribe({
      next: (data) => {
        console.log('Outlets loaded:', data);
        this.outlets = data || [];
      },
      error: (error) => {
        console.error('Error loading outlets:', error);
      }
    });
  }

  loadCategories() {
    console.log('Loading categories from:', `${environment.apiUrl}/categories`);
    this.http.get<Category[]>(`${environment.apiUrl}/categories`).subscribe({
      next: (data) => {
        console.log('Categories API response:', data);
        this.categories = data || [];
        console.log('Categories loaded for home page:', this.categories.length, 'categories');
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
    console.log('Loading menu items from:', `${environment.apiUrl}/menu`);
    this.http.get<any>(`${environment.apiUrl}/menu`).subscribe({
      next: (data) => {
        console.log('Menu items API response:', data);
        const allItems = data || [];
        // Deduplicate menu items by name across outlets
        const seen = new Set<string>();
        this.menuItems = allItems.filter((item: any) => {
          const name = (item.name || item.catalogueName || '').toLowerCase();
          if (!name || seen.has(name)) return false;
          seen.add(name);
          return true;
        });
        console.log('Unique menu items for home page:', this.menuItems.length, 'of', allItems.length, 'total');

        // Update stats after menu items are loaded
        this.loadStats();

        // Get latest 6 menu items with real data
        if (this.menuItems && this.menuItems.length > 0) {
          const latestItems = this.menuItems.slice(0, 6);
          console.log('Processing latest items:', latestItems);
          this.latestMenuItems = latestItems.map((item: any, index: number) => {
            const categoryName = this.getCategoryNameById(item.categoryId);
            const tags = ['NEW', 'POPULAR', 'BESTSELLER', 'CHEF SPECIAL', 'TRENDING', 'HOT'];
            const processedItem = {
              name: item.name || item.catalogueName || item.description || 'Menu Item',
              category: categoryName || 'Special',
              price: `₹${item.onlinePrice || item.shopSellingPrice || 99}`,
              image: this.getCategoryIcon(categoryName),
              tag: tags[index % tags.length]
            };
            console.log('Processed item:', processedItem);
            return processedItem;
          });
          console.log('Latest menu items ready:', this.latestMenuItems);
        } else {
          console.warn('No menu items available');
        }
      },
      error: (error) => {
        console.error('Error loading menu items:', error);
        console.error('Error details:', error.message, error.status);
        // Still try to load stats
        this.loadStats();
      }
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
          this.animateStatValue(2, d.averageRating, 'Average Rating', '⭐', '⭐', false);
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
    return this.menuItems.filter(item => item.categoryId === categoryId).length;
  }

  getGalleryItems() {
    // Get a sample of menu items for the gallery (5 items for 1 row)
    return this.menuItems.slice(0, 5);
  }

  getCategoryNameById(categoryId: string): string {
    const category = this.categories.find(c => c.id === categoryId);
    return category ? category.name : '';
  }

  startTestimonialRotation() {
    setInterval(() => {
      this.currentTestimonialIndex = (this.currentTestimonialIndex + 1) % this.testimonials.length;
    }, 5000);
  }

  getStars(rating: number): string[] {
    return Array(rating).fill('⭐');
  }

  scrollToSection(sectionId: string) {
    const element = document.getElementById(sectionId);
    element?.scrollIntoView({ behavior: 'smooth' });
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
}
