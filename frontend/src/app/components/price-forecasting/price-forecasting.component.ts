import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { PriceForecastService, PriceForecast, PriceHistory } from '../../services/price-forecast.service';
import { MenuService, MenuItem } from '../../services/menu.service';
import { DiscountCouponService, DiscountCoupon } from '../../services/discount-coupon.service';

@Component({
  selector: 'app-price-forecasting',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './price-forecasting.component.html',
  styleUrls: ['./price-forecasting.component.scss']
})
export class PriceForecastingComponent implements OnInit {
  forecasts: PriceForecast[] = [];
  menuItems: MenuItem[] = [];
  activeCoupons: DiscountCoupon[] = [];
  selectedCouponId: string = '';
  discountWarning: string = '';
  loading = true;
  showModal = false;
  showHistoryModal = false;
  isEditMode = false;
  currentForecast: PriceForecast | null = null;
  selectedHistory: PriceHistory[] = [];

  forecastForm: Partial<PriceForecast> = this.getEmptyForecast();

  constructor(
    private forecastService: PriceForecastService,
    private menuService: MenuService,
    private couponService: DiscountCouponService
  ) {}

  ngOnInit() {
    this.loadMenuItems();
    this.loadForecasts();
    this.loadActiveCoupons();
  }

  loadActiveCoupons() {
    this.couponService.getActiveCoupons().subscribe({
      next: (response) => {
        if (response.success) {
          this.activeCoupons = response.data || [];
        }
      },
      error: (err) => {
        console.error('Error loading active coupons:', err);
      }
    });
  }

  loadMenuItems() {
    this.menuService.getMenuItems().subscribe({
      next: (items) => {
        this.menuItems = items;
      },
      error: (err) => {
        console.error('Error loading menu items:', err);
        alert('Failed to load menu items');
      }
    });
  }

  loadForecasts() {
    this.loading = true;
    this.forecastService.getPriceForecasts().subscribe({
      next: (forecasts) => {
        this.forecasts = forecasts;
        this.loading = false;
      },
      error: (err) => {
        console.error('Error loading price forecasts:', err);
        alert('Failed to load price forecasts');
        this.loading = false;
      }
    });
  }

  getEmptyForecast(): Partial<PriceForecast> {
    return {
      menuItemId: '',
      menuItemName: '',
      makePrice: 0,
      packagingCost: 0,
      shopPrice: 0,
      shopDeliveryPrice: 0,
      onlinePrice: 0,
      updatedShopPrice: 0,
      updatedOnlinePrice: 0,
      onlineDeduction: 42, // Default 42% deduction for online orders
      onlineDiscount: 0,
      payoutCalculation: 0,
      onlinePayout: 0,
      onlineProfit: 0,
      offlineProfit: 0,
      takeawayProfit: 0,
      isFinalized: false,
      history: []
    };
  }

  openCreateModal() {
    this.isEditMode = false;
    this.forecastForm = this.getEmptyForecast();
    this.selectedCouponId = '';
    this.discountWarning = '';
    this.showModal = true;
  }

  openEditModal(forecast: PriceForecast) {
    if (forecast.isFinalized) {
      alert('Cannot edit finalized forecast');
      return;
    }
    this.isEditMode = true;
    this.currentForecast = forecast;
    this.forecastForm = { ...forecast };
    this.selectedCouponId = '';
    this.discountWarning = '';
    this.showModal = true;
  }

  closeModal() {
    this.showModal = false;
    this.currentForecast = null;
    this.forecastForm = this.getEmptyForecast();
    this.selectedCouponId = '';
    this.discountWarning = '';
  }

  onMenuItemChange() {
    const selectedItem = this.menuItems.find(item => item.id === this.forecastForm.menuItemId);
    if (selectedItem) {
      this.forecastForm.menuItemName = selectedItem.name;
      // Populate pricing fields from existing menu item
      this.forecastForm.makePrice = selectedItem.makingPrice || 0;
      this.forecastForm.packagingCost = selectedItem.packagingCharge || 0;
      this.forecastForm.shopPrice = selectedItem.shopSellingPrice || 0;
      // Sync Takeaway price with Shop price
      this.forecastForm.shopDeliveryPrice = this.forecastForm.shopPrice;
      this.forecastForm.onlinePrice = selectedItem.onlinePrice || 0;
      // Set default 42% online deduction if not already set
      if (!this.isEditMode) {
        this.forecastForm.onlineDeduction = 42;
      }
      // Calculate payout with populated values
      this.onPriceChange();
    }
  }

