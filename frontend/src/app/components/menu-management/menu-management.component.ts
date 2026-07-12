import { Component, OnInit, OnDestroy, inject } from '@angular/core';
import { downloadFile } from '../../utils/file-download';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Subscription } from 'rxjs';
import { filter } from 'rxjs/operators';
import { MenuService } from '../../services/menu.service';
import { OutletService } from '../../services/outlet.service';
import { UIStore } from '../../store/ui.store';
import { environment } from '../../../environments/environment';
import { getIstNow } from '../../utils/date-utils';

interface MenuItemVariant {
  variantName: string;
  price: number;
  quantity?: number;
}

interface MenuItemAddOn {
  name: string;
  price: number;
  isActive?: boolean;
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
  webPrice: number;
  futureShopPrice?: number;
  futureOnlinePrice?: number;
  futureWebPrice?: number;
  dietaryType?: string;
  variants: MenuItemVariant[];
  addOns?: MenuItemAddOn[];
  isAddOnOnly?: boolean;
  isAvailable?: boolean;
  imageUrl?: string;
  imageThumbnailUrl?: string;
  isVisibleToCustomers?: boolean;
  createdBy: string;
  createdDate: string;
  lastUpdatedBy: string;
  lastUpdated: string;
}

interface Category {
  id?: string;
  _id?: string;
  name: string;
  isVisibleToCustomers?: boolean;
}

interface SubCategory {
  id?: string;
  _id?: string;
  categoryId: string;
  name: string;
  isVisibleToCustomers?: boolean;
}

interface AdminSubCategoryAccordionGroup {
  key: string;
  name: string;
  subCategoryId?: string;
  isVisibleToCustomers: boolean;
  items: MenuItem[];
}

interface AdminCategoryAccordionGroup {
  key: string;
  name: string;
  categoryId?: string;
  isVisibleToCustomers: boolean;
  itemCount: number;
  subGroups: AdminSubCategoryAccordionGroup[];
}

@Component({
  selector: 'app-menu-management',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './menu-management.component.html',
  styleUrl: './menu-management.component.scss'
})
export class MenuManagementComponent implements OnInit, OnDestroy {
  private outletService = inject(OutletService);
  private uiStore = inject(UIStore);
  private outletSubscription?: Subscription;

  menuItems: MenuItem[] = [];
  categories: Category[] = [];
  subCategories: SubCategory[] = [];
  filteredSubCategories: SubCategory[] = [];

  private menuRefreshSubscription?: Subscription;

  loading = false;
  syncingRecipePrices = false;
  showModal = false;
  showUploadModal = false;
  isEditMode = false;
  selectedItem: MenuItem | null = null;

  searchTerm = '';
  filterCategory = '';
  activeStockTab: 'active' | 'outOfStock' = 'active';
  private collapsedCategoryAccordionKeys: Set<string> = new Set();
  private collapsedSubCategoryAccordionKeys: Set<string> = new Set();

  // Upload properties
  selectedFile: File | null = null;
  uploading = false;
  uploadResult: any = null;
  uploadError: string | null = null;
  dragOver = false;
  clearExisting = false;

