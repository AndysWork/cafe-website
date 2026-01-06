import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { Subject, takeUntil } from 'rxjs';
import { PriceCalculatorService } from '../../services/price-calculator.service';
import { MenuService, MenuItem } from '../../services/menu.service';
import { OverheadCostService, OverheadCost, OverheadAllocation } from '../../services/overhead-cost.service';
import { FrozenItemService } from '../../services/frozen-item.service';
import { environment } from '../../../environments/environment';
import {
  Ingredient,
  MenuItemRecipe,
  IngredientUsage,
  PriceCalculation,
  FrozenItem,
  INGREDIENT_CATEGORIES,
  MEASUREMENT_UNITS
} from '../../models/ingredient.model';

@Component({
  selector: 'app-price-calculator',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './price-calculator.component.html',
  styleUrls: ['./price-calculator.component.scss']
})
export class PriceCalculatorComponent implements OnInit, OnDestroy {
  private destroy$ = new Subject<void>();

  // Data
  ingredients: Ingredient[] = [];
  recipes: MenuItemRecipe[] = [];
  menuItems: MenuItem[] = [];
  frozenItems: FrozenItem[] = [];

  // UI State
  activeTab: 'calculator' | 'ingredients' | 'recipes' | 'overhead' | 'frozen' = 'calculator';

  // Ingredient Management
  showIngredientModal = false;
  editingIngredient: Ingredient | null = null;
  ingredientForm: Ingredient = this.getEmptyIngredient();
  ingredientSearchTerm = '';
  selectedCategory = '';

  // Overhead Cost Management
  showOverheadModal = false;
  editingOverheadCost: OverheadCost | null = null;
  overheadCosts: OverheadCost[] = [];
  overheadForm: OverheadCost = this.getEmptyOverheadCost();
  overheadAllocation: OverheadAllocation | null = null;

  // Frozen Items Management
  showFrozenModal = false;
  editingFrozenItem: FrozenItem | null = null;
  frozenForm: FrozenItem = this.getEmptyFrozenItem();
  selectedFile: File | null = null;
  uploadProgress = false;
  uploadResult: { success: number; failed: number; errors: string[] } | null = null;

  // Inline editing
  editingInlineId: string | null = null;
  inlineEditForm: {price: number, unit: string} = {price: 0, unit: 'kg'};

  // Price Tracking
  showPriceHistoryModal = false;
  selectedIngredientForHistory: Ingredient | null = null;
  priceHistory: any[] = [];
  priceTrends: any = {};
  isRefreshingPrice = false;
  refreshingIngredientId: string | null = null;
  priceAlerts: Array<{message: string, type: 'success' | 'warning' | 'error', timestamp: Date}> = [];

  // Recipe/Calculator
  currentRecipe: MenuItemRecipe = this.getEmptyRecipe();
  selectedIngredient: Ingredient | null = null;
  isCalculatingOverhead = false;
  selectedIngredientId: string = '';
  ingredientQuantity = 0;
  ingredientUnit: 'kg' | 'gm' | 'ml' | 'pc' | 'ltr' = 'gm';
  customIngredientPrice: number = 0; // Override price for individual ingredient
  calculation: PriceCalculation | null = null;
  preparationTimeMinutes = 0; // Preparation time for overhead calculation

  // KPT Analysis Integration
  kptData: {
    avgPreparationTime: number;
    minPreparationTime: number;
    maxPreparationTime: number;
    medianPreparationTime: number;
    orderCount: number;
    stdDeviation: number;
  } | null = null;
  isLoadingKpt = false;
  kptMessage = '';

  // Constants for templates
  categories = INGREDIENT_CATEGORIES;
  units = MEASUREMENT_UNITS;
  measurementUnits = MEASUREMENT_UNITS;
  Math = Math; // Expose Math for template

  constructor(
    private priceCalculatorService: PriceCalculatorService,
    private menuService: MenuService,
    private overheadCostService: OverheadCostService,
    private frozenItemService: FrozenItemService,
    private http: HttpClient
  ) {}

