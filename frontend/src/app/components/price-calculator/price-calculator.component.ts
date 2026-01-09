import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { Subject, takeUntil } from 'rxjs';
import { PriceCalculatorService } from '../../services/price-calculator.service';
import { MenuService, MenuItem } from '../../services/menu.service';
import { OverheadCostService, OverheadCost, OverheadAllocation } from '../../services/overhead-cost.service';
import { FrozenItemService } from '../../services/frozen-item.service';
import { PriceForecastService, PriceForecast } from '../../services/price-forecast.service';
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

  // Oil Usage Calculation
  fryingTimeMinutes = 0; // Optional frying time for items that use oil
  oilCapacityLiters = 2.5; // Oil capacity in liters
  oilPricePer750ml = 112; // Price for 750ml oil bottle
  oilUsageDays = 7; // Number of days oil is used
  oilUsageHoursPerDay = 9; // Operating hours per day
  calculatedOilCost = 0; // Calculated oil cost for this item

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

  // Price Forecast Integration
  priceForecast: {
    packagingCost: number;
    onlineDeduction: number;
    onlineDiscount: number;
    shopPrice: number;
    shopDeliveryPrice: number;
    onlinePrice: number;
    onlinePayout: number;
    onlineProfit: number;
    offlineProfit: number;
    takeawayProfit: number;
    // Future pricing
    futureShopPrice?: number;
    futureOnlinePrice?: number;
    futureShopProfit?: number;
    futureOnlineProfit?: number;
  } | null = null;
  showForecastPanel = true;
  savedPriceForecast: any = null; // Currently saved forecast with history
  showForecastHistoryModal = false;
  priceChangeReason = '';

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
    private priceForecastService: PriceForecastService,
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
      .subscribe({
        next: (recipes) => {
          this.recipes = recipes;
          console.log('Loaded recipes:', recipes);
        },
        error: (err) => {
          console.error('Error loading recipes:', err);
          this.showAlert('Failed to load recipes', 'error');
        }
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

    // Store original values for rollback if needed
    const originalPrice = ingredient.marketPrice;
    const originalUnit = ingredient.unit;

    // Optimistic update - update local ingredient immediately for instant UI feedback
    ingredient.marketPrice = this.inlineEditForm.price;
    ingredient.unit = this.inlineEditForm.unit as any;
    this.editingInlineId = null;

    const updatedIngredient: Partial<Ingredient> = {
      ...ingredient,
      isActive: ingredient.isActive ?? true // Preserve or default to true
    };

    this.priceCalculatorService.updateIngredient(ingredient.id, updatedIngredient)
      .subscribe({
        next: (response: any) => {
          // Reload ingredients in background to sync with server
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
          // Rollback optimistic update on error
          ingredient.marketPrice = originalPrice;
          ingredient.unit = originalUnit;
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
      // Show ingredient if isActive is true or undefined (default to active)
      const isActiveOrUndefined = ing.isActive !== false;
      return matchesSearch && matchesCategory && isActiveOrUndefined;
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

    // Load preparation time if available
    if (recipe.preparationTimeMinutes !== undefined && recipe.preparationTimeMinutes !== null) {
      this.preparationTimeMinutes = recipe.preparationTimeMinutes;
      console.log('Loaded preparation time:', recipe.preparationTimeMinutes);
    } else {
      this.preparationTimeMinutes = 0;
    }

    // Load oil usage data if available
    if (recipe.oilUsage) {
      this.fryingTimeMinutes = recipe.oilUsage.fryingTimeMinutes || 0;
      this.oilCapacityLiters = recipe.oilUsage.oilCapacityLiters || 2.5;
      this.oilPricePer750ml = recipe.oilUsage.oilPricePer750ml || 112;
      this.oilUsageDays = recipe.oilUsage.oilUsageDays || 7;
      this.oilUsageHoursPerDay = recipe.oilUsage.oilUsageHoursPerDay || 9;
      this.calculatedOilCost = recipe.oilUsage.calculatedOilCost || 0;
      console.log('Loaded oil usage data:', recipe.oilUsage);
    } else {
      // Reset oil usage to defaults
      this.fryingTimeMinutes = 0;
      this.oilCapacityLiters = 2.5;
      this.oilPricePer750ml = 112;
      this.oilUsageDays = 7;
      this.oilUsageHoursPerDay = 9;
      this.calculatedOilCost = 0;
    }

    // Load price forecast data if available
    if (recipe.priceForecast) {
      this.priceForecast = { ...recipe.priceForecast };
      console.log('Loaded price forecast data:', recipe.priceForecast);
    } else {
      // Initialize price forecast if not present
      this.initializePriceForecast();
    }

    // Load KPT data if available
    if (recipe.kptAnalysis) {
      this.kptData = { ...recipe.kptAnalysis };
      console.log('Loaded KPT analysis data:', recipe.kptAnalysis);
    }

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
    // Calculate oil cost if frying time is specified
    if (this.fryingTimeMinutes > 0) {
      this.calculateOilCost();
    } else {
      this.calculatedOilCost = 0;
    }

    // Update totals in recipe
    this.currentRecipe.totalIngredientCost = this.currentRecipe.ingredients
      .reduce((sum, ing) => sum + ing.totalCost, 0);

    const wastage = (this.currentRecipe.totalIngredientCost * this.currentRecipe.overheadCosts.wastagePercentage) / 100;
    this.currentRecipe.totalOverheadCost =
      this.currentRecipe.overheadCosts.labourCharge +
      this.currentRecipe.overheadCosts.rentAllocation +
      this.currentRecipe.overheadCosts.electricityCharge +
      wastage +
      this.calculatedOilCost + // Add oil cost to overhead
      this.currentRecipe.overheadCosts.miscellaneous;

    this.currentRecipe.totalMakingCost = this.currentRecipe.totalIngredientCost + this.currentRecipe.totalOverheadCost;

    const profitAmount = (this.currentRecipe.totalMakingCost * this.currentRecipe.profitMargin) / 100;
    this.currentRecipe.suggestedSellingPrice = this.currentRecipe.totalMakingCost + profitAmount;

    // Get detailed calculation
    this.calculation = this.priceCalculatorService.calculateRecipePrice(this.currentRecipe);

    // Calculate price forecast
    this.calculatePriceForecast();
  }

  initializePriceForecast(): void {
    if (!this.calculation) {
      // Calculate first if not already calculated
      this.calculatePrice();
    }

    if (!this.calculation) return;

    const makePrice = this.calculation.breakdown.makingCost;
    const packagingCost = 6; // Default ₹16
    const onlineDeduction = 42; // Default 42%
    const onlineDiscount = 0; // Default 0%

    // Calculate suggested prices with 25% profit margin
    const suggestedPrice = this.calculation.breakdown.sellingPrice;
    const shopPrice = suggestedPrice;
    const shopDeliveryPrice = suggestedPrice + packagingCost;
    const onlinePrice = suggestedPrice + packagingCost;

    // Use service to calculate profits
    const profits = this.priceForecastService.calculateProfits({
      makePrice,
      packagingCost,
      onlineDeduction,
      onlineDiscount,
      shopPrice,
      shopDeliveryPrice,
      onlinePrice,
      updatedOnlinePrice: onlinePrice
    });

    this.priceForecast = {
      packagingCost,
      onlineDeduction,
      onlineDiscount,
      shopPrice,
      shopDeliveryPrice,
      onlinePrice,
      onlinePayout: profits.onlinePayout,
      onlineProfit: profits.onlineProfit,
      offlineProfit: profits.offlineProfit,
      takeawayProfit: profits.takeawayProfit
    };
  }

  calculatePriceForecast(): void {
    if (!this.calculation) return;

    const makePrice = this.calculation.breakdown.makingCost;
    const packagingCost = this.priceForecast?.packagingCost || 6; // Default ₹6
    const onlineDeduction = this.priceForecast?.onlineDeduction || 42; // Default 42%
    const onlineDiscount = this.priceForecast?.onlineDiscount || 0; // Default 0%

    // Calculate suggested prices with 25% profit margin
    const suggestedPrice = this.calculation.breakdown.sellingPrice;
    const shopPrice = this.priceForecast?.shopPrice || suggestedPrice;
    const shopDeliveryPrice = this.priceForecast?.shopDeliveryPrice || (suggestedPrice + packagingCost);
    const onlinePrice = this.priceForecast?.onlinePrice || (suggestedPrice + packagingCost);

    // Use service to calculate profits
    const profits = this.priceForecastService.calculateProfits({
      makePrice,
      packagingCost,
      onlineDeduction,
      onlineDiscount,
      shopPrice,
      shopDeliveryPrice,
      onlinePrice,
      updatedOnlinePrice: onlinePrice
    });

    this.priceForecast = {
      packagingCost,
      onlineDeduction,
      onlineDiscount,
      shopPrice,
      shopDeliveryPrice,
      onlinePrice,
      onlinePayout: profits.onlinePayout,
      onlineProfit: profits.onlineProfit,
      offlineProfit: profits.offlineProfit,
      takeawayProfit: profits.takeawayProfit,
      // Future prices
      futureShopPrice: this.priceForecast?.futureShopPrice || shopPrice,
      futureOnlinePrice: this.priceForecast?.futureOnlinePrice || onlinePrice,
      futureShopProfit: 0,
      futureOnlineProfit: 0
    };

    // Calculate future profits if future prices are set
    this.updateFutureProfits();
  }

  updateFutureProfits(): void {
    if (!this.priceForecast || !this.calculation) return;

    const makePrice = this.calculation.breakdown.makingCost;
    const packagingCost = this.priceForecast.packagingCost;

    // Calculate future shop profit (Shop Price - Making Price)
    if (this.priceForecast.futureShopPrice) {
      this.priceForecast.futureShopProfit = this.priceForecast.futureShopPrice - makePrice;
    }

    // Calculate future online profit using the same formula as current prices
    // Online Payout = ((Online Price + Packaging) - Discount%) - Deduction%
    // Online Profit = Online Payout - Making Price
    if (this.priceForecast.futureOnlinePrice) {
      const baseAmount = this.priceForecast.futureOnlinePrice + packagingCost;
      const discountAmount = (baseAmount * this.priceForecast.onlineDiscount) / 100;
      const afterDiscount = baseAmount - discountAmount;
      const deductionAmount = (afterDiscount * this.priceForecast.onlineDeduction) / 100;
      const futurePayout = Math.max(0, afterDiscount - deductionAmount);
      this.priceForecast.futureOnlineProfit = Math.max(0, futurePayout - makePrice);
    }
  }

  updateForecastCalculation(): void {
    if (this.priceForecast) {
      this.calculatePriceForecast();
      this.updateFutureProfits();
    }
  }

  calculateOilCost(): void {
    // Formula:
    // 1. Total oil cost = (oilCapacityLiters / 0.75) * oilPricePer750ml
    // 2. Total usage minutes = oilUsageDays * oilUsageHoursPerDay * 60
    // 3. Cost per minute = Total oil cost / Total usage minutes
    // 4. Item oil cost = Cost per minute * fryingTimeMinutes

    const totalOilCost = (this.oilCapacityLiters / 0.75) * this.oilPricePer750ml;
    const totalUsageMinutes = this.oilUsageDays * this.oilUsageHoursPerDay * 60;
    const costPerMinute = totalOilCost / totalUsageMinutes;
    this.calculatedOilCost = costPerMinute * this.fryingTimeMinutes;

    console.log('Oil Cost Calculation:', {
      totalOilCost: totalOilCost.toFixed(2),
      totalUsageMinutes,
      costPerMinute: costPerMinute.toFixed(4),
      fryingTimeMinutes: this.fryingTimeMinutes,
      calculatedOilCost: this.calculatedOilCost.toFixed(2)
    });
  }

  loadPriceForecastForMenuItem(): void {
    if (!this.currentRecipe.menuItemName) return;

    // Find menu item by name
    const menuItem = this.menuItems.find(m =>
      m.name.toLowerCase() === this.currentRecipe.menuItemName.toLowerCase()
    );

    if (!menuItem || !menuItem.id) return;

    // Get price forecasts for this menu item
    this.priceForecastService.getPriceForecastsByMenuItem(menuItem.id)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (forecasts) => {
          if (forecasts && forecasts.length > 0) {
            // Get the most recent forecast
            const latestForecast = forecasts.sort((a, b) =>
              new Date(b.lastUpdated).getTime() - new Date(a.lastUpdated).getTime()
            )[0];

            this.savedPriceForecast = latestForecast;

            // Pre-populate forecast values if they exist
            if (this.priceForecast) {
              this.priceForecast.packagingCost = latestForecast.packagingCost;
              this.priceForecast.onlineDeduction = latestForecast.onlineDeduction;
              this.priceForecast.onlineDiscount = latestForecast.onlineDiscount;
              this.priceForecast.shopPrice = latestForecast.shopPrice;
              this.priceForecast.shopDeliveryPrice = latestForecast.shopDeliveryPrice;
              this.priceForecast.onlinePrice = latestForecast.onlinePrice;
              this.updateForecastCalculation();
            }
          } else {
            this.savedPriceForecast = null;
          }
        },
        error: (err) => {
          console.error('Error loading price forecast:', err);
        }
      });
  }

  savePriceForecast(): void {
    if (!this.calculation || !this.priceForecast) {
      alert('Please calculate the recipe price first.');
      return;
    }

    if (!this.currentRecipe.menuItemName) {
      alert('Please enter a menu item name.');
      return;
    }

    // Find menu item - don't use empty string for missing ID
    let menuItem = this.menuItems.find(m =>
      m.name.toLowerCase() === this.currentRecipe.menuItemName.toLowerCase()
    );

    const makePrice = this.calculation.breakdown.makingCost;

    const forecastData: any = {
      menuItemName: this.currentRecipe.menuItemName,
      makePrice: makePrice,
      packagingCost: this.priceForecast.packagingCost,
      shopPrice: this.priceForecast.shopPrice,
      shopDeliveryPrice: this.priceForecast.shopDeliveryPrice,
      onlinePrice: this.priceForecast.onlinePrice,
      updatedShopPrice: this.priceForecast.shopPrice,
      updatedOnlinePrice: this.priceForecast.onlinePrice,
      onlineDeduction: this.priceForecast.onlineDeduction,
      onlineDiscount: this.priceForecast.onlineDiscount,
      payoutCalculation: this.priceForecast.onlinePayout,
      onlinePayout: this.priceForecast.onlinePayout,
      onlineProfit: this.priceForecast.onlineProfit,
      offlineProfit: this.priceForecast.offlineProfit,
      takeawayProfit: this.priceForecast.takeawayProfit,
      // Future pricing
      futureShopPrice: this.priceForecast.futureShopPrice,
      futureOnlinePrice: this.priceForecast.futureOnlinePrice,
      isFinalized: false,
      createdBy: 'Current User',
      lastUpdatedBy: 'Current User'
    };

    // Only include menuItemId if menu item exists
    if (menuItem?.id) {
      forecastData.menuItemId = menuItem.id;
    }

    if (this.savedPriceForecast && this.savedPriceForecast.id) {
      // Update existing forecast
      // Add to history if values changed
      const hasChanges =
        this.savedPriceForecast.makePrice !== makePrice ||
        this.savedPriceForecast.shopPrice !== this.priceForecast.shopPrice ||
        this.savedPriceForecast.onlinePrice !== this.priceForecast.onlinePrice ||
        this.savedPriceForecast.shopDeliveryPrice !== this.priceForecast.shopDeliveryPrice;

      if (hasChanges) {
        if (!this.priceChangeReason) {
          this.priceChangeReason = prompt('Please provide a reason for the price change:') || 'Price updated';
        }

        forecastData.history = this.savedPriceForecast.history || [];
        forecastData.history.push({
          changeDate: new Date().toISOString(),
          changedBy: 'Current User',
          makePrice: this.savedPriceForecast.makePrice,
          packagingCost: this.savedPriceForecast.packagingCost,
          shopPrice: this.savedPriceForecast.shopPrice,
          shopDeliveryPrice: this.savedPriceForecast.shopDeliveryPrice,
          onlinePrice: this.savedPriceForecast.onlinePrice,
          updatedShopPrice: this.savedPriceForecast.updatedShopPrice,
          updatedOnlinePrice: this.savedPriceForecast.updatedOnlinePrice,
          onlineDeduction: this.savedPriceForecast.onlineDeduction,
          onlineDiscount: this.savedPriceForecast.onlineDiscount,
          payoutCalculation: this.savedPriceForecast.payoutCalculation,
          onlinePayout: this.savedPriceForecast.onlinePayout,
          onlineProfit: this.savedPriceForecast.onlineProfit,
          offlineProfit: this.savedPriceForecast.offlineProfit,
          takeawayProfit: this.savedPriceForecast.takeawayProfit,
          changeReason: this.priceChangeReason
        });
      }

      this.priceForecastService.updatePriceForecast(this.savedPriceForecast.id, forecastData)
        .pipe(takeUntil(this.destroy$))
        .subscribe({
          next: (updated) => {
            this.savedPriceForecast = updated;
            this.priceChangeReason = '';

            // Update menu item prices
            this.updateMenuItemPricesFromForecast();

            this.showAlert('Price forecast updated successfully!', 'success');
          },
          error: (err) => {
            console.error('Error updating price forecast:', err);
            this.showAlert('Failed to update price forecast', 'error');
          }
        });
    } else {
      // Create new forecast
      forecastData.history = [];
      this.priceForecastService.createPriceForecast(forecastData)
        .pipe(takeUntil(this.destroy$))
        .subscribe({
          next: (created) => {
            this.savedPriceForecast = created;

            // Update menu item prices
            this.updateMenuItemPricesFromForecast();

            this.showAlert('Price forecast saved successfully!', 'success');
          },
          error: (err) => {
            console.error('Error creating price forecast:', err);
            this.showAlert('Failed to save price forecast', 'error');
          }
        });
    }
  }

  updateMenuItemPricesFromForecast(): void {
    if (!this.priceForecast || !this.calculation || !this.currentRecipe.menuItemName) return;

    // Find the menu item by name
    const menuItem = this.menuItems.find(m =>
      m.name.toLowerCase() === this.currentRecipe.menuItemName.toLowerCase()
    );

    // Prepare price data with forecast prices
    const priceData: any = {
      name: this.currentRecipe.menuItemName,
      makingPrice: this.calculation.breakdown.makingCost,
      onlinePrice: this.priceForecast.onlinePrice,
      dineInPrice: this.priceForecast.shopPrice,
      shopSellingPrice: this.priceForecast.shopPrice,
      packagingCharge: this.priceForecast.packagingCost,
      // Add future prices if set
      futureShopPrice: this.priceForecast.futureShopPrice,
      futureOnlinePrice: this.priceForecast.futureOnlinePrice
    };

    if (!menuItem || !menuItem.id) {
      // Menu item doesn't exist, create it
      console.log('Menu item not found, creating new menu item from forecast');

      // Set default category if available
      if (this.menuItems.length > 0 && this.menuItems[0].categoryId) {
        priceData.categoryId = this.menuItems[0].categoryId;
      }

      // Set default availability
      priceData.isAvailable = true;

      this.menuService.createMenuItem(priceData)
        .pipe(takeUntil(this.destroy$))
        .subscribe({
          next: (created) => {
            console.log('Menu item created successfully:', created);
            // Add to local menu items array
            this.menuItems.push(created);
            this.showAlert('Menu item created and prices synced!', 'success');
          },
          error: (err) => {
            console.error('Error creating menu item:', err);
            this.showAlert('Forecast saved but failed to create menu item', 'warning');
            // Reload to ensure consistency after error
            this.loadMenuItems();
          }
        });
      return;
    }

    // Menu item exists, update it with forecast prices
    const priceUpdate: any = {
      makingPrice: priceData.makingPrice,
      onlinePrice: priceData.onlinePrice,
      dineInPrice: priceData.dineInPrice,
      shopSellingPrice: priceData.shopSellingPrice,
      packagingCharge: priceData.packagingCharge
    };

    // Update the menu item
    this.menuService.updateMenuItem(menuItem.id, priceUpdate)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (updated) => {
          console.log('Menu item prices updated from forecast:', updated);
          // Update local menu items array with server response
          const index = this.menuItems.findIndex(m => m.id === menuItem.id);
          if (index !== -1) {
            this.menuItems[index] = { ...this.menuItems[index], ...updated };
          }
          this.showAlert('Menu prices synced successfully!', 'success');
        },
        error: (err) => {
          console.error('Error updating menu item prices:', err);
          this.showAlert('Forecast saved but failed to sync menu prices', 'warning');
          // Reload to ensure consistency after error
          this.loadMenuItems();
        }
      });
  }

  viewPriceHistory(): void {
    if (!this.savedPriceForecast || !this.savedPriceForecast.history || this.savedPriceForecast.history.length === 0) {
      alert('No price history available for this menu item.');
      return;
    }
    this.showForecastHistoryModal = true;
  }

  closePriceHistoryModal(): void {
    this.showForecastHistoryModal = false;
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

    console.log('Saving recipe:', this.currentRecipe);

    // Add oil usage data to recipe if frying time is specified
    if (this.fryingTimeMinutes > 0) {
      this.currentRecipe.oilUsage = {
        fryingTimeMinutes: this.fryingTimeMinutes,
        oilCapacityLiters: this.oilCapacityLiters,
        oilPricePer750ml: this.oilPricePer750ml,
        oilUsageDays: this.oilUsageDays,
        oilUsageHoursPerDay: this.oilUsageHoursPerDay,
        calculatedOilCost: this.calculatedOilCost
      };
      console.log('Oil usage data added:', this.currentRecipe.oilUsage);
    } else {
      // Clear oil usage if frying time is 0
      this.currentRecipe.oilUsage = undefined;
    }

    // Add preparation time to recipe
    this.currentRecipe.preparationTimeMinutes = this.preparationTimeMinutes;
    console.log('Preparation time added:', this.preparationTimeMinutes);

    // Add price forecast data to recipe if available
    if (this.priceForecast) {
      this.currentRecipe.priceForecast = {
        packagingCost: this.priceForecast.packagingCost,
        onlineDeduction: this.priceForecast.onlineDeduction,
        onlineDiscount: this.priceForecast.onlineDiscount,
        shopPrice: this.priceForecast.shopPrice,
        shopDeliveryPrice: this.priceForecast.shopDeliveryPrice,
        onlinePrice: this.priceForecast.onlinePrice,
        onlinePayout: this.priceForecast.onlinePayout,
        onlineProfit: this.priceForecast.onlineProfit,
        offlineProfit: this.priceForecast.offlineProfit,
        takeawayProfit: this.priceForecast.takeawayProfit,
        futureShopPrice: this.priceForecast.futureShopPrice,
        futureOnlinePrice: this.priceForecast.futureOnlinePrice,
        futureShopProfit: this.priceForecast.futureShopProfit,
        futureOnlineProfit: this.priceForecast.futureOnlineProfit
      };
      console.log('Price forecast data added:', this.currentRecipe.priceForecast);
    }

    // Add KPT data if available
    if (this.kptData) {
      this.currentRecipe.kptAnalysis = {
        avgPreparationTime: this.kptData.avgPreparationTime,
        minPreparationTime: this.kptData.minPreparationTime,
        maxPreparationTime: this.kptData.maxPreparationTime,
        medianPreparationTime: this.kptData.medianPreparationTime,
        stdDeviation: this.kptData.stdDeviation,
        orderCount: this.kptData.orderCount
      };
      console.log('KPT analysis data added:', this.currentRecipe.kptAnalysis);
    }

    console.log('Final recipe to save:', JSON.stringify(this.currentRecipe, null, 2));

    this.priceCalculatorService.saveRecipe(this.currentRecipe)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (savedRecipe) => {
          console.log('Recipe saved successfully:', savedRecipe);
          this.currentRecipe = savedRecipe;

          // Reload recipes to update the list
          this.loadRecipes();

          // Update menu item with calculated prices
          this.updateMenuItemPrices();

          // Save price forecast if present
          if (this.priceForecast && this.calculation) {
            this.savePriceForecast();
          }

          this.showAlert('Recipe saved successfully!', 'success');
        },
        error: (err) => {
          console.error('Error saving recipe:', err);
          this.showAlert('Failed to save recipe', 'error');
        }
      });
  }

  updateMenuItemPrices(): void {
    if (!this.calculation || !this.currentRecipe.menuItemName) return;

    // Find the menu item by name
    const menuItem = this.menuItems.find(m =>
      m.name.toLowerCase() === this.currentRecipe.menuItemName.toLowerCase()
    );

    // Prepare price data
    const priceData: any = {
      name: this.currentRecipe.menuItemName,
      makingPrice: this.calculation.breakdown.makingCost,
      onlinePrice: this.priceForecast?.onlinePrice || this.calculation.breakdown.sellingPrice,
      dineInPrice: this.priceForecast?.shopPrice || this.calculation.breakdown.sellingPrice,
      shopSellingPrice: this.priceForecast?.shopPrice || this.calculation.breakdown.sellingPrice,
      packagingCharge: this.priceForecast?.packagingCost || 0
    };

    if (!menuItem || !menuItem.id) {
      // Menu item doesn't exist, create it
      console.log('Menu item not found, creating new menu item');

      // Set default category if available
      if (this.menuItems.length > 0 && this.menuItems[0].categoryId) {
        priceData.categoryId = this.menuItems[0].categoryId;
      }

      // Set default availability
      priceData.isAvailable = true;

      this.menuService.createMenuItem(priceData)
        .pipe(takeUntil(this.destroy$))
        .subscribe({
          next: (created) => {
            console.log('Menu item created successfully:', created);
            // Add to local menu items array
            this.menuItems.push(created);
            this.showAlert('Menu item created and prices set!', 'success');
          },
          error: (err) => {
            console.error('Error creating menu item:', err);
            this.showAlert('Recipe saved but failed to create menu item', 'warning');
            // Reload to ensure consistency after error
            this.loadMenuItems();
          }
        });
      return;
    }

    // Menu item exists, update it
    const priceUpdate: any = {
      makingPrice: priceData.makingPrice,
      onlinePrice: priceData.onlinePrice,
      dineInPrice: priceData.dineInPrice,
      shopSellingPrice: priceData.shopSellingPrice,
      packagingCharge: priceData.packagingCharge
    };

    // Update the menu item
    this.menuService.updateMenuItem(menuItem.id, priceUpdate)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (updated) => {
          console.log('Menu item prices updated successfully:', updated);
          // Update local menu items array with server response
          const index = this.menuItems.findIndex(m => m.id === menuItem.id);
          if (index !== -1) {
            this.menuItems[index] = { ...this.menuItems[index], ...updated };
          }
          this.showAlert('Menu prices updated successfully!', 'success');
        },
        error: (err) => {
          console.error('Error updating menu item prices:', err);
          this.showAlert('Recipe saved but failed to update menu prices', 'warning');
          // Reload to ensure consistency after error
          this.loadMenuItems();
        }
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

  isMenuItemLinked(): boolean {
    if (!this.currentRecipe.menuItemName) return false;
    return this.menuItems.some(m =>
      m.name.toLowerCase() === this.currentRecipe.menuItemName.toLowerCase()
    );
  }

  isRecipeNotInMenu(recipe: MenuItemRecipe): boolean {
    if (!recipe.menuItemName) return false;
    return !this.menuItems.some(m =>
      m.name.toLowerCase() === recipe.menuItemName.toLowerCase()
    );
  }

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
