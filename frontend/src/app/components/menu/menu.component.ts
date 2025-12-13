import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MenuService, MenuItem, MenuCategory } from '../../services/menu.service';
import { CartService } from '../../services/cart.service';
import { Router } from '@angular/router';

@Component({
  selector: 'app-menu',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './menu.component.html',
  styleUrls: ['./menu.component.scss']
})
export class MenuComponent implements OnInit {
  categories: MenuCategory[] = [];
  menuItems: MenuItem[] = [];
  filteredItems: MenuItem[] = [];
  selectedCategoryId: string | null = null;
  isLoading = false;
  errorMessage = '';
  addedToCartMessage: string | null = null;

  constructor(
    private menuService: MenuService,
    private cartService: CartService,
    private router: Router
  ) {}

  ngOnInit() {
    this.loadMenu();
  }

  loadMenu() {
    this.isLoading = true;
    this.errorMessage = '';

    // Load categories and menu items in parallel
    Promise.all([
      this.menuService.getCategories().toPromise(),
      this.menuService.getMenuItems().toPromise()
    ])
    .then(([categories, items]) => {
      this.categories = categories || [];
      this.menuItems = items || [];
      this.filteredItems = this.menuItems;
      this.isLoading = false;
    })
    .catch(error => {
      console.error('Error loading menu:', error);
      this.errorMessage = 'Failed to load menu. Please try again.';
      this.isLoading = false;
    });
  }

  filterByCategory(categoryId: string | null) {
    this.selectedCategoryId = categoryId;
    if (categoryId) {
      this.filteredItems = this.menuItems.filter(item => item.categoryId === categoryId);
    } else {
      this.filteredItems = this.menuItems;
    }
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

    // Show success message
    this.addedToCartMessage = `${item.name} added to cart!`;
    setTimeout(() => {
      this.addedToCartMessage = null;
    }, 2000);
  }

  goToCart() {
    this.router.navigate(['/cart']);
  }

  getCategoryName(categoryId: string): string {
    const category = this.categories.find(c => c.id === categoryId);
    return category?.name || 'Other';
  }
}