  ngOnInit(): void {
    this.loadIngredients();
    this.loadRecipes();
    this.loadMenuItems();
    this.loadOverheadCosts();
    this.loadFrozenItems();
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  // ===== DATA LOADING =====

  loadIngredients(): void {
    this.priceCalculatorService.getIngredients()
      .pipe(takeUntil(this.destroy$))
      .subscribe(ingredients => {
        this.ingredients = ingredients;
      });
  }

  loadRecipes(): void {
    this.priceCalculatorService.getRecipes()
      .pipe(takeUntil(this.destroy$))
      .subscribe(recipes => {
        this.recipes = recipes;
      });
  }

  loadMenuItems(): void {
    this.menuService.getMenuItems()
      .pipe(takeUntil(this.destroy$))
      .subscribe(items => {
        this.menuItems = items;
      });
  }

  loadOverheadCosts(): void {
    this.overheadCostService.getAllOverheadCosts()
      .pipe(takeUntil(this.destroy$))
      .subscribe(costs => {
        this.overheadCosts = costs;
      });
  }

  // ===== OVERHEAD COST MANAGEMENT =====

  getEmptyOverheadCost(): OverheadCost {
    return {
      costType: '',
      monthlyCost: 0,
      operationalHoursPerDay: 11,
      workingDaysPerMonth: 30,
      isActive: true,
      description: ''
    };
  }

  openOverheadModal(overheadCost?: OverheadCost): void {
    if (overheadCost) {
      this.editingOverheadCost = overheadCost;
      this.overheadForm = { ...overheadCost };
    } else {
      this.editingOverheadCost = null;
      this.overheadForm = this.getEmptyOverheadCost();
    }
    this.showOverheadModal = true;
  }

  closeOverheadModal(): void {
    this.showOverheadModal = false;
    this.editingOverheadCost = null;
    this.overheadForm = this.getEmptyOverheadCost();
  }

  saveOverheadCost(): void {
    if (!this.overheadForm.costType || this.overheadForm.monthlyCost <= 0) {
      alert('Please fill in all required fields with valid values.');
      return;
    }

    if (this.editingOverheadCost?.id) {
      this.overheadCostService.updateOverheadCost(this.editingOverheadCost.id, this.overheadForm)
        .subscribe({
          next: () => {
            this.closeOverheadModal();
            this.loadOverheadCosts();
            this.showAlert('Overhead cost updated successfully', 'success');
            // Recalculate if preparation time is set
            if (this.preparationTimeMinutes > 0) {
              this.calculateOverheadCosts();
            }
          },
          error: (err) => {
            console.error('Error updating overhead cost:', err);
            this.showAlert('Failed to update overhead cost', 'error');
          }
        });
    } else {
      this.overheadCostService.createOverheadCost(this.overheadForm)
        .subscribe({
          next: () => {
            this.closeOverheadModal();
            this.loadOverheadCosts();
            this.showAlert('Overhead cost added successfully', 'success');
          },
          error: (err) => {
            console.error('Error adding overhead cost:', err);
            this.showAlert('Failed to add overhead cost', 'error');
          }
        });
    }
  }

  deleteOverheadCost(id?: string): void {
    if (!id) return;
    if (confirm('Are you sure you want to delete this overhead cost?')) {
      this.overheadCostService.deleteOverheadCost(id).subscribe({
        next: () => {
          this.loadOverheadCosts();
          this.showAlert('Overhead cost deleted successfully', 'success');
          // Recalculate if preparation time is set
          if (this.preparationTimeMinutes > 0) {
            this.calculateOverheadCosts();
          }
        },
        error: (err) => {
          console.error('Error deleting overhead cost:', err);
          this.showAlert('Failed to delete overhead cost', 'error');
        }
      });
    }
  }

  calculateOverheadCosts(): void {
    if (this.preparationTimeMinutes <= 0) {
      this.overheadAllocation = null;
      return;
    }

    if (this.isCalculatingOverhead) {
      return; // Already calculating, prevent duplicate calls
    }

    this.isCalculatingOverhead = true;
    this.overheadCostService.calculateOverheadAllocation(this.preparationTimeMinutes)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (allocation) => {
          this.overheadAllocation = allocation;
          // Update recipe overhead costs
          if (allocation && allocation.costs) {
            this.currentRecipe.overheadCosts.labourCharge =
              allocation.costs.find(c => c.costType.toLowerCase() === 'labour')?.allocatedCost || 10;
            this.currentRecipe.overheadCosts.rentAllocation =
              allocation.costs.find(c => c.costType.toLowerCase() === 'rent')?.allocatedCost || 5;
            this.currentRecipe.overheadCosts.electricityCharge =
              allocation.costs.find(c => c.costType.toLowerCase() === 'electricity')?.allocatedCost || 3;
          }
          this.isCalculatingOverhead = false;
          this.calculatePrice(); // Now recalculate with updated overhead costs
        },
        error: (err) => {
          console.error('Error calculating overhead costs:', err);
          this.showAlert('Failed to calculate overhead costs', 'error');
          this.isCalculatingOverhead = false;
        }
      });
  }

  initializeDefaultOverheadCosts(): void {
    if (confirm('This will create default overhead costs (Rent, Labour, Electricity). Continue?')) {
      this.overheadCostService.initializeDefaultOverheadCosts()
        .subscribe({
          next: () => {
            this.loadOverheadCosts();
            this.showAlert('Default overhead costs initialized successfully', 'success');
          },
          error: (err) => {
            console.error('Error initializing default overhead costs:', err);
            this.showAlert('Failed to initialize default overhead costs', 'error');
          }
        });
    }
  }

  // ===== INGREDIENT MANAGEMENT =====

  getEmptyIngredient(): Ingredient {
    return {
      name: '',
      category: 'others',
      marketPrice: 0,
      unit: 'kg',
      isActive: true
    };
  }

  openIngredientModal(ingredient?: Ingredient): void {
    if (ingredient) {
      this.editingIngredient = ingredient;
      this.ingredientForm = { ...ingredient };
    } else {
      this.editingIngredient = null;
      this.ingredientForm = this.getEmptyIngredient();
    }
    this.showIngredientModal = true;
  }

  closeIngredientModal(): void {
    this.showIngredientModal = false;
    this.editingIngredient = null;
    this.ingredientForm = this.getEmptyIngredient();
  }

  saveIngredient(): void {
    if (!this.ingredientForm.name || this.ingredientForm.marketPrice <= 0) {
      alert('Please fill in all required fields with valid values.');
      return;
    }

    if (this.editingIngredient?.id) {
      this.priceCalculatorService.updateIngredient(this.editingIngredient.id, this.ingredientForm)
        .subscribe({
          next: (response: any) => {
            this.closeIngredientModal();
            this.loadIngredients();

            // Check for price change alert
            if (response.priceChangeAlert) {
              this.showAlert(
                response.priceChangeAlert.message,
                Math.abs(response.priceChangeAlert.percentage) >= 20 ? 'error' : 'warning',
                8000
              );
            } else {
              this.showAlert('Ingredient updated successfully', 'success');
            }
          },
          error: (err) => {
            console.error('Error updating ingredient:', err);
            this.showAlert('Failed to update ingredient', 'error');
          }
        });
    } else {
      this.priceCalculatorService.addIngredient(this.ingredientForm)
        .subscribe({
          next: () => {
            this.closeIngredientModal();
            this.loadIngredients();
            this.showAlert('Ingredient added successfully', 'success');
          },
          error: (err) => {
            console.error('Error adding ingredient:', err);
            this.showAlert('Failed to add ingredient', 'error');
          }
        });
    }
  }

  // Inline editing methods
  startInlineEdit(ingredient: Ingredient): void {
    this.editingInlineId = ingredient.id || null;
    this.inlineEditForm = {
      price: ingredient.marketPrice,
      unit: ingredient.unit
    };
  }

  cancelInlineEdit(): void {
    this.editingInlineId = null;
  }

  saveInlineEdit(ingredient: Ingredient): void {
    if (!ingredient.id || this.inlineEditForm.price <= 0) {
      this.showAlert('Invalid price value', 'error');
      return;
    }

    const updatedIngredient: Partial<Ingredient> = {
      ...ingredient,
      marketPrice: this.inlineEditForm.price,
      unit: this.inlineEditForm.unit as any
    };

    this.priceCalculatorService.updateIngredient(ingredient.id, updatedIngredient)
      .subscribe({
        next: (response: any) => {
          this.editingInlineId = null;
          this.loadIngredients();

          // Check for major price change alert
          if (response.priceChangeAlert) {
            const alert = response.priceChangeAlert;
            this.showAlert(
              `⚠️ ${ingredient.name}: ${alert.message}`,
              Math.abs(alert.percentage) >= 20 ? 'error' : 'warning',
              10000
            );
          } else {
            this.showAlert(`${ingredient.name} price updated`, 'success', 3000);
          }
        },
        error: (err) => {
          console.error('Error updating price:', err);
          this.showAlert('Failed to update price', 'error');
          this.editingInlineId = null;
        }
      });
  }

  isEditingInline(ingredient: Ingredient): boolean {
    return this.editingInlineId === ingredient.id;
  }

  deleteIngredient(id?: string): void {
    if (!id) return;
    if (confirm('Are you sure you want to delete this ingredient?')) {
      this.priceCalculatorService.deleteIngredient(id).subscribe();
    }
  }

  get filteredIngredients(): Ingredient[] {
    // Convert frozen items to ingredient format
    const frozenAsIngredients: Ingredient[] = this.frozenItems
      .filter(item => item.isActive)
      .map(item => ({
        id: item.id,
        name: item.itemName,
        category: 'frozen',
        marketPrice: item.perPiecePrice,
        unit: 'pc' as const,
        isActive: item.isActive,
        lastUpdated: item.updatedAt
      }));

    // Combine regular ingredients and frozen items
    const allIngredients = [...this.ingredients, ...frozenAsIngredients];

    return allIngredients.filter(ing => {
      const matchesSearch = !this.ingredientSearchTerm ||
        ing.name.toLowerCase().includes(this.ingredientSearchTerm.toLowerCase());
      const matchesCategory = !this.selectedCategory || ing.category === this.selectedCategory;
      return matchesSearch && matchesCategory && ing.isActive;
    });
  }

  // ===== PRICE TRACKING METHODS =====

  refreshIngredientPrice(ingredient: Ingredient): void {
    if (!ingredient.id || this.isRefreshingPrice) return;

    this.isRefreshingPrice = true;
    this.refreshingIngredientId = ingredient.id;

    this.priceCalculatorService.refreshIngredientPrice(ingredient.id)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (result) => {
          this.isRefreshingPrice = false;
          this.refreshingIngredientId = null;

          if (result.success) {
            const changePercent = result.data?.priceChange || 0;
            const alertType = Math.abs(changePercent) > 15 ? 'warning' : 'success';

            this.showAlert(
              `✅ ${ingredient.name}: Price updated to ₹${result.data?.ingredient?.marketPrice || 0} (${changePercent > 0 ? '+' : ''}${changePercent.toFixed(2)}%)`,
              alertType
            );
            this.loadIngredients(); // Reload to get updated data
          } else {
            this.showAlert(`❌ Failed to refresh price for ${ingredient.name}: ${result.error || 'Unknown error'}`, 'error');
          }
        },
        error: (err) => {
          this.isRefreshingPrice = false;
          this.refreshingIngredientId = null;
          this.showAlert(`❌ Error refreshing price for ${ingredient.name}`, 'error');
        }
      });
  }

  bulkRefreshPrices(): void {
    if (this.isRefreshingPrice) return;

    this.isRefreshingPrice = true;
    this.showAlert('🔄 Starting bulk price refresh...', 'success');

    this.priceCalculatorService.bulkRefreshPrices()
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (result) => {
          this.isRefreshingPrice = false;

          if (result.success) {
            this.showAlert(
              `✅ Bulk refresh completed: ${result.updated} updated, ${result.failed} failed`,
              result.failed > 0 ? 'warning' : 'success'
            );
            this.loadIngredients();
          } else {
            this.showAlert(`❌ Bulk refresh failed: ${result.error}`, 'error');
          }
        },
        error: (err) => {
          this.isRefreshingPrice = false;
          this.showAlert('❌ Error during bulk refresh', 'error');
        }
      });
  }

  toggleAutoUpdate(ingredient: Ingredient): void {
    if (!ingredient.id) return;

    this.priceCalculatorService.toggleAutoUpdate(ingredient.id)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (result) => {
          if (result.success) {
            ingredient.autoUpdateEnabled = result.autoUpdateEnabled;
            this.showAlert(result.message, 'success');
          } else {
            this.showAlert(`Failed to toggle auto-update: ${result.error}`, 'error');
          }
        },
        error: (err) => {
          this.showAlert('Error toggling auto-update', 'error');
        }
      });
  }

  openPriceHistory(ingredient: Ingredient): void {
    if (!ingredient.id) return;

    this.selectedIngredientForHistory = ingredient;
    this.showPriceHistoryModal = true;
    this.priceHistory = [];
    this.priceTrends = {};

    // Load price history
    this.priceCalculatorService.getPriceHistory(ingredient.id, 30)
      .pipe(takeUntil(this.destroy$))
      .subscribe(history => {
        this.priceHistory = history;
      });

    // Load price trends for chart
    this.priceCalculatorService.getPriceTrends(ingredient.id, 30)
      .pipe(takeUntil(this.destroy$))
      .subscribe(trends => {
        this.priceTrends = trends;
      });
  }

  closePriceHistory(): void {
    this.showPriceHistoryModal = false;
    this.selectedIngredientForHistory = null;
    this.priceHistory = [];
    this.priceTrends = {};
  }

  getPriceSourceBadge(source?: string): { label: string; class: string } {
    switch (source?.toLowerCase()) {
      case 'agmarknet':
        return { label: '🌾 AGMARKNET', class: 'badge-agri' };
      case 'scraped':
        return { label: '🌐 Web', class: 'badge-web' };
      case 'api':
        return { label: '🔌 API', class: 'badge-api' };
      case 'manual':
      default:
        return { label: '✏️ Manual', class: 'badge-manual' };
    }
  }

  getPriceChangeClass(changePercent?: number): string {
    if (!changePercent) return '';
    if (changePercent > 0) return 'price-increase';
    if (changePercent < 0) return 'price-decrease';
    return '';
  }

  showAlert(message: string, type: 'success' | 'warning' | 'error', duration: number = 5000): void {
    this.priceAlerts.push({ message, type, timestamp: new Date() });

    // Auto-remove after specified duration
    setTimeout(() => {
      this.priceAlerts = this.priceAlerts.filter(a => a.message !== message);
    }, duration);
  }

  dismissAlert(alert: any): void {
    this.priceAlerts = this.priceAlerts.filter(a => a !== alert);
  }

  getChartData(): { labels: string[], values: number[] } {
    if (!this.priceTrends || Object.keys(this.priceTrends).length === 0) {
      return { labels: [], values: [] };
    }

    const entries = Object.entries(this.priceTrends).sort((a, b) => a[0].localeCompare(b[0]));
    return {
      labels: entries.map(([date]) => date),
      values: entries.map(([, price]) => price as number)
    };
  }

  // ===== RECIPE/CALCULATOR MANAGEMENT =====

  getEmptyRecipe(): MenuItemRecipe {
    return {
      menuItemName: '',
      ingredients: [],
      overheadCosts: {
        labourCharge: 10,
        rentAllocation: 5,
        electricityCharge: 3,
        wastagePercentage: 5,
        miscellaneous: 2
      },
      totalIngredientCost: 0,
      totalOverheadCost: 0,
      totalMakingCost: 0,
      profitMargin: 30,
      suggestedSellingPrice: 0
    };
  }

  loadRecipe(recipe: MenuItemRecipe): void {
    this.currentRecipe = { ...recipe };
    this.calculatePrice();
    this.activeTab = 'calculator';
  }

  onMenuItemNameChange(): void {
    // Check if a recipe exists for this menu item
    const existingRecipe = this.recipes.find(
      r => r.menuItemName.toLowerCase() === this.currentRecipe.menuItemName.toLowerCase()
    );

    if (existingRecipe) {
      const confirmLoad = confirm(
        `A recipe for "${existingRecipe.menuItemName}" already exists. Do you want to load it?`
      );
      if (confirmLoad) {
        this.loadRecipe(existingRecipe);
      }
    }

    // Fetch KPT data for this menu item
    this.fetchKptDataForMenuItem();
  }

  fetchKptDataForMenuItem(): void {
    if (!this.currentRecipe.menuItemName || this.currentRecipe.menuItemName.trim() === '') {
      this.kptData = null;
      this.kptMessage = '';
      return;
    }

    this.isLoadingKpt = true;
    this.kptMessage = '';

    // Get last 90 days of data
    const endDate = new Date();
    const startDate = new Date();
    startDate.setDate(startDate.getDate() - 90);

    const params = new URLSearchParams();
    params.append('startDate', startDate.toISOString().split('T')[0]);
    params.append('endDate', endDate.toISOString().split('T')[0]);

    this.http.get<any>(`${environment.apiUrl}/online-sales/kpt-analysis?${params.toString()}`)
      .subscribe({
        next: (response) => {
          if (response.success && response.menuItems) {
            // Find the matching menu item (case-insensitive)
            const menuItemName = this.currentRecipe.menuItemName.toLowerCase().trim();
            const matchingItem = response.menuItems.find((item: any) =>
              item.itemName.toLowerCase().trim() === menuItemName
            );

            if (matchingItem) {
              this.kptData = {
                avgPreparationTime: matchingItem.avgPreparationTime,
                minPreparationTime: matchingItem.minPreparationTime,
                maxPreparationTime: matchingItem.maxPreparationTime,
                medianPreparationTime: matchingItem.medianPreparationTime,
                orderCount: matchingItem.orderCount,
                stdDeviation: matchingItem.stdDeviation
              };

              // Auto-populate preparation time with the average (rounded to nearest minute)
              this.preparationTimeMinutes = Math.round(matchingItem.avgPreparationTime);

              // Calculate overhead costs with the new time
              if (this.preparationTimeMinutes > 0) {
                this.calculateOverheadCosts();
              }

              this.kptMessage = `✅ KPT data loaded from ${matchingItem.orderCount} orders (last 90 days)`;
            } else {
              this.kptData = null;
              this.kptMessage = '⚠️ No KPT data found for this item. Using manual input.';
            }
          }
          this.isLoadingKpt = false;
        },
        error: (error) => {
          console.error('Error fetching KPT data:', error);
          this.kptData = null;
          this.kptMessage = '⚠️ Could not load KPT data. Using manual input.';
          this.isLoadingKpt = false;
        }
      });
  }

  useKptValue(type: 'avg' | 'min' | 'max' | 'median'): void {
    if (!this.kptData) return;

    switch(type) {
      case 'avg':
        this.preparationTimeMinutes = Math.round(this.kptData.avgPreparationTime);
        break;
      case 'min':
        this.preparationTimeMinutes = Math.round(this.kptData.minPreparationTime);
        break;
      case 'max':
        this.preparationTimeMinutes = Math.round(this.kptData.maxPreparationTime);
        break;
      case 'median':
        this.preparationTimeMinutes = Math.round(this.kptData.medianPreparationTime);
        break;
    }

    // Recalculate overhead with the new time
    if (this.preparationTimeMinutes > 0) {
      this.calculateOverheadCosts();
    }
  }

  newRecipe(): void {
    this.currentRecipe = this.getEmptyRecipe();
    this.calculation = null;
    this.activeTab = 'calculator';
  }

  addIngredientToRecipe(): void {
    if (!this.selectedIngredient || this.ingredientQuantity <= 0) {
      alert('Please select an ingredient and enter a valid quantity.');
      return;
    }

    // Use custom price if provided, otherwise use market price
    const priceToUse = this.customIngredientPrice > 0
      ? this.customIngredientPrice
      : this.selectedIngredient.marketPrice;

    // Create a temporary ingredient object with the custom price for calculation
    const ingredientForCalc = {
      ...this.selectedIngredient,
      marketPrice: priceToUse
    };

    const cost = this.priceCalculatorService.calculateIngredientCost(
      this.ingredientQuantity,
      this.ingredientUnit,
      ingredientForCalc
    );

    const usage: IngredientUsage = {
      ingredientId: this.selectedIngredient.id!,
      ingredientName: this.selectedIngredient.name,
      quantity: this.ingredientQuantity,
      unit: this.ingredientUnit,
      unitPrice: priceToUse, // Store the custom price used
      totalCost: cost
    };

    this.currentRecipe.ingredients.push(usage);

    // Reset form
    this.selectedIngredient = null;
    this.selectedIngredientId = '';
    this.ingredientQuantity = 0;
    this.ingredientUnit = 'gm';
    this.customIngredientPrice = 0;

    this.calculatePrice();
  }

  removeIngredientFromRecipe(index: number): void {
    this.currentRecipe.ingredients.splice(index, 1);
    this.calculatePrice();
  }

  calculatePrice(): void {
    // Update totals in recipe
    this.currentRecipe.totalIngredientCost = this.currentRecipe.ingredients
      .reduce((sum, ing) => sum + ing.totalCost, 0);

    const wastage = (this.currentRecipe.totalIngredientCost * this.currentRecipe.overheadCosts.wastagePercentage) / 100;
    this.currentRecipe.totalOverheadCost =
      this.currentRecipe.overheadCosts.labourCharge +
      this.currentRecipe.overheadCosts.rentAllocation +
      this.currentRecipe.overheadCosts.electricityCharge +
      wastage +
      this.currentRecipe.overheadCosts.miscellaneous;

    this.currentRecipe.totalMakingCost = this.currentRecipe.totalIngredientCost + this.currentRecipe.totalOverheadCost;

    const profitAmount = (this.currentRecipe.totalMakingCost * this.currentRecipe.profitMargin) / 100;
    this.currentRecipe.suggestedSellingPrice = this.currentRecipe.totalMakingCost + profitAmount;

    // Get detailed calculation
    this.calculation = this.priceCalculatorService.calculateRecipePrice(this.currentRecipe);
  }

  saveCurrentRecipe(): void {
    if (!this.currentRecipe.menuItemName) {
      alert('Please enter a menu item name.');
      return;
    }

    if (this.currentRecipe.ingredients.length === 0) {
      alert('Please add at least one ingredient.');
      return;
    }

    this.priceCalculatorService.saveRecipe(this.currentRecipe)
      .subscribe(savedRecipe => {
        alert('Recipe saved successfully!');
        this.currentRecipe = savedRecipe;
      });
  }

  deleteRecipe(id?: string): void {
    if (!id) return;
    if (confirm('Are you sure you want to delete this recipe?')) {
      this.priceCalculatorService.deleteRecipe(id).subscribe(() => {
        if (this.currentRecipe.id === id) {
          this.newRecipe();
        }
      });
    }
  }

  // ===== UTILITY METHODS =====

  getCategoryLabel(value: string): string {
    const category = this.categories.find(c => c.value === value);
    return category ? category.label : value;
  }

  getUnitLabel(value: string): string {
    const unit = this.units.find(u => u.value === value);
    return unit ? unit.label : value;
  }

  exportRecipe(): void {
    if (!this.currentRecipe.id) {
      alert('Please save the recipe first.');
      return;
    }

    const json = this.priceCalculatorService.exportRecipe(this.currentRecipe);
    const blob = new Blob([json], { type: 'application/json' });
    const url = window.URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `${this.currentRecipe.menuItemName.replace(/\s+/g, '_')}_recipe.json`;
    a.click();
    window.URL.revokeObjectURL(url);
  }

  resetIngredients(): void {
    if (confirm('This will reset all ingredients to default values. Are you sure?')) {
      this.priceCalculatorService.resetToDefaultIngredients().subscribe();
    }
  }

  onIngredientSelect(event: Event): void {
    const selectElement = event.target as HTMLSelectElement;
    const ingredientId = selectElement.value;
    console.log('Ingredient selected:', ingredientId);
    console.log('Filtered ingredients:', this.filteredIngredients.map(i => ({ id: i.id, name: i.name })));
    // Search in both regular ingredients and frozen items (converted to ingredients)
    this.selectedIngredient = this.filteredIngredients.find(ing => ing.id === ingredientId) || null;
    console.log('Selected ingredient object:', this.selectedIngredient);

    // Auto-set unit based on ingredient
    if (this.selectedIngredient) {
      this.ingredientUnit = this.selectedIngredient.unit;
      // Auto-populate custom price with market price (can be overridden)
      this.customIngredientPrice = this.selectedIngredient.marketPrice;
    }
  }

  // Helper methods for chart calculations
  getChartAverage(): number {
    const data = this.getChartData();
    if (data.values.length === 0) return 0;
    const sum = data.values.reduce((a, b) => a + b, 0);
    return sum / data.values.length;
  }

  getChartPoints(): string {
    const data = this.getChartData();
    if (data.values.length === 0) return '';

    const max = Math.max(...data.values);
    const min = Math.min(...data.values);
    const range = max - min || 1;

    return data.values.map((val, idx) => {
      const x = (idx / (data.values.length - 1)) * 780 + 10;
      const y = 280 - ((val - min) / range) * 260;
      return `${x},${y}`;
    }).join(' ');
  }

  getChartPointX(index: number): number {
    const data = this.getChartData();
    if (data.values.length === 0) return 0;
    return (index / (data.values.length - 1)) * 780 + 10;
  }

  getChartPointY(value: number): number {
    const data = this.getChartData();
    if (data.values.length === 0) return 280;

    const max = Math.max(...data.values);
    const min = Math.min(...data.values);
    const range = max - min || 1;

    return 280 - ((value - min) / range) * 260;
  }

  getChartMidLabel(): string {
    const data = this.getChartData();
    if (data.labels.length === 0) return '';
    return data.labels[Math.floor(data.labels.length / 2)];
  }

  // ===== FROZEN ITEMS MANAGEMENT =====

  loadFrozenItems(): void {
    this.frozenItemService.getAllFrozenItems()
      .pipe(takeUntil(this.destroy$))
      .subscribe(items => {
        this.frozenItems = items;
      });
  }

  getEmptyFrozenItem(): FrozenItem {
    return {
      itemName: '',
      quantity: 0,
      packetWeight: 0,
      buyPrice: 0,
      perPiecePrice: 0,
      perPieceWeight: 0,
      vendor: '',
      category: 'frozen',
      isActive: true
    };
  }

  openFrozenModal(item?: FrozenItem): void {
    if (item) {
      this.editingFrozenItem = item;
      this.frozenForm = { ...item };
    } else {
      this.editingFrozenItem = null;
      this.frozenForm = this.getEmptyFrozenItem();
    }
    this.showFrozenModal = true;
  }

  closeFrozenModal(): void {
    this.showFrozenModal = false;
    this.editingFrozenItem = null;
    this.frozenForm = this.getEmptyFrozenItem();
  }

  saveFrozenItem(): void {
    if (!this.frozenForm.itemName || this.frozenForm.buyPrice <= 0) {
      alert('Please fill in all required fields with valid values.');
      return;
    }

    if (this.editingFrozenItem?.id) {
      this.frozenItemService.updateFrozenItem(this.editingFrozenItem.id, this.frozenForm)
        .subscribe({
          next: () => {
            this.closeFrozenModal();
            this.loadFrozenItems();
            this.showAlert('Frozen item updated successfully', 'success');
          },
          error: (err) => {
            console.error('Error updating frozen item:', err);
            this.showAlert('Failed to update frozen item', 'error');
          }
        });
    } else {
      this.frozenItemService.createFrozenItem(this.frozenForm)
        .subscribe({
          next: () => {
            this.closeFrozenModal();
            this.loadFrozenItems();
            this.showAlert('Frozen item added successfully', 'success');
          },
          error: (err) => {
            console.error('Error adding frozen item:', err);
            this.showAlert('Failed to add frozen item', 'error');
          }
        });
    }
  }

  deleteFrozenItem(id?: string): void {
    if (!id) return;
    if (confirm('Are you sure you want to delete this frozen item?')) {
      this.frozenItemService.deleteFrozenItem(id).subscribe({
        next: () => {
          this.loadFrozenItems();
          this.showAlert('Frozen item deleted successfully', 'success');
        },
        error: (err) => {
          console.error('Error deleting frozen item:', err);
          this.showAlert('Failed to delete frozen item', 'error');
        }
      });
    }
  }

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    if (input.files && input.files.length > 0) {
      this.selectedFile = input.files[0];
    }
  }

  uploadExcelFile(): void {
    if (!this.selectedFile) {
      alert('Please select an Excel file first.');
      return;
    }

    // Validate file extension
    const fileName = this.selectedFile.name.toLowerCase();
    if (!fileName.endsWith('.xlsx') && !fileName.endsWith('.xls')) {
      alert('Please select a valid Excel file (.xlsx or .xls)');
      return;
    }

    this.uploadProgress = true;
    this.uploadResult = null;

    this.frozenItemService.uploadExcel(this.selectedFile)
      .subscribe({
        next: (result) => {
          this.uploadProgress = false;
          this.uploadResult = result;
          this.loadFrozenItems();

          if (result.failed === 0) {
            this.showAlert(`✅ Successfully uploaded ${result.success} frozen items`, 'success');
          } else {
            this.showAlert(`⚠️ Uploaded ${result.success} items, ${result.failed} failed. Check details below.`, 'warning', 10000);
          }

          // Reset file input
          this.selectedFile = null;
          const fileInput = document.querySelector('input[type="file"]') as HTMLInputElement;
          if (fileInput) fileInput.value = '';
        },
        error: (err) => {
          this.uploadProgress = false;
          console.error('Error uploading Excel:', err);
          this.showAlert('Failed to upload Excel file', 'error');
        }
      });
  }

  downloadExcelTemplate(): void {
    // Create a simple CSV template
    const headers = 'ItemName,Quantity,PacketWeight,BuyPrice,PerPiecePrice,PerPieceWeight,Vendor\n';
    const example = 'Frozen Chicken Wings,10,1000,500,50,100,ABC Suppliers\n';
    const csvContent = headers + example;

    const blob = new Blob([csvContent], { type: 'text/csv' });
    const url = window.URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = 'frozen_items_template.csv';
    a.click();
    window.URL.revokeObjectURL(url);
  }}
