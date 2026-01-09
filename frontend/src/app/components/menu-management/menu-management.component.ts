import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { Subscription } from 'rxjs';
import { MenuService } from '../../services/menu.service';
import { environment } from '../../../environments/environment';
import { getIstNow } from '../../utils/date-utils';

interface MenuItemVariant {
  variantName: string;
  price: number;
  quantity?: number;
}

interface MenuItem {
  id: string;
  name: string;
  description: string;
  category: string;
  categoryId: string;
  subCategoryId?: string;
  quantity: number;
  makingPrice: number;
  packagingCharge: number;
  shopSellingPrice: number;
  onlinePrice: number;
  futureShopPrice?: number;
  futureOnlinePrice?: number;
  variants: MenuItemVariant[];
  createdBy: string;
  createdDate: string;
  lastUpdatedBy: string;
  lastUpdated: string;
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

@Component({
  selector: 'app-menu-management',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './menu-management.component.html',
  styleUrl: './menu-management.component.scss'
})
export class MenuManagementComponent implements OnInit, OnDestroy {
  menuItems: MenuItem[] = [];
  categories: Category[] = [];
  subCategories: SubCategory[] = [];
  filteredSubCategories: SubCategory[] = [];

  private menuRefreshSubscription?: Subscription;

  loading = false;
  showModal = false;
  showUploadModal = false;
  isEditMode = false;
  selectedItem: MenuItem | null = null;

  searchTerm = '';
  filterCategory = '';

  // Upload properties
  selectedFile: File | null = null;
  uploading = false;
  uploadResult: any = null;
  uploadError: string | null = null;
  dragOver = false;
  clearExisting = false;

  formData: Partial<MenuItem> = {
    name: '',
    description: '',
    category: '',
    categoryId: '',
    subCategoryId: undefined,
    quantity: 0,
    makingPrice: 0,
    packagingCharge: 0,
    shopSellingPrice: 0,
    onlinePrice: 0,
    futureShopPrice: undefined,
    futureOnlinePrice: undefined,
    variants: []
  };

  newVariant: MenuItemVariant = {
    variantName: '',
    price: 0,
    quantity: undefined
  };

  constructor(
    private http: HttpClient,
    private menuService: MenuService
  ) {}

  ngOnInit(): void {
    this.loadMenuItems();
    this.loadCategories();
    this.loadSubCategories();

    // Subscribe to menu refresh notifications
    this.menuRefreshSubscription = this.menuService.menuItemsRefresh$.subscribe((refresh) => {
      if (refresh) {
        console.log('Menu items updated, refreshing list...');
        this.loadMenuItems();
      }
    });
  }

  ngOnDestroy(): void {
    this.menuRefreshSubscription?.unsubscribe();
  }

  refreshMenuItems(): void {
    console.log('Refreshing menu items...');
    this.loadMenuItems();
  }

  loadMenuItems(): void {
    this.loading = true;
    console.log('Loading menu items from:', `${environment.apiUrl}/menu`);
    this.http.get<MenuItem[]>(`${environment.apiUrl}/menu`)
      .subscribe({
        next: (data) => {
          console.log('Menu items loaded successfully:', data.length, 'items');
          console.log('First item sample:', data[0]);
          console.log('Future prices in first item:', {
            futureShopPrice: data[0]?.futureShopPrice,
            futureOnlinePrice: data[0]?.futureOnlinePrice
          });
          this.menuItems = data;
          this.loading = false;
        },
        error: (error) => {
          console.error('Error loading menu items:', error);
          alert('Failed to load menu items. Please check the console for details.');
          this.loading = false;
        }
      });
  }

  loadCategories(): void {
    this.http.get<Category[]>(`${environment.apiUrl}/categories`)
      .subscribe({
        next: (data) => {
          this.categories = data;
        },
        error: (error) => {
          console.error('Error loading categories:', error);
        }
      });
  }

  loadSubCategories(): void {
    this.http.get<SubCategory[]>(`${environment.apiUrl}/subcategories`)
      .subscribe({
        next: (data) => {
          this.subCategories = data;
        },
        error: (error) => {
          console.error('Error loading subcategories:', error);
        }
      });
  }

