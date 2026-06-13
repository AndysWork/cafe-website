import { Component, OnInit, OnDestroy, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ComboMealService, ComboMeal, CreateComboRequest } from '../../services/combo-meal.service';
import { MenuService, MenuItem } from '../../services/menu.service';
import { PriceCalculatorService } from '../../services/price-calculator.service';
import { OutletService } from '../../services/outlet.service';
import { UIStore } from '../../store/ui.store';
import { Subscription } from 'rxjs';
import { filter } from 'rxjs/operators';

interface ComboItemDetail {
  menuItemId: string;
  quantity: number;
  shopPrice: number;
  onlinePrice: number;
  packagingCharge: number;
  menuItem?: MenuItem;
}

interface ComboFormItem {
  menuItemId: string;
  quantity: number;
  selectedPieces: number;
  basePieces: number;
  shopPrice: number;
  onlinePrice: number;
  packagingCharge: number;
}

interface ComboFormModel {
  name: string;
  description?: string;
  items: ComboFormItem[];
  comboPrice: number;
  comboOnlinePrice: number;
  imageUrl?: string;
  validFrom?: string;
  validTill?: string;
}

interface PricingSummary {
  totalMakingCost: number;
  comboPackagingPrice: number;
  comboShopPrice: number;
  comboTakeawayPrice: number;
  comboOnlinePrice: number;
  comboShopProfit: number;
  comboTakeawayProfit: number;
  comboOnlineProfit: number;
}

@Component({
  selector: 'app-admin-combos',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './admin-combos.component.html',
  styleUrls: ['./admin-combos.component.scss']
})
export class AdminCombosComponent implements OnInit, OnDestroy {
  private outletService = inject(OutletService);
  private uiStore = inject(UIStore);
  private menuService = inject(MenuService);
  private priceService = inject(PriceCalculatorService);
  private outletSub?: Subscription;

  combos: ComboMeal[] = [];
  menuItems: MenuItem[] = [];
  recipes: any[] = [];
  recipeMap = new Map<string, any>();

  loading = true;
  showModal = false;
  isEditMode = false;
  currentCombo: ComboMeal | null = null;

  comboForm: ComboFormModel = this.getEmpty();

  // For menu item picker dropdown
  showItemPickerAt: number | null = null;
  itemSearchFilter = '';
  itemPickerFiltered: MenuItem[] = [];

  constructor(private comboService: ComboMealService) {}

  ngOnInit() {
    this.outletSub = this.outletService.selectedOutlet$
      .pipe(filter(o => o !== null))
      .subscribe(() => this.loadCombos());
    if (this.outletService.getSelectedOutlet()) this.loadCombos();
    this.loadMenuItems();
    this.loadRecipes();
  }

  ngOnDestroy() { this.outletSub?.unsubscribe(); }

  loadMenuItems(): void {
    this.menuService.getMenuItems().subscribe({
      next: items => this.menuItems = items,
      error: () => console.warn('Failed to load menu items')
    });
  }

  loadRecipes(): void {
    this.priceService.getRecipes().subscribe({
      next: recipes => {
        this.recipes = recipes;
        this.recipeMap.clear();
        for (const r of recipes) {
          if (r.menuItemId) this.recipeMap.set(r.menuItemId, r);
          if (r.menuItemName) this.recipeMap.set(r.menuItemName.toLowerCase(), r);
        }
      },
      error: () => console.warn('Failed to load recipes')
    });
  }

  getEmpty(): ComboFormModel {
    return {
      name: '',
      description: '',
      items: [{ menuItemId: '', quantity: 1, selectedPieces: 1, basePieces: 1, shopPrice: 0, onlinePrice: 0, packagingCharge: 0 }],
      comboPrice: 0,
      comboOnlinePrice: 0
    };
  }

  loadCombos() {
    this.loading = true;
    this.comboService.getAllCombos().subscribe({
      next: c => { this.combos = c; this.loading = false; },
      error: () => { this.uiStore.error('Failed to load combos'); this.loading = false; }
    });
  }

  openCreateModal() {
    this.isEditMode = false;
    this.comboForm = this.getEmpty();
    this.showModal = true;
    this.showItemPickerAt = null;
    this.itemSearchFilter = '';
  }