  onShopPriceChange() {
    // Auto-sync Takeaway price with Shop price
    this.forecastForm.shopDeliveryPrice = this.forecastForm.shopPrice;
    this.onPriceChange();
  }

  onDiscountCouponChange() {
    this.discountWarning = '';

    if (!this.selectedCouponId || this.selectedCouponId === 'none') {
      this.forecastForm.onlineDiscount = 0;
      this.onPriceChange();
      return;
    }

    // Find the selected coupon by ID
    const selectedCoupon = this.activeCoupons.find(c => c.id === this.selectedCouponId);

    if (!selectedCoupon) {
      this.forecastForm.onlineDiscount = 0;
      this.onPriceChange();
      return;
    }

    // If discount percentage is set on the coupon, use it directly
    if (selectedCoupon.discountPercentage !== undefined && selectedCoupon.discountPercentage !== null) {
      this.forecastForm.onlineDiscount = selectedCoupon.discountPercentage;
      this.discountWarning = `✓ Using fixed discount percentage: ${selectedCoupon.discountPercentage}%`;
      this.onPriceChange();
      return;
    }

    // Otherwise, calculate based on average discount amount
    const basePrice = this.forecastForm.updatedOnlinePrice || this.forecastForm.onlinePrice || 0;
    const onlinePrice = basePrice + (this.forecastForm.packagingCost || 0);

    if (onlinePrice === 0) {
      this.forecastForm.onlineDiscount = 0;
      this.discountWarning = `⚠️ Please set online price first to calculate discount`;
      this.onPriceChange();
      return;
    }

    // Use average discount as the base discount amount
    let discountAmount = selectedCoupon.averageDiscountAmount || 0;

    // Apply max value validation
    if (selectedCoupon.maxValue && discountAmount > selectedCoupon.maxValue) {
      discountAmount = selectedCoupon.maxValue;
      this.discountWarning = `⚠️ Discount capped at ₹${selectedCoupon.maxValue.toFixed(2)} (max value for ${selectedCoupon.couponCode})`;
    } else if (selectedCoupon.maxValue) {
      this.discountWarning = `✓ Discount of ₹${discountAmount.toFixed(2)} is within the max value of ₹${selectedCoupon.maxValue.toFixed(2)}`;
    }

    // Calculate discount percentage based on online price
    this.forecastForm.onlineDiscount = (discountAmount / onlinePrice) * 100;

    this.onPriceChange();
  }

  onPriceChange() {
    // Revalidate discount if a coupon is selected
    if (this.selectedCouponId && this.selectedCouponId !== 'none') {
      const selectedCoupon = this.activeCoupons.find(c => c.id === this.selectedCouponId);

      if (selectedCoupon) {
        // If discount percentage is set on the coupon, use it directly (don't recalculate)
        if (selectedCoupon.discountPercentage !== undefined && selectedCoupon.discountPercentage !== null) {
          this.forecastForm.onlineDiscount = selectedCoupon.discountPercentage;
          this.discountWarning = `✓ Using fixed discount percentage: ${selectedCoupon.discountPercentage}%`;
        } else {
          // Recalculate based on average discount amount
          const basePrice = this.forecastForm.updatedOnlinePrice || this.forecastForm.onlinePrice || 0;
          const onlinePrice = basePrice + (this.forecastForm.packagingCost || 0);

          if (onlinePrice > 0) {
            // Recalculate discount percentage based on the coupon's average discount
            let discountAmount = selectedCoupon.averageDiscountAmount || 0;

            // Apply max value validation
            if (selectedCoupon.maxValue && discountAmount > selectedCoupon.maxValue) {
              discountAmount = selectedCoupon.maxValue;
              this.discountWarning = `⚠️ Discount capped at ₹${selectedCoupon.maxValue.toFixed(2)} (max value for ${selectedCoupon.couponCode})`;
            } else if (selectedCoupon.maxValue) {
              const currentDiscountAmount = (onlinePrice * (this.forecastForm.onlineDiscount || 0)) / 100;
              this.discountWarning = `✓ Discount of ₹${currentDiscountAmount.toFixed(2)} is within the max value of ₹${selectedCoupon.maxValue.toFixed(2)}`;
            }

            // Recalculate percentage based on new price
            this.forecastForm.onlineDiscount = (discountAmount / onlinePrice) * 100;
          } else {
            this.forecastForm.onlineDiscount = 0;
            this.discountWarning = `⚠️ Please set online price first to calculate discount`;
          }
        }
      }
    }

    this.forecastForm.payoutCalculation = this.forecastService.calculatePayout(this.forecastForm);
    const profits = this.forecastService.calculateProfits(this.forecastForm);
    this.forecastForm.onlinePayout = profits.onlinePayout;
    this.forecastForm.onlineProfit = profits.onlineProfit;
    this.forecastForm.offlineProfit = profits.offlineProfit;
    this.forecastForm.takeawayProfit = profits.takeawayProfit;
  }