  onCategoryChange(categoryId: string, preserveSubCategory: boolean = false): void {
    this.filteredSubCategories = this.subCategories.filter(
      sc => sc.categoryId === categoryId
    );

    // Only reset subCategoryId if we're not preserving it (i.e., during actual category change, not during edit load)
    if (!preserveSubCategory) {
      this.formData.subCategoryId = undefined;
    }

    const category = this.categories.find(c => c.id === categoryId);
    if (category) {
      this.formData.category = category.name;
    }
  }

  get filteredMenuItems(): MenuItem[] {
    return this.menuItems.filter(item => {
      const matchesSearch = !this.searchTerm ||
        item.name?.toLowerCase().includes(this.searchTerm.toLowerCase()) ||
        item.description?.toLowerCase().includes(this.searchTerm.toLowerCase());

      const matchesCategory = !this.filterCategory ||
        item.categoryId === this.filterCategory;

      return matchesSearch && matchesCategory;
    });
  }

  openCreateModal(): void {
    this.isEditMode = false;
    this.formData = {
      name: '',
      description: '',
      category: '',
      categoryId: '',
      subCategoryId: undefined,
      quantity: 0,
      makingPrice: 0,
      packagingCharge: 0,
      shopSellingPrice: 0,
      onlinePrice: 0,
      variants: []
    };
    this.filteredSubCategories = [];
    this.showModal = true;
  }

  openEditModal(item: MenuItem): void {
    this.isEditMode = true;
    this.selectedItem = item;
    // Deep copy the item including variants
    this.formData = {
      ...item,
      variants: item.variants ? item.variants.map(v => ({ ...v })) : []
    };
    // Pass true to preserve the existing subCategoryId when filtering
    this.onCategoryChange(item.categoryId, true);
    this.showModal = true;
  }

  closeModal(): void {
    this.showModal = false;
    this.selectedItem = null;
  }

  addVariant(): void {
    if (this.newVariant.variantName && this.newVariant.price > 0) {
      if (!this.formData.variants) {
        this.formData.variants = [];
      }
      this.formData.variants.push({ ...this.newVariant });
      this.newVariant = {
        variantName: '',
        price: 0,
        quantity: undefined
      };
    }
  }

  removeVariant(index: number): void {
    this.formData.variants?.splice(index, 1);
  }

  saveMenuItem(): void {
    if (!this.formData.name || !this.formData.categoryId) {
      alert('Please fill in all required fields');
      return;
    }

    this.loading = true;

    // Prepare the payload - convert empty strings to null for ObjectId fields
    const payload = {
      ...this.formData,
      subCategoryId: this.formData.subCategoryId || undefined,
      variants: this.formData.variants || []
    };

    if (this.isEditMode && this.selectedItem) {
      // Update existing item - preserve existing metadata
      const updatePayload = {
        ...payload,
        id: this.selectedItem.id,
        createdBy: this.selectedItem.createdBy,
        createdDate: this.selectedItem.createdDate,
        lastUpdatedBy: 'Admin',
        lastUpdated: getIstNow().toISOString()
      };

      this.http.put(`${environment.apiUrl}/menu/${this.selectedItem.id}`, updatePayload)
        .subscribe({
          next: () => {
            this.loading = false;
            this.closeModal();
            this.loadMenuItems();
          },
          error: (error) => {
            console.error('Error updating menu item:', error);
            this.loading = false;
            alert('Failed to update menu item: ' + (error.error?.error || error.message));
          }
        });
    } else {
      // Create new item
      const createPayload = {
        ...payload,
        createdBy: 'Admin',
        createdDate: getIstNow().toISOString(),
        lastUpdatedBy: 'Admin',
        lastUpdated: getIstNow().toISOString()
      };

      this.http.post(`${environment.apiUrl}/menu`, createPayload)
        .subscribe({
          next: () => {
            this.loading = false;
            this.closeModal();
            this.loadMenuItems();
          },
          error: (error) => {
            console.error('Error creating menu item:', error);
            this.loading = false;
            alert('Failed to create menu item: ' + (error.error?.error || error.message));
          }
        });
    }
  }

