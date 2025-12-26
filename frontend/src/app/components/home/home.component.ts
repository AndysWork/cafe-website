import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../../environments/environment';
import { CustomerReviewsComponent } from '../customer-reviews/customer-reviews.component';

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
    { value: '...', label: 'Happy Customers', icon: '😊' },
    { value: '...', label: 'Menu Items', icon: '🍽️' },
    { value: '...', label: 'Average Rating', icon: '⭐' },
    { value: '3+', label: 'Years Serving', icon: '🎉' }
  ];

  experienceVideos = [
    { title: 'Customer Experience', thumbnail: '🎬', description: 'Watch our happy customers' },
    { title: 'Kitchen Tour', thumbnail: '🏪', description: 'Behind the scenes' },
    { title: 'Food Preparation', thumbnail: '👨‍🍳', description: 'How we make magic' }
  ];

  currentTestimonialIndex = 0;

  constructor(private http: HttpClient) {}

  ngOnInit() {
    this.startTestimonialRotation();
    // Load categories first, which will trigger menu items, then stats
    this.loadCategories();
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
        this.menuItems = data || [];
        console.log('Menu items loaded for home page:', this.menuItems.length, 'items');

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

  loadStats() {
    // For public landing page, show impressive static stats
    // Admin analytics has the real-time data with authentication
    console.log('Setting stats with menu count:', this.menuItems.length);

    this.stats = [
      { value: '1000+', label: 'Happy Customers', icon: '😊' },
      { value: `${this.menuItems.length || 100}+`, label: 'Menu Items', icon: '🍽️' },
      { value: '4.8⭐', label: 'Average Rating', icon: '⭐' },
      { value: '3+', label: 'Years Serving', icon: '🎉' }
    ];
    console.log('Stats updated:', this.stats);
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
}
