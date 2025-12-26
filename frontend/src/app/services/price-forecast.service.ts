import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';

export interface PriceHistory {
  changeDate: string;
  changedBy: string;
  makePrice: number;
  packagingCost: number;
  shopPrice: number;
  shopDeliveryPrice: number;
  onlinePrice: number;
  updatedShopPrice: number;
  updatedOnlinePrice: number;
  onlineDeduction: number;
  onlineDiscount: number;
  payoutCalculation: number;
  onlinePayout: number;
  onlineProfit: number;
  offlineProfit: number;
  takeawayProfit: number;
  changeReason: string;
}

export interface PriceForecast {
  id?: string;
  menuItemId: string;
  menuItemName: string;
  makePrice: number;
  packagingCost: number;
  shopPrice: number;
  shopDeliveryPrice: number;
  onlinePrice: number;
  updatedShopPrice: number;
  updatedOnlinePrice: number;
  onlineDeduction: number;
  onlineDiscount: number;
  payoutCalculation: number;
  onlinePayout: number;
  onlineProfit: number;
  offlineProfit: number;
  takeawayProfit: number;
  isFinalized: boolean;
  finalizedDate?: string;
  finalizedBy?: string;
  history: PriceHistory[];
  createdBy: string;
  createdDate: string;
  lastUpdatedBy: string;
  lastUpdated: string;
}

@Injectable({
  providedIn: 'root'
})
export class PriceForecastService {
  private http = inject(HttpClient);
  private apiUrl = environment.apiUrl;

  // Get all price forecasts
  getPriceForecasts(): Observable<PriceForecast[]> {
    return this.http.get<PriceForecast[]>(`${this.apiUrl}/priceforecasts`);
  }

  // Get price forecast by ID
  getPriceForecast(id: string): Observable<PriceForecast> {
    return this.http.get<PriceForecast>(`${this.apiUrl}/priceforecasts/${id}`);
  }

  // Get price forecasts by menu item ID
  getPriceForecastsByMenuItem(menuItemId: string): Observable<PriceForecast[]> {
    return this.http.get<PriceForecast[]>(`${this.apiUrl}/priceforecasts/menuitem/${menuItemId}`);
  }

  // Create a new price forecast
  createPriceForecast(forecast: PriceForecast): Observable<PriceForecast> {
    return this.http.post<PriceForecast>(`${this.apiUrl}/priceforecasts`, forecast);
  }

  // Update an existing price forecast
  updatePriceForecast(id: string, forecast: PriceForecast): Observable<PriceForecast> {
    return this.http.put<PriceForecast>(`${this.apiUrl}/priceforecasts/${id}`, forecast);
  }

  // Delete a price forecast
  deletePriceForecast(id: string): Observable<any> {
    return this.http.delete(`${this.apiUrl}/priceforecasts/${id}`);
  }

  // Finalize a price forecast
  finalizePriceForecast(id: string): Observable<any> {
    return this.http.post(`${this.apiUrl}/priceforecasts/${id}/finalize`, {});
  }

  // Calculate payout based on other fields
  calculatePayout(forecast: Partial<PriceForecast>): number {
    // Use updatedOnlinePrice if available, otherwise use onlinePrice
    const onlinePrice = forecast.updatedOnlinePrice || forecast.onlinePrice || 0;
    const packagingCost = forecast.packagingCost || 0;
    const onlineDeduction = forecast.onlineDeduction || 0;
    const onlineDiscount = forecast.onlineDiscount || 0;

    // Calculate: ((Online Price + Packaging) - Discount%) - (((Online Price + Packaging) - Discount%) × Deduction%)
    const baseAmount = onlinePrice + packagingCost;
    const discountAmount = (baseAmount * onlineDiscount) / 100;
    const afterDiscount = baseAmount - discountAmount;
    const deductionAmount = (afterDiscount * onlineDeduction) / 100;
    const payout = afterDiscount - deductionAmount;

    return Math.max(0, payout); // Ensure non-negative
  }

  // Calculate all profit metrics
  calculateProfits(forecast: Partial<PriceForecast>): {
    onlinePayout: number;
    onlineProfit: number;
    offlineProfit: number;
    takeawayProfit: number;
  } {
    // Use updatedOnlinePrice if available, otherwise use onlinePrice
    const onlinePrice = forecast.updatedOnlinePrice || forecast.onlinePrice || 0;
    const onlineDeduction = forecast.onlineDeduction || 0;
    const onlineDiscount = forecast.onlineDiscount || 0;
    const makePrice = forecast.makePrice || 0;
    const packagingCost = forecast.packagingCost || 0;
    const shopPrice = forecast.shopPrice || 0;
    const shopDeliveryPrice = forecast.shopDeliveryPrice || 0;

    // Online Payout = ((Online Price + Packaging) - Discount%) - (((Online Price + Packaging) - Discount%) × Deduction%)
    const baseAmount = onlinePrice + packagingCost;
    const discountAmount = (baseAmount * onlineDiscount) / 100;
    const afterDiscount = baseAmount - discountAmount;
    const deductionAmount = (afterDiscount * onlineDeduction) / 100;
    const onlinePayout = Math.max(0, afterDiscount - deductionAmount);

    // Online Profit = Online Payout - Making Price
    const onlineProfit = Math.max(0, onlinePayout - makePrice);

    // Offline Profit = Shop Price - Making Price
    const offlineProfit = Math.max(0, shopPrice - makePrice);

    // Takeaway Profit = Shop Delivery Price - (Making Price + Packaging Price)
    const takeawayProfit = Math.max(0, shopDeliveryPrice - (makePrice + packagingCost));

    return {
      onlinePayout,
      onlineProfit,
      offlineProfit,
      takeawayProfit
    };
  }
}