  deleteMenuItem(item: MenuItem): void {
    if (confirm(`Are you sure you want to delete "${item.name}"?`)) {
      this.loading = true;
      this.http.delete(`${environment.apiUrl}/menu/${item.id}`)
        .subscribe({
          next: () => {
            this.loading = false;
            this.loadMenuItems();
          },
          error: (error) => {
            console.error('Error deleting menu item:', error);
            this.loading = false;
            alert('Failed to delete menu item');
          }
        });
    }
  }

  // Upload methods
  openUploadModal(): void {
    this.showUploadModal = true;
    this.selectedFile = null;
    this.uploadResult = null;
    this.uploadError = null;
    this.clearExisting = false;
  }

  closeUploadModal(): void {
    this.showUploadModal = false;
    this.selectedFile = null;
    this.uploadResult = null;
    this.uploadError = null;
  }

  onFileSelected(event: any): void {
    const file = event.target.files[0];
    if (file && this.isValidFile(file)) {
      this.selectedFile = file;
      this.uploadError = null;
      this.uploadResult = null;
    } else {
      this.uploadError = 'Please select a valid Excel file (.xlsx or .xls)';
      this.selectedFile = null;
    }
  }

  onDragOver(event: DragEvent): void {
    event.preventDefault();
    event.stopPropagation();
    this.dragOver = true;
  }

  onDragLeave(event: DragEvent): void {
    event.preventDefault();
    event.stopPropagation();
    this.dragOver = false;
  }

  onDrop(event: DragEvent): void {
    event.preventDefault();
    event.stopPropagation();
    this.dragOver = false;

    const files = event.dataTransfer?.files;
    if (files && files.length > 0) {
      const file = files[0];
      if (this.isValidFile(file)) {
        this.selectedFile = file;
        this.uploadError = null;
        this.uploadResult = null;
      } else {
        this.uploadError = 'Please select a valid Excel file (.xlsx or .xls)';
      }
    }
  }

  isValidFile(file: File): boolean {
    const validExtensions = ['.xlsx', '.xls'];
    const fileName = file.name.toLowerCase();
    return validExtensions.some(ext => fileName.endsWith(ext));
  }

  uploadFile(): void {
    if (!this.selectedFile) {
      this.uploadError = 'Please select a file first';
      return;
    }

    this.uploading = true;
    this.uploadError = null;
    this.uploadResult = null;

    const formData = new FormData();
    formData.append('file', this.selectedFile);

    const url = this.clearExisting
      ? `${environment.apiUrl}/menu/upload?clearExisting=true`
      : `${environment.apiUrl}/menu/upload`;

    this.http.post(url, formData)
      .subscribe({
        next: (response: any) => {
          this.uploading = false;
          this.uploadResult = response;
          this.selectedFile = null;
          // Reload menu items after successful upload
          this.loadMenuItems();
        },
        error: (error) => {
          this.uploading = false;
          this.uploadError = error.error?.error || 'Failed to upload file. Please try again.';
          console.error('Upload error:', error);
        }
      });
  }

  clearUploadSelection(): void {
    this.selectedFile = null;
    this.uploadResult = null;
    this.uploadError = null;
  }

  downloadTemplate(): void {
    const template =
      'category_name,subcategory_name,catalogue_name,variant_name,current_price,description\n' +
      'Beverages,Hot Drinks,Cappuccino,250ml,120,Classic Italian coffee with steamed milk foam\n' +
      'Beverages,Hot Drinks,Cappuccino,350ml,150,Classic Italian coffee with steamed milk foam\n' +
      'Beverages,Cold Drinks,Iced Latte,Regular,130,Refreshing iced latte with milk\n' +
      'Beverages,Cold Drinks,Iced Latte,Large,160,Refreshing iced latte with milk\n' +
      'Food,Breakfast,Sandwich,Regular,80,Grilled vegetable sandwich\n';

    const blob = new Blob([template], { type: 'text/csv' });
    const url = window.URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = 'menu_upload_template.csv';
    a.click();
    window.URL.revokeObjectURL(url);
  }
}
