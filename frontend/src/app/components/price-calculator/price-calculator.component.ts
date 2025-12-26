import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Subject, takeUntil } from 'rxjs';
import { PriceCalculatorService } from '../../services/price-calculator.service';
import { MenuService, MenuItem } from '../../services/menu.service';
import {
  Ingredient,
  MenuItemRecipe,
  IngredientUsage,
  PriceCalculation,
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

  // UI State
  activeTab: 'calculator' | 'ingredients' | 'recipes' = 'calculator';

  // Ingredient Management
  showIngredientModal = false;
  editingIngredient: Ingredient | null = null;
  ingredientForm: Ingredient = this.getEmptyIngredient();
  ingredientSearchTerm = '';
  selectedCategory = '';

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
  ingredientQuantity = 0;
  ingredientUnit: 'kg' | 'gm' | 'ml' | 'pc' | 'ltr' = 'gm';
  calculation: PriceCalculation | null = null;

  // Constants for templates
  categories = INGREDIENT_CATEGORIES;
  units = MEASUREMENT_UNITS;
  measurementUnits = MEASUREMENT_UNITS;

  constructor(
    private priceCalculatorService: PriceCalculatorService,
    private menuService: MenuService
  ) {}

  ngOnInit(): void {
    this.loadIngredients();
    this.loadRecipes();
    this.loadMenuItems();
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
    return this.ingredients.filter(ing => {
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
              `✅ ${ingredient.name}: Price updated to ₹${result.data?.ingredient?.marketPrice || 0} (${changePercent > 0 ? '+' : ''}${changePercent?.toFixed(2)}%)`,
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

    const cost = this.priceCalculatorService.calculateIngredientCost(
      this.ingredientQuantity,
      this.ingredientUnit,
      this.selectedIngredient
    );

    const usage: IngredientUsage = {
      ingredientId: this.selectedIngredient.id!,
      ingredientName: this.selectedIngredient.name,
      quantity: this.ingredientQuantity,
      unit: this.ingredientUnit,
      unitPrice: this.selectedIngredient.marketPrice,
      totalCost: cost
    };

    this.currentRecipe.ingredients.push(usage);

    // Reset form
    this.selectedIngredient = null;
    this.ingredientQuantity = 0;
    this.ingredientUnit = 'gm';

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
    this.selectedIngredient = this.ingredients.find(ing => ing.id === ingredientId) || null;
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
}