  saveForecast() {
    if (!this.forecastForm.menuItemId) {
      alert('Please select a menu item');
      return;
    }

    // Calculate payout and profits before saving
    this.forecastForm.payoutCalculation = this.forecastService.calculatePayout(this.forecastForm);
    const profits = this.forecastService.calculateProfits(this.forecastForm);
    this.forecastForm.onlinePayout = profits.onlinePayout;
    this.forecastForm.onlineProfit = profits.onlineProfit;
    this.forecastForm.offlineProfit = profits.offlineProfit;
    this.forecastForm.takeawayProfit = profits.takeawayProfit;

    if (this.isEditMode && this.currentForecast?.id) {
      this.forecastService.updatePriceForecast(this.currentForecast.id, this.forecastForm as PriceForecast).subscribe({
        next: () => {
          alert('Price forecast updated successfully!');
          this.loadForecasts();
          this.closeModal();
        },
        error: (err) => {
          console.error('Error updating price forecast:', err);
          alert('Failed to update price forecast: ' + (err.error?.error || err.message));
        }
      });
    } else {
      this.forecastService.createPriceForecast(this.forecastForm as PriceForecast).subscribe({
        next: () => {
          alert('Price forecast created successfully!');
          this.loadForecasts();
          this.closeModal();
        },
        error: (err) => {
          console.error('Error creating price forecast:', err);
          alert('Failed to create price forecast: ' + (err.error?.error || err.message));
        }
      });
    }
  }

  deleteForecast(id: string, isFinalized: boolean) {
    if (isFinalized) {
      alert('Cannot delete finalized forecast');
      return;
    }

    if (confirm('Are you sure you want to delete this price forecast?')) {
      this.forecastService.deletePriceForecast(id).subscribe({
        next: () => {
          alert('Price forecast deleted successfully!');
          this.loadForecasts();
        },
        error: (err) => {
          console.error('Error deleting price forecast:', err);
          alert('Failed to delete price forecast: ' + (err.error?.error || err.message));
        }
      });
    }
  }

  finalizeForecast(id: string) {
    if (confirm('Are you sure you want to finalize this price forecast? This will update the menu item prices and cannot be undone.')) {
      this.forecastService.finalizePriceForecast(id).subscribe({
        next: () => {
          alert('Price forecast finalized successfully! Menu item prices have been updated.');
          this.loadForecasts();
        },
        error: (err) => {
          console.error('Error finalizing price forecast:', err);
          alert('Failed to finalize price forecast: ' + (err.error?.error || err.message));
        }
      });
    }
  }

  showHistory(forecast: PriceForecast) {
    this.selectedHistory = forecast.history || [];
    this.showHistoryModal = true;
  }

  closeHistoryModal() {
    this.showHistoryModal = false;
    this.selectedHistory = [];
  }

  formatDate(date: string): string {
    return new Date(date).toLocaleString('en-IN', {
      year: 'numeric',
      month: 'short',
      day: 'numeric',
      hour: '2-digit',
      minute: '2-digit'
    });
  }

  getStatusBadgeClass(isFinalized: boolean): string {
    return isFinalized ? 'badge-success' : 'badge-warning';
  }

  getStatusText(isFinalized: boolean): string {
    return isFinalized ? 'Finalized' : 'Draft';
  }
}
