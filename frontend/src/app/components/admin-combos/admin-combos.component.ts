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

interface PricingSummary {
  totalMakingCost: number;
  avgShopPrice: number;
  avgOnlinePrice: number;
  totalPackaging: number;
  comboPrice: number;
  shopProfit: number;
  onlineProfit: number;
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

  comboForm: CreateComboRequest = this.getEmpty();

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

  getEmpty(): CreateComboRequest {
    return { name: '', description: '', items: [{ menuItemId: '', quantity: 1, shopPrice: 0, onlinePrice: 0, packagingCharge: 0 }], comboPrice: 0 };
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
        shopPrice: (i as any).shopPrice || 0,
        onlinePrice: (i as any).onlinePrice || i.originalPrice || 0,
        packagingCharge: (i as any).packagingCharge || 0
      })),
      comboPrice: c.comboPrice
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
    this.comboForm.items[index].menuItemId = item.id;
    this.comboForm.items[index].shopPrice = item.dineInPrice || item.shopSellingPrice || 0;
    this.comboForm.items[index].onlinePrice = item.onlinePrice || 0;
    this.comboForm.items[index].packagingCharge = item.packagingCharge || 0;
    this.showItemPickerAt = null;
    this.itemSearchFilter = '';
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
    let totalMaking = 0, totalShop = 0, totalOnline = 0, totalPackaging = 0, totalShopProfit = 0, totalOnlineProfit = 0;
    let count = 0;

    for (const item of this.comboForm.items) {
      if (!item.menuItemId) continue;
      const makingCost = this.getMakingCost(item.menuItemId) * item.quantity;
      const itemShopTotal = item.shopPrice * item.quantity;
      const itemOnlineTotal = item.onlinePrice * item.quantity;
      const itemPackagingTotal = item.packagingCharge * item.quantity;
      const itemOnlineProfit = makingCost - ((itemOnlineTotal + itemPackagingTotal) * 0.42);

      totalMaking += makingCost;
      totalShop += itemShopTotal;
      totalOnline += itemOnlineTotal;
      totalPackaging += itemPackagingTotal;
      totalShopProfit += itemShopTotal - makingCost;
      totalOnlineProfit += itemOnlineProfit;
      count++;
    }

    return {
      totalMakingCost: totalMaking,
      avgShopPrice: count > 0 ? totalShop / count : 0,
      avgOnlinePrice: count > 0 ? totalOnline / count : 0,
      totalPackaging,
      comboPrice: this.comboForm.comboPrice,
      shopProfit: totalShopProfit,
      onlineProfit: totalOnlineProfit
    };
  }

  addItem() {
    this.comboForm.items.push({ menuItemId: '', quantity: 1, shopPrice: 0, onlinePrice: 0, packagingCharge: 0 });
  }

  getSelectedItemCount(): number {
    return this.comboForm.items.filter(item => !!item.menuItemId?.trim()).length;
  }

  getItemProfit(index: number): { shopProfit: number; onlineProfit: number } {
    const item = this.comboForm.items[index];
    if (!item.menuItemId) return { shopProfit: 0, onlineProfit: 0 };
    const makingCost = this.getMakingCost(item.menuItemId);
    const totalMakingCost = makingCost * item.quantity;
    const shopProfit = (item.shopPrice * item.quantity) - totalMakingCost;
    const onlineSellingPriceTotal = item.onlinePrice * item.quantity;
    const packagingTotal = item.packagingCharge * item.quantity;
    const onlineProfit = totalMakingCost - ((onlineSellingPriceTotal + packagingTotal) * 0.42);
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

    const payload: CreateComboRequest = { ...this.comboForm, items: validItems };

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