  openEditModal(c: ComboMeal) {
    this.isEditMode = true;
    this.currentCombo = c;
    this.comboForm = {
      name: c.name,
      description: c.description,
      items: c.items.map(i => ({
        menuItemId: i.menuItemId,
        quantity: i.quantity,
        selectedPieces: (i as any).selectedPieces || (i as any).basePieces || this.getMenuItemBasePieces(i.menuItemId),
        basePieces: (i as any).basePieces || this.getMenuItemBasePieces(i.menuItemId),
        shopPrice: (i as any).shopPrice || 0,
        onlinePrice: (i as any).onlinePrice || i.originalPrice || 0,
        packagingCharge: (i as any).packagingCharge || 0
      })),
      comboPrice: c.comboPrice,
      comboOnlinePrice: c.comboOnlinePrice ?? c.comboPrice
    };
    this.showModal = true;
    this.showItemPickerAt = null;
    this.itemSearchFilter = '';
  }

  closeModal() {
    this.showModal = false;
    this.currentCombo = null;
    this.showItemPickerAt = null;
  }

  openItemPicker(index: number): void {
    this.showItemPickerAt = this.showItemPickerAt === index ? null : index;
    this.itemSearchFilter = '';
    this.updateFilteredItems();
  }

  updateFilteredItems(): void {
    const filter = this.itemSearchFilter.toLowerCase();
    this.itemPickerFiltered = this.menuItems.filter(m =>
      m.name.toLowerCase().includes(filter) ||
      m.id.toLowerCase().includes(filter) ||
      (m.categoryName && m.categoryName.toLowerCase().includes(filter))
    );
  }

  selectMenuItem(item: MenuItem, index: number): void {
    const basePieces = this.getMenuItemBasePiecesFromMenuItem(item);
    this.comboForm.items[index].menuItemId = item.id;
    this.comboForm.items[index].basePieces = basePieces;
    this.comboForm.items[index].selectedPieces = basePieces;
    this.comboForm.items[index].shopPrice = item.dineInPrice || item.shopSellingPrice || 0;
    this.comboForm.items[index].onlinePrice = item.onlinePrice || 0;
    this.comboForm.items[index].packagingCharge = item.packagingCharge || 0;
    this.showItemPickerAt = null;
    this.itemSearchFilter = '';
  }

  getMenuItemBasePiecesFromMenuItem(item?: MenuItem): number {
    if (!item?.quantity || item.quantity < 1) return 1;
    return Math.floor(item.quantity);
  }

  getMenuItemBasePieces(menuItemId: string): number {
    const item = this.getMenuItemDetail(menuItemId);
    return this.getMenuItemBasePiecesFromMenuItem(item);
  }

  getEffectiveMultiplier(item: ComboFormItem): number {
    const basePieces = item.basePieces > 0 ? item.basePieces : this.getMenuItemBasePieces(item.menuItemId);
    const selectedPieces = Math.max(1, Math.floor(item.selectedPieces || basePieces));
    return item.quantity * (selectedPieces / basePieces);
  }

  onSelectedPiecesChange(item: ComboFormItem): void {
    const basePieces = item.basePieces > 0 ? item.basePieces : this.getMenuItemBasePieces(item.menuItemId);
    if (!Number.isFinite(item.selectedPieces)) {
      item.selectedPieces = basePieces;
      return;
    }

    const normalized = Math.floor(item.selectedPieces);
    if (normalized < 1) {
      item.selectedPieces = 1;
      return;
    }

    if (normalized > basePieces) {
      item.selectedPieces = basePieces;
      return;
    }

    item.selectedPieces = normalized;
  }

  getMenuItemName(menuItemId: string): string {
    return this.menuItems.find(m => m.id === menuItemId)?.name || 'Unknown Item';
  }

  getMenuItemDetail(menuItemId: string): MenuItem | undefined {
    return this.menuItems.find(m => m.id === menuItemId);
  }

  getMakingCost(menuItemId: string): number {
    const recipe = this.recipeMap.get(menuItemId);
    if (recipe) return recipe.totalMakingCost || 0;
    return 0;
  }

  getItemSnapshot(item: ComboFormItem): {
    makingCost: number;
    shopPrice: number;
    onlinePrice: number;
    packagingCost: number;
  } {
    const menuItem = this.getMenuItemDetail(item.menuItemId);
    const unitShopPrice = item.shopPrice || menuItem?.dineInPrice || menuItem?.shopSellingPrice || 0;
    const unitOnlinePrice = item.onlinePrice || menuItem?.onlinePrice || 0;
    const unitPackaging = item.packagingCharge || menuItem?.packagingCharge || 0;
    const unitMakingCost = this.getMakingCost(item.menuItemId);
    const multiplier = this.getEffectiveMultiplier(item);

    return {
      makingCost: unitMakingCost * multiplier,
      shopPrice: unitShopPrice * multiplier,
      onlinePrice: unitOnlinePrice * multiplier,
      packagingCost: unitPackaging * multiplier
    };
  }