  // Image upload properties
  imageFile: File | null = null;
  imagePreview: string | null = null;
  uploadingImage = false;

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
    webPrice: 0,
    futureShopPrice: undefined,
    futureOnlinePrice: undefined,
    futureWebPrice: undefined,
    variants: [],
    addOns: []
  };

  newVariant: MenuItemVariant = {
    variantName: '',
    price: 0,
    quantity: undefined
  };

  newAddOn: MenuItemAddOn = {
    name: '',
    price: 0,
    isActive: true
  };

  selectedExistingAddOnItemId = '';

  constructor(
    private http: HttpClient,
    private menuService: MenuService
  ) {}

  ngOnInit(): void {
    // Subscribe to outlet changes
    this.outletSubscription = this.outletService.selectedOutlet$
      .pipe(filter(outlet => outlet !== null))
      .subscribe(() => {
        this.loadMenuItems();
        this.loadCategories();
        this.loadSubCategories();
      });

    // Subscribe to menu refresh notifications
    this.menuRefreshSubscription = this.menuService.menuItemsRefresh$.subscribe((refresh) => {
      if (refresh) {
        this.loadMenuItems();
      }
    });

    // Load immediately if outlet is already selected
    if (this.outletService.getSelectedOutlet()) {
      this.loadMenuItems();
      this.loadCategories();
      this.loadSubCategories();
    }
  }

  ngOnDestroy(): void {
    this.outletSubscription?.unsubscribe();
    this.menuRefreshSubscription?.unsubscribe();
  }

  refreshMenuItems(): void {
    this.loadMenuItems();
  }

  syncRecipePricesToMenu(): void {
    if (!confirm('Re-sync recipe prices to menu items for all outlets?')) {
      return;
    }

    this.syncingRecipePrices = true;
    this.menuService.syncRecipePrices().subscribe({
      next: (response) => {
        this.syncingRecipePrices = false;
        this.uiStore.success(response?.message || 'Recipe prices synced to menu items');
        this.loadMenuItems();
      },
      error: (error) => {
        this.syncingRecipePrices = false;
        this.uiStore.error(error?.message || error?.error?.error || 'Failed to sync recipe prices');
      }
    });
  }

  loadMenuItems(): void {
    this.loading = true;
    const headers = new HttpHeaders({
      'ngsw-bypass': 'true',
      'Cache-Control': 'no-cache',
      Pragma: 'no-cache'
    });

    this.http.get<MenuItem[]>(`${environment.apiUrl}/menu`, { headers })
      .subscribe({
        next: (data) => {
          this.menuItems = data;
          this.loading = false;
        },
        error: (error) => {
          console.error('Error loading menu items:', error);
          this.uiStore.error('Failed to load menu items. Please check the console for details.');
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
    const selectedCategoryKey = this.normalizeToken(categoryId);
    this.filteredSubCategories = this.subCategories.filter(
      sc => this.normalizeToken(sc.categoryId) === selectedCategoryKey
    );

    // Only reset subCategoryId if we're not preserving it (i.e., during actual category change, not during edit load)
    if (!preserveSubCategory) {
      this.formData.subCategoryId = undefined;
    }

    const category = this.categories.find(c => this.getCategoryKey(c) === selectedCategoryKey);
    if (category) {
      this.formData.category = category.name;
    }
  }

  private normalizeToken(value?: string | null): string {
    return (value || '').toString().trim().toLowerCase();
  }

  getCategoryKey(category?: Category | null): string {
    return (category?.id || category?._id || '').toString();
  }

  getSubCategoryKey(subCategory?: SubCategory | null): string {
    return (subCategory?.id || subCategory?._id || '').toString();
  }

  private getCategoryNameByKey(categoryKey: string): string {
    const normalizedKey = this.normalizeToken(categoryKey);
    if (!normalizedKey) return '';

    const matched = this.categories.find(c => this.normalizeToken(this.getCategoryKey(c)) === normalizedKey);
    return (matched?.name || '').trim();
  }

  private matchesSelectedCategory(item: MenuItem): boolean {
    const selectedKey = this.normalizeToken(this.filterCategory);
    if (!selectedKey) return true;

    const itemCategoryId = this.normalizeToken(item.categoryId);
    if (itemCategoryId && itemCategoryId === selectedKey) {
      return true;
    }

    const selectedCategoryName = this.normalizeToken(this.getCategoryNameByKey(this.filterCategory));
    const itemCategoryName = this.normalizeToken(item.category);
    if (selectedCategoryName && itemCategoryName && selectedCategoryName === itemCategoryName) {
      return true;
    }

    if (itemCategoryId) {
      const itemCategoryMappedName = this.normalizeToken(this.getCategoryNameByKey(item.categoryId));
      if (selectedCategoryName && itemCategoryMappedName && selectedCategoryName === itemCategoryMappedName) {
        return true;
      }
    }

    return false;
  }

  get filteredMenuItems(): MenuItem[] {
    return this.menuItems.filter(item => {
      const matchesSearch = !this.searchTerm ||
        item.name?.toLowerCase().includes(this.searchTerm.toLowerCase()) ||
        item.description?.toLowerCase().includes(this.searchTerm.toLowerCase());

      const matchesCategory = this.matchesSelectedCategory(item);

      return matchesSearch && matchesCategory;
    });
  }

  get activeMenuItems(): MenuItem[] {
    return this.filteredMenuItems.filter(item => item.isAvailable !== false);
  }

  get outOfStockMenuItems(): MenuItem[] {
    return this.filteredMenuItems.filter(item => item.isAvailable === false);
  }

  get visibleMenuItems(): MenuItem[] {
    return this.activeStockTab === 'active' ? this.activeMenuItems : this.outOfStockMenuItems;
  }

  get groupedVisibleMenuItems(): AdminCategoryAccordionGroup[] {
    const categoryMap = new Map<string, {
      key: string;
      name: string;
      categoryId?: string;
      items: MenuItem[];
      subMap: Map<string, AdminSubCategoryAccordionGroup>;
    }>();

    for (const item of this.visibleMenuItems) {
      const categoryKey = this.normalizeToken(item.categoryId) || `name:${this.normalizeToken(item.category) || 'uncategorized'}`;
      const categoryName = item.category || this.getCategoryNameByKey(item.categoryId) || 'Uncategorized';

      if (!categoryMap.has(categoryKey)) {
        categoryMap.set(categoryKey, {
          key: categoryKey,
          name: categoryName,
          items: [],
          subMap: new Map<string, AdminSubCategoryAccordionGroup>()
        });
      }

      const categoryGroup = categoryMap.get(categoryKey)!;
      categoryGroup.items.push(item);

      const categoryId = item.categoryId || undefined;
      if (categoryId && !categoryGroup.key.startsWith('name:')) {
        categoryGroup.categoryId = categoryId;
      }

      const subCategoryName = this.getSubCategoryNameByItem(item);
      const subCategoryKey = this.normalizeToken(item.subCategoryId)
        || `name:${this.normalizeToken(subCategoryName) || 'general'}`;

      if (!categoryGroup.subMap.has(subCategoryKey)) {
        const subCategoryRef = this.subCategories.find(sc =>
          this.normalizeToken(this.getSubCategoryKey(sc)) === this.normalizeToken(item.subCategoryId)
        );

        categoryGroup.subMap.set(subCategoryKey, {
          key: subCategoryKey,
          name: subCategoryName,
          subCategoryId: item.subCategoryId || this.getSubCategoryKey(subCategoryRef) || undefined,
          isVisibleToCustomers: this.isVisibleToCustomers(subCategoryRef?.isVisibleToCustomers),
          items: []
        });
      }

      categoryGroup.subMap.get(subCategoryKey)!.items.push(item);
    }

    return Array.from(categoryMap.values()).map(group => {
      const categoryId = group.categoryId;
      const categoryRef = this.categories.find(c => this.normalizeToken(this.getCategoryKey(c)) === this.normalizeToken(categoryId));

      return {
        key: group.key,
        name: group.name,
        categoryId,
        isVisibleToCustomers: this.isVisibleToCustomers(categoryRef?.isVisibleToCustomers),
        itemCount: group.items.length,
        subGroups: Array.from(group.subMap.values())
      };
    });
  }

  isVisibleToCustomers(flag?: boolean): boolean {
    return flag !== false;
  }

  isCategoryAccordionExpanded(key: string): boolean {
    return !this.collapsedCategoryAccordionKeys.has(key);
  }

  toggleCategoryAccordion(key: string): void {
    if (this.collapsedCategoryAccordionKeys.has(key)) {
      this.collapsedCategoryAccordionKeys.delete(key);
    } else {
      this.collapsedCategoryAccordionKeys.add(key);
    }

    this.collapsedCategoryAccordionKeys = new Set(this.collapsedCategoryAccordionKeys);
  }

  isSubCategoryAccordionExpanded(categoryKey: string, subKey: string): boolean {
    return !this.collapsedSubCategoryAccordionKeys.has(`${categoryKey}::${subKey}`);
  }

  toggleSubCategoryAccordion(categoryKey: string, subKey: string): void {
    const key = `${categoryKey}::${subKey}`;

    if (this.collapsedSubCategoryAccordionKeys.has(key)) {
      this.collapsedSubCategoryAccordionKeys.delete(key);
    } else {
      this.collapsedSubCategoryAccordionKeys.add(key);
    }

    this.collapsedSubCategoryAccordionKeys = new Set(this.collapsedSubCategoryAccordionKeys);
  }

  trackByKey(index: number, item: { key?: string }): string {
    return item?.key || `${index}`;
  }

  private getSubCategoryNameByItem(item: MenuItem): string {
    const itemSubCategoryKey = this.normalizeToken(item.subCategoryId);
    if (!itemSubCategoryKey) {
      return 'General';
    }

    const subCategory = this.subCategories.find(sc =>
      this.normalizeToken(this.getSubCategoryKey(sc)) === itemSubCategoryKey
    );

    return subCategory?.name || 'General';
  }

  openCreateModal(): void {
    this.isEditMode = false;
    this.imageFile = null;
    this.imagePreview = null;
    this.selectedExistingAddOnItemId = '';
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
      webPrice: 0,
      dietaryType: 'veg',
      variants: [],
      addOns: [],
      isAddOnOnly: false
    };
    this.filteredSubCategories = [];
    this.showModal = true;
  }

  openCreateAddOnModal(): void {
    this.isEditMode = false;
    this.imageFile = null;
    this.imagePreview = null;
    this.selectedExistingAddOnItemId = '';
    this.formData = {
      name: '',
      description: '',
      category: '',
      categoryId: this.filterCategory || '',
      subCategoryId: undefined,
      quantity: 0,
      makingPrice: 0,
      packagingCharge: 0,
      shopSellingPrice: 0,
      onlinePrice: 0,
      webPrice: 0,
      dietaryType: 'veg',
      variants: [],
      addOns: [],
      isAddOnOnly: true
    };

    if (this.formData.categoryId) {
      this.onCategoryChange(this.formData.categoryId);
    } else {
      this.filteredSubCategories = [];
    }

    this.showModal = true;
  }

  openEditModal(item: MenuItem): void {
    this.isEditMode = true;
    this.selectedItem = item;
    this.imageFile = null;
    this.imagePreview = item.imageUrl || null;
    // Deep copy the item including variants
    this.formData = {
      ...item,
      dietaryType: item.dietaryType || 'veg',
      variants: item.variants ? item.variants.map(v => ({ ...v })) : [],
      addOns: item.addOns ? item.addOns.map(a => ({ ...a })) : [],
      isAddOnOnly: item.isAddOnOnly === true
    };
    this.selectedExistingAddOnItemId = '';
    // Pass true to preserve the existing subCategoryId when filtering
    this.onCategoryChange(item.categoryId, true);
    this.showModal = true;
  }

  closeModal(): void {
    this.showModal = false;
    this.selectedItem = null;
    this.imageFile = null;
    this.imagePreview = null;
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

  addAddOn(): void {
    if (this.newAddOn.name && this.newAddOn.price > 0) {
      if (!this.formData.addOns) {
        this.formData.addOns = [];
      }
      this.formData.addOns.push({ ...this.newAddOn, isActive: this.newAddOn.isActive !== false });
      this.newAddOn = {
        name: '',
        price: 0,
        isActive: true
      };
    }
  }

  get addOnCandidateItems(): MenuItem[] {
    const selectedItemId = this.selectedItem?.id;
    const existingNames = new Set((this.formData.addOns || []).map(a => (a.name || '').trim().toLowerCase()));

    return this.menuItems.filter(item => {
      if (!item?.id || !item?.name) return false;
      if (selectedItemId && item.id === selectedItemId) return false;
      return !existingNames.has(item.name.trim().toLowerCase());
    });
  }

  addExistingItemAsAddOn(): void {
    if (!this.selectedExistingAddOnItemId) {
      this.uiStore.warning('Select an existing menu item to add as add-on');
      return;
    }

    const existingItem = this.menuItems.find(item => item.id === this.selectedExistingAddOnItemId);
    if (!existingItem) {
      this.uiStore.warning('Selected menu item was not found');
      return;
    }

    const addOnPrice = existingItem.webPrice || existingItem.shopSellingPrice || existingItem.onlinePrice || 0;
    if (addOnPrice <= 0) {
      this.uiStore.warning('Selected item does not have a valid price for add-on');
      return;
    }

    if (!this.formData.addOns) {
      this.formData.addOns = [];
    }

    this.formData.addOns.push({
      name: existingItem.name,
      price: addOnPrice,
      isActive: true
    });

    this.selectedExistingAddOnItemId = '';
  }

  removeAddOn(index: number): void {
    this.formData.addOns?.splice(index, 1);
  }

  saveMenuItem(): void {
    if (!this.formData.name || !this.formData.categoryId) {
      this.uiStore.warning('Please fill in all required fields');
      return;
    }

    this.loading = true;

    // Prepare the payload - convert empty strings to null for ObjectId fields
    // Strip zero future prices so null is sent (and omitted by WhenWritingNull) instead of 0
    const payload = {
      ...this.formData,
      subCategoryId: this.formData.subCategoryId || undefined,
      futureShopPrice: this.formData.futureShopPrice || undefined,
      futureOnlinePrice: this.formData.futureOnlinePrice || undefined,
      futureWebPrice: this.formData.futureWebPrice || this.formData.futureShopPrice || undefined,
      webPrice: this.formData.webPrice || this.formData.shopSellingPrice || 0,
      variants: this.formData.variants || [],
      addOns: (this.formData.addOns || []).map(a => ({
        name: a.name,
        price: a.price,
        isActive: a.isActive !== false
      })),
      isAddOnOnly: this.formData.isAddOnOnly === true
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
            if (this.imageFile && this.selectedItem) {
              this.uploadMenuImage(this.selectedItem.id);
            } else {
              this.loading = false;
              this.closeModal();
              this.loadMenuItems();
            }
          },
          error: (error) => {
            console.error('Error updating menu item:', error);
            this.loading = false;
            this.uiStore.error('Failed to update menu item: ' + (error.error?.error || error.message));
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

      this.http.post<any>(`${environment.apiUrl}/menu`, createPayload)
        .subscribe({
          next: (response) => {
            if (this.imageFile && response?.data?.id) {
              this.uploadMenuImage(response.data.id);
            } else {
              this.loading = false;
              this.closeModal();
              this.loadMenuItems();
            }
          },
          error: (error) => {
            console.error('Error creating menu item:', error);
            this.loading = false;
            this.uiStore.error('Failed to create menu item: ' + (error.error?.error || error.message));
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
            this.uiStore.error('Failed to delete menu item');
          }
        });
    }
  }

  toggleAvailability(item: MenuItem): void {
    const action = item.isAvailable ? 'mark as out of stock' : 'mark as in stock';
    if (confirm(`Are you sure you want to ${action} "${item.name}"?`)) {
      this.loading = true;
      this.menuService.toggleAvailability(item.id)
        .subscribe({
          next: () => {
            this.loading = false;
            this.loadMenuItems();
          },
          error: (error) => {
            console.error('Error toggling availability:', error);
            this.loading = false;
            this.uiStore.error('Failed to update availability status');
          }
        });
    }
  }

  toggleItemCustomerVisibility(item: MenuItem): void {
    const nextValue = !this.isVisibleToCustomers(item.isVisibleToCustomers);
    const actionLabel = nextValue ? 'show to customers' : 'hide from customers';

    if (!confirm(`Are you sure you want to ${actionLabel} "${item.name}"?`)) {
      return;
    }

    this.loading = true;

    const payload = {
      ...item,
      isVisibleToCustomers: nextValue,
      lastUpdatedBy: 'Admin',
      lastUpdated: getIstNow().toISOString()
    };

    this.http.put(`${environment.apiUrl}/menu/${item.id}`, payload)
      .subscribe({
        next: () => {
          this.loading = false;
          this.loadMenuItems();
        },
        error: (error) => {
          console.error('Error toggling customer visibility:', error);
          this.loading = false;
          this.uiStore.error('Failed to update customer visibility');
        }
      });
  }

  toggleCategoryCustomerVisibility(group: AdminCategoryAccordionGroup): void {
    if (!group.categoryId) {
      this.uiStore.warning('This category cannot be updated from this view.');
      return;
    }

    const category = this.categories.find(c =>
      this.normalizeToken(this.getCategoryKey(c)) === this.normalizeToken(group.categoryId)
    );

    if (!category) {
      this.uiStore.warning('Category record not found. Please refresh and try again.');
      return;
    }

    const nextValue = !this.isVisibleToCustomers(category.isVisibleToCustomers);
    const actionLabel = nextValue ? 'show to customers' : 'hide from customers';

    if (!confirm(`Are you sure you want to ${actionLabel} category "${category.name}"?`)) {
      return;
    }

    this.loading = true;
    const categoryId = this.getCategoryKey(category);

    this.http.put(`${environment.apiUrl}/categories/${categoryId}`, {
      ...category,
      id: categoryId,
      isVisibleToCustomers: nextValue
    }).subscribe({
      next: () => {
        this.loading = false;
        this.loadCategories();
      },
      error: (error) => {
        console.error('Error toggling category visibility:', error);
        this.loading = false;
        this.uiStore.error('Failed to update category visibility');
      }
    });
  }

  toggleSubCategoryCustomerVisibility(group: AdminSubCategoryAccordionGroup): void {
    if (!group.subCategoryId) {
      this.uiStore.warning('This subcategory cannot be updated from this view.');
      return;
    }

    const subCategory = this.subCategories.find(sc =>
      this.normalizeToken(this.getSubCategoryKey(sc)) === this.normalizeToken(group.subCategoryId)
    );

    if (!subCategory) {
      this.uiStore.warning('Subcategory record not found. Please refresh and try again.');
      return;
    }

    const nextValue = !this.isVisibleToCustomers(subCategory.isVisibleToCustomers);
    const actionLabel = nextValue ? 'show to customers' : 'hide from customers';

    if (!confirm(`Are you sure you want to ${actionLabel} subcategory "${subCategory.name}"?`)) {
      return;
    }

    this.loading = true;
    const subCategoryId = this.getSubCategoryKey(subCategory);

    this.http.put(`${environment.apiUrl}/subcategories/${subCategoryId}`, {
      ...subCategory,
      id: subCategoryId,
      isVisibleToCustomers: nextValue
    }).subscribe({
      next: () => {
        this.loading = false;
        this.loadSubCategories();
      },
      error: (error) => {
        console.error('Error toggling subcategory visibility:', error);
        this.loading = false;
        this.uiStore.error('Failed to update subcategory visibility');
      }
    });
  }

  // Image methods
  onImageSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input?.files?.[0];
    if (!file) return;

    const validTypes = ['image/jpeg', 'image/png', 'image/webp', 'image/gif'];
    if (!validTypes.includes(file.type)) {
      this.uiStore.warning('Please select a valid image file (JPEG, PNG, WebP, or GIF)');
      return;
    }
    if (file.size > 5 * 1024 * 1024) {
      this.uiStore.warning('Image must be less than 5MB');
      return;
    }

    this.imageFile = file;
    const reader = new FileReader();
    reader.onload = (e) => {
      this.imagePreview = e.target?.result as string;
    };
    reader.readAsDataURL(file);
  }

  removeImage(): void {
    this.imageFile = null;
    this.imagePreview = null;
  }

  deleteExistingImage(): void {
    if (!this.selectedItem || !this.selectedItem.imageUrl) return;
    if (!confirm('Are you sure you want to delete this image?')) return;

    this.uploadingImage = true;
    this.http.delete(`${environment.apiUrl}/menu/${this.selectedItem.id}/image`)
      .subscribe({
        next: () => {
          this.uploadingImage = false;
          this.imagePreview = null;
          if (this.selectedItem) {
            this.selectedItem.imageUrl = undefined;
          }
          this.loadMenuItems();
        },
        error: (error) => {
          this.uploadingImage = false;
          console.error('Error deleting image:', error);
          this.uiStore.error('Failed to delete image');
        }
      });
  }

  private uploadMenuImage(menuItemId: string): void {
    if (!this.imageFile) return;

    this.uploadingImage = true;
    const formData = new FormData();
    formData.append('file', this.imageFile);

    this.http.post(`${environment.apiUrl}/menu/${menuItemId}/image`, formData)
      .subscribe({
        next: () => {
          this.uploadingImage = false;
          this.loading = false;
          this.closeModal();
          this.loadMenuItems();
        },
        error: (error) => {
          this.uploadingImage = false;
          this.loading = false;
          console.error('Error uploading image:', error);
          this.uiStore.error('Item saved but image upload failed: ' + (error.error?.error || error.message));
          this.closeModal();
          this.loadMenuItems();
        }
      });
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
    downloadFile(template, 'menu_upload_template.csv');
  }

  normalizeDietaryType(value?: string): 'veg' | 'non-veg' | 'egg' | 'vegan' {
    const normalized = (value || 'veg').trim().toLowerCase();
    if (normalized === 'nonveg' || normalized === 'non-veg') return 'non-veg';
    if (normalized === 'egg') return 'egg';
    if (normalized === 'vegan') return 'vegan';
    return 'veg';
  }

  getDietaryLabel(value?: string): string {
    const dietary = this.normalizeDietaryType(value);
    if (dietary === 'non-veg') return '🔴 Non-Veg';
    if (dietary === 'egg') return '🟡 Egg';
    if (dietary === 'vegan') return '🟢 Vegan';
    return '🟢 Veg';
  }

  trackByIndex(index: number): number { return index; }
  trackByObjId(index: number, item: any): string { return item?.id || item?._id || String(index); }
}