  calculateItemPricings(menuItemId: string, quantity: number): {
    makingCost: number;
    shopPrice: number;
    onlinePrice: number;
    packaging: number;
    shopProfit: number;
    onlineProfit: number;
  } {
    const item = this.getMenuItemDetail(menuItemId);
    const makingCost = this.getMakingCost(menuItemId);

    return {
      makingCost: makingCost * quantity,
      shopPrice: (item?.dineInPrice || item?.shopSellingPrice || 0) * quantity,
      onlinePrice: (item?.onlinePrice || 0) * quantity,
      packaging: (item?.packagingCharge || 0) * quantity,
      shopProfit: ((item?.dineInPrice || item?.shopSellingPrice || 0) - makingCost) * quantity,
      onlineProfit: ((item?.onlinePrice || 0) - makingCost - (item?.packagingCharge || 0)) * quantity
    };
  }

  calculateItemEffectiveComboProfit(menuItemId: string, quantity: number): {
    futureShopPrice: number;
    futureOnlinePrice: number;
    futureShopProfit: number;
    futureOnlineProfit: number;
  } {
    const item = this.getMenuItemDetail(menuItemId);
    const makingCost = this.getMakingCost(menuItemId);

    // Calculate total individual value
    let totalIndividualValue = 0;
    for (const comboItem of this.comboForm.items) {
      if (!comboItem.menuItemId) continue;
      const m = this.getMenuItemDetail(comboItem.menuItemId);
      const shopVal = (m?.dineInPrice || m?.shopSellingPrice || 0) * comboItem.quantity;
      totalIndividualValue += shopVal;
    }

    // Allocate combo price proportionally
    const thisItemIndividualValue = (item?.dineInPrice || item?.shopSellingPrice || 0) * quantity;
    const ratio = totalIndividualValue > 0 ? this.comboForm.comboPrice / totalIndividualValue : 0;
    const futureShopPrice = thisItemIndividualValue * ratio;
    const futureOnlinePrice = (item?.onlinePrice || 0) * quantity * ratio;
    const futureShopProfit = futureShopPrice - (makingCost * quantity);
    const futureOnlineProfit = futureOnlinePrice - (makingCost * quantity) - ((item?.packagingCharge || 0) * quantity);

    return { futureShopPrice, futureOnlinePrice, futureShopProfit, futureOnlineProfit };
  }

  calculateComboPricings(): PricingSummary {
    let totalMaking = 0;
    let totalPackaging = 0;

    for (const item of this.comboForm.items) {
      if (!item.menuItemId) continue;
      const snapshot = this.getItemSnapshot(item);
      totalMaking += snapshot.makingCost;
      totalPackaging += snapshot.packagingCost;
    }

    const comboShopPrice = this.comboForm.comboPrice || 0;
    const comboOnlinePrice = this.comboForm.comboOnlinePrice || 0;
    const comboTakeawayPrice = comboShopPrice + totalPackaging;

    const comboShopProfit = comboShopPrice - totalMaking;
    const comboTakeawayProfit = (comboShopPrice + totalPackaging) - totalMaking;
    const comboOnlineProfit = ((comboOnlinePrice + totalPackaging) * 0.58) - totalMaking;

    return {
      totalMakingCost: totalMaking,
      comboPackagingPrice: totalPackaging,
      comboShopPrice,
      comboTakeawayPrice,
      comboOnlinePrice,
      comboShopProfit,
      comboTakeawayProfit,
      comboOnlineProfit
    };
  }

  addItem() {
    this.comboForm.items.push({ menuItemId: '', quantity: 1, selectedPieces: 1, basePieces: 1, shopPrice: 0, onlinePrice: 0, packagingCharge: 0 });
  }

  getSelectedItemCount(): number {
    return this.comboForm.items.filter(item => !!item.menuItemId?.trim()).length;
  }

  getItemProfit(index: number): { shopProfit: number; onlineProfit: number } {
    const item = this.comboForm.items[index];
    if (!item.menuItemId) return { shopProfit: 0, onlineProfit: 0 };
    const snapshot = this.getItemSnapshot(item);
    const shopProfit = snapshot.shopPrice - snapshot.makingCost;
    const onlineProfit = ((snapshot.onlinePrice + snapshot.packagingCost) * 0.58) - snapshot.makingCost;
    return { shopProfit, onlineProfit };
  }

  removeItem(i: number) {
    this.comboForm.items.splice(i, 1);
  }

  saveCombo() {
    if (!this.comboForm.name?.trim()) {
      this.uiStore.error('Combo name is required');
      return;
    }

    const validItems = this.comboForm.items.filter(i => i.menuItemId?.trim());
    if (validItems.length === 0) {
      this.uiStore.error('Add at least one menu item to the combo');
      return;
    }
    if (validItems.length < 2) {
      this.uiStore.error('A combo must have at least 2 items');
      return;
    }

    const normalizedIds = validItems.map(i => i.menuItemId.trim());
    if (new Set(normalizedIds).size !== normalizedIds.length) {
      this.uiStore.error('Duplicate menu items are not allowed in a combo');
      return;
    }

    if (validItems.some(i => !Number.isInteger(i.quantity) || i.quantity < 1 || i.quantity > 10)) {
      this.uiStore.error('Each combo item quantity must be between 1 and 10');
      return;
    }

    if (validItems.some(i => !Number.isInteger(i.selectedPieces) || i.selectedPieces < 1)) {
      this.uiStore.error('Selected pieces must be a whole number greater than or equal to 1');
      return;
    }

    const invalidPiecesItem = validItems.find(i => {
      const base = i.basePieces > 0 ? i.basePieces : this.getMenuItemBasePieces(i.menuItemId.trim());
      return i.selectedPieces > base;
    });
    if (invalidPiecesItem) {
      this.uiStore.error('Selected pieces cannot be greater than total pieces in the dish');
      return;
    }

    if (this.comboForm.comboPrice < 0) {
      this.uiStore.error('Combo price cannot be negative');
      return;
    }

    if (this.comboForm.comboOnlinePrice < 0) {
      this.uiStore.error('Combo online price cannot be negative');
      return;
    }

    const totalOriginalPrice = validItems.reduce((sum, i) => {
      const menuItem = this.getMenuItemDetail(i.menuItemId.trim());
      return sum + (menuItem?.onlinePrice || 0) * this.getEffectiveMultiplier(i);
    }, 0);

    if (this.comboForm.comboPrice > totalOriginalPrice) {
      this.uiStore.error('Combo price cannot be greater than the total original price');
      return;
    }

    if (this.comboForm.validFrom && this.comboForm.validTill) {
      const validFrom = new Date(this.comboForm.validFrom).getTime();
      const validTill = new Date(this.comboForm.validTill).getTime();
      if (!Number.isNaN(validFrom) && !Number.isNaN(validTill) && validFrom > validTill) {
        this.uiStore.error('validFrom cannot be later than validTill');
        return;
      }
    }

    const payload: CreateComboRequest = {
      name: this.comboForm.name,
      description: this.comboForm.description,
      comboPrice: this.comboForm.comboPrice,
      comboOnlinePrice: this.comboForm.comboOnlinePrice,
      imageUrl: this.comboForm.imageUrl,
      validFrom: this.comboForm.validFrom,
      validTill: this.comboForm.validTill,
      items: validItems.map(i => ({ menuItemId: i.menuItemId.trim(), quantity: i.quantity, selectedPieces: i.selectedPieces }))
    };

    if (this.isEditMode && this.currentCombo?.id) {
      this.comboService.updateCombo(this.currentCombo.id, payload).subscribe({
        next: () => { this.uiStore.success('Combo updated'); this.loadCombos(); this.closeModal(); },
        error: (err) => this.uiStore.error(err?.message || 'Failed to update combo')
      });
    } else {
      this.comboService.createCombo(payload).subscribe({
        next: () => { this.uiStore.success('Combo created'); this.loadCombos(); this.closeModal(); },
        error: (err) => this.uiStore.error(err?.message || 'Failed to create combo')
      });
    }
  }

  deleteCombo(id: string) {
    if (!confirm('Delete this combo?')) return;
    this.comboService.deleteCombo(id).subscribe({
      next: () => { this.uiStore.success('Combo deleted'); this.loadCombos(); },
      error: () => this.uiStore.error('Failed to delete combo')
    });
  }

  trackById(_: number, item: ComboMeal) { return item.id; }
  trackByIndex(i: number) { return i; }
}
