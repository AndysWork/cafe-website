import { Injectable, signal, inject } from '@angular/core';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Observable, of, forkJoin, throwError } from 'rxjs';
import { map, catchError, switchMap } from 'rxjs/operators';
import { toObservable } from '@angular/core/rxjs-interop';
import {
  Ingredient,
  MenuItemRecipe,
  IngredientUsage,
  PriceCalculation,
  PriceHistory,
  PriceUpdateSettings,
  COMMON_INGREDIENTS
} from '../models/ingredient.model';
import { OutletService } from './outlet.service';
import { environment } from '../../environments/environment';

export interface BulkUploadRecipeResult {
  recipesCreated: number;
  recipesUpdated: number;
  totalRows: number;
  totalRecipes: number;
  failedRows: number;
  errors: string[];
  message: string;
}

@Injectable({
  providedIn: 'root'
})
export class PriceCalculatorService {
  private http = inject(HttpClient);
  private outletService = inject(OutletService);
  private apiUrl = environment.apiUrl || '/api';

  // Signal-based reactive state (replaces BehaviorSubjects)
  private ingredientsSignal = signal<Ingredient[]>([]);
  private recipesSignal = signal<MenuItemRecipe[]>([]);

  public ingredients$ = toObservable(this.ingredientsSignal);
  public recipes$ = toObservable(this.recipesSignal);

  constructor() {
    this.loadIngredientsFromServer();
    this.loadRecipesFromServer();
  }

  private getCurrentOutletId(): string | null {
    return this.outletService.getSelectedOutletId();
  }

  private getHeaders(): HttpHeaders {
    const outletId = this.getCurrentOutletId();
    let headers = new HttpHeaders();

    if (outletId) {
      headers = headers.set('X-Outlet-Id', outletId);
    }

    return headers;
  }

  // Load data from server
  private loadIngredientsFromServer(): void {
    const outletId = this.getCurrentOutletId() || 'default';
    this.http.get<Ingredient[]>(`${this.apiUrl}/ingredients`, { headers: this.getHeaders() })
      .pipe(catchError(() => {
        // If API fails, try localStorage as fallback
        const stored = localStorage.getItem(`cafe_ingredients_${outletId}`);
        if (stored) {
          return of(JSON.parse(stored));
        }
        // If no data, initialize with defaults
        return of(this.initializeDefaultIngredients());
      }))
      .subscribe(ingredients => {
        this.ingredientsSignal.set(ingredients);
        // Cache in localStorage as backup
        localStorage.setItem(`cafe_ingredients_${outletId}`, JSON.stringify(ingredients));
      });
  }

  private loadRecipesFromServer(): void {
    const outletId = this.getCurrentOutletId() || 'default';
    this.http.get<MenuItemRecipe[]>(`${this.apiUrl}/recipes`, { headers: this.getHeaders() })
      .pipe(catchError((error) => {
        console.error('Error loading recipes from API:', error);
        const stored = localStorage.getItem(`cafe_recipes_${outletId}`);
        if (stored) {
          return of(JSON.parse(stored));
        }
        return of([]);
      }))
      .subscribe(recipes => {
        this.recipesSignal.set(recipes);
        localStorage.setItem(`cafe_recipes_${outletId}`, JSON.stringify(recipes));
      });
  }

  private initializeDefaultIngredients(): Ingredient[] {
    return COMMON_INGREDIENTS.map((ing, index) => ({
      ...ing,
      id: `ing_${Date.now()}_${index}`,
      lastUpdated: new Date()
    }));
  }

  // Public method to reload data (used when outlet changes)
  reloadData(): void {
    this.loadIngredientsFromServer();
    this.loadRecipesFromServer();
  }

  // ===== INGREDIENT MANAGEMENT =====

  getIngredients(): Observable<Ingredient[]> {
    return this.ingredients$;
  }

  getIngredientById(id: string): Observable<Ingredient | undefined> {
    return this.http.get<Ingredient>(`${this.apiUrl}/ingredients/${id}`)
      .pipe(
        catchError(() => {
          return this.ingredients$.pipe(
            map(ingredients => ingredients.find(ing => ing.id === id))
          );
        })
      );
  }

  addIngredient(ingredient: Ingredient): Observable<Ingredient> {
    const outletId = this.getCurrentOutletId() || 'default';
    if (!ingredient.outletId && outletId !== 'default') {
      ingredient.outletId = outletId;
    }

    return this.http.post<Ingredient>(`${this.apiUrl}/ingredients`, ingredient)
      .pipe(
        map(newIngredient => {
          const currentIngredients = this.ingredientsSignal();
          const updatedIngredients = [...currentIngredients, newIngredient];
          this.ingredientsSignal.set(updatedIngredients);
          localStorage.setItem(`cafe_ingredients_${outletId}`, JSON.stringify(updatedIngredients));
          return newIngredient;
        }),
        catchError((error) => {
          console.error('Error adding ingredient, using local fallback', error);
          const newIngredient: Ingredient = {
            ...ingredient,
            id: `ing_${Date.now()}_${Math.random().toString(36).substr(2, 9)}`,
            lastUpdated: new Date(),
            isActive: true
          };
          const currentIngredients = this.ingredientsSignal();
          const updatedIngredients = [...currentIngredients, newIngredient];
          this.ingredientsSignal.set(updatedIngredients);
          localStorage.setItem(`cafe_ingredients_${outletId}`, JSON.stringify(updatedIngredients));
          return of(newIngredient);
        })
      );
  }

  updateIngredient(id: string, ingredient: Partial<Ingredient>): Observable<Ingredient> {
    const outletId = this.getCurrentOutletId() || 'default';
    const updateData = { ...ingredient, id, lastUpdated: new Date() };
    if (!updateData.outletId && outletId !== 'default') {
      updateData.outletId = outletId;
    }

    return this.http.put<Ingredient>(`${this.apiUrl}/ingredients/${id}`, updateData)
      .pipe(
        map(updatedIngredient => {
          const currentIngredients = this.ingredientsSignal();
          const index = currentIngredients.findIndex(ing => ing.id === id);
          if (index !== -1) {
            currentIngredients[index] = updatedIngredient;
            this.ingredientsSignal.set([...currentIngredients]);
            localStorage.setItem(`cafe_ingredients_${outletId}`, JSON.stringify(currentIngredients));
          }
          return updatedIngredient;
        }),
        catchError((error) => {
          console.error('Error updating ingredient, using local fallback', error);
          const currentIngredients = this.ingredientsSignal();
          const index = currentIngredients.findIndex(ing => ing.id === id);
          if (index !== -1) {
            const updatedIngredient = {
              ...currentIngredients[index],
              ...ingredient,
              lastUpdated: new Date()
            };
            currentIngredients[index] = updatedIngredient;
            this.ingredientsSignal.set([...currentIngredients]);
            localStorage.setItem(`cafe_ingredients_${outletId}`, JSON.stringify(currentIngredients));
            return of(updatedIngredient);
          }
          throw error;
        })
      );
  }

  deleteIngredient(id: string): Observable<boolean> {
    const outletId = this.getCurrentOutletId() || 'default';
    return this.http.delete(`${this.apiUrl}/ingredients/${id}`)
      .pipe(
        map(() => {
          const currentIngredients = this.ingredientsSignal();
          const filteredIngredients = currentIngredients.filter(ing => ing.id !== id);
          this.ingredientsSignal.set(filteredIngredients);
          localStorage.setItem(`cafe_ingredients_${outletId}`, JSON.stringify(filteredIngredients));
          return true;
        }),
        catchError((error) => {
          console.error('Error deleting ingredient, using local fallback', error);
          const currentIngredients = this.ingredientsSignal();
          const filteredIngredients = currentIngredients.filter(ing => ing.id !== id);
          this.ingredientsSignal.set(filteredIngredients);
          localStorage.setItem(`cafe_ingredients_${outletId}`, JSON.stringify(filteredIngredients));
          return of(true);
        })
      );
  }

  // ===== RECIPE MANAGEMENT =====

  getRecipes(): Observable<MenuItemRecipe[]> {
    return this.recipes$;
  }

  getRecipeById(id: string): Observable<MenuItemRecipe | undefined> {
    return this.http.get<MenuItemRecipe>(`${this.apiUrl}/recipes/${id}`)
      .pipe(
        catchError(() => {
          return this.recipes$.pipe(
            map(recipes => recipes.find(recipe => recipe.id === id))
          );
        })
      );
  }

  getRecipeByMenuItemName(menuItemName: string): Observable<MenuItemRecipe | undefined> {
    return this.http.get<MenuItemRecipe>(`${this.apiUrl}/recipes/menuitem/${encodeURIComponent(menuItemName)}`)
      .pipe(
        catchError(() => {
          return this.recipes$.pipe(
            map(recipes => recipes.find(recipe =>
              recipe.menuItemName.toLowerCase() === menuItemName.toLowerCase()
            ))
          );
        })
      );
  }

  saveRecipe(recipe: MenuItemRecipe): Observable<MenuItemRecipe> {
    const outletId = this.getCurrentOutletId() || 'default';
    if (!recipe.outletId && outletId !== 'default') {
      recipe.outletId = outletId;
    }

    if (recipe.id) {
      // Update existing recipe
      return this.http.put<MenuItemRecipe>(`${this.apiUrl}/recipes/${recipe.id}`, recipe)
        .pipe(
          map(updatedRecipe => {
            const currentRecipes = this.recipesSignal();
            const index = currentRecipes.findIndex(r => r.id === recipe.id);
            if (index !== -1) {
              currentRecipes[index] = updatedRecipe;
              this.recipesSignal.set([...currentRecipes]);
              localStorage.setItem(`cafe_recipes_${outletId}`, JSON.stringify(currentRecipes));
            }
            return updatedRecipe;
          }),
          catchError((error) => {
            console.error('Error updating recipe on server', error);
            return throwError(() => error);
          })
        );
    } else {
      // Create new recipe
      return this.http.post<MenuItemRecipe>(`${this.apiUrl}/recipes`, recipe)
        .pipe(
          map(newRecipe => {
            const currentRecipes = this.recipesSignal();
            const updatedRecipes = [...currentRecipes, newRecipe];
            this.recipesSignal.set(updatedRecipes);
            localStorage.setItem(`cafe_recipes_${outletId}`, JSON.stringify(updatedRecipes));
            return newRecipe;
          }),
          catchError((error) => {
            console.error('Error creating recipe on server', error);
            return throwError(() => error);
          })
        );
    }
  }

  deleteRecipe(id: string): Observable<boolean> {
    const outletId = this.getCurrentOutletId() || 'default';
    return this.http.delete(`${this.apiUrl}/recipes/${id}`)
      .pipe(
        map(() => {
          const currentRecipes = this.recipesSignal();
          const filteredRecipes = currentRecipes.filter(recipe => recipe.id !== id);
          this.recipesSignal.set(filteredRecipes);
          localStorage.setItem(`cafe_recipes_${outletId}`, JSON.stringify(filteredRecipes));
          return true;
        }),
        catchError((error) => {
          console.error('Error deleting recipe, using local fallback', error);
          const currentRecipes = this.recipesSignal();
          const filteredRecipes = currentRecipes.filter(recipe => recipe.id !== id);
          this.recipesSignal.set(filteredRecipes);
          localStorage.setItem(`cafe_recipes_${outletId}`, JSON.stringify(filteredRecipes));
          return of(true);
        })
      );
  }

  // ===== RECIPE BULK UPLOAD & TEMPLATE =====

  downloadRecipeTemplate(): Observable<Blob> {
    return this.http.get(`${this.apiUrl}/recipes/template`, { responseType: 'blob' });
  }

  uploadRecipesExcel(file: File): Observable<BulkUploadRecipeResult> {
    const formData = new FormData();
    formData.append('file', file);
    return this.http.post<BulkUploadRecipeResult>(`${this.apiUrl}/recipes/upload`, formData);
  }

  // ===== PRICE CALCULATION =====

  calculateRecipePrice(recipe: MenuItemRecipe): PriceCalculation {
    // Calculate total ingredient cost
    const ingredientSubtotal = recipe.ingredients.reduce((sum, ing) => sum + ing.totalCost, 0);

    // Calculate wastage
    const wastageAmount = (ingredientSubtotal * recipe.overheadCosts.wastagePercentage) / 100;

    // Calculate overhead costs
    const overheadSubtotal =
      recipe.overheadCosts.labourCharge +
      recipe.overheadCosts.rentAllocation +
      recipe.overheadCosts.electricityCharge +
      wastageAmount +
      recipe.overheadCosts.miscellaneous;

    // Calculate making cost
    const makingCost = ingredientSubtotal + overheadSubtotal;

    // Calculate profit amount
    const profitAmount = (makingCost * recipe.profitMargin) / 100;

    // Calculate selling price
    const sellingPrice = makingCost + profitAmount;

    return {
      recipeId: recipe.id || '',
      recipeName: recipe.menuItemName,
      breakdown: {
        ingredients: recipe.ingredients,
        ingredientSubtotal,
        labour: recipe.overheadCosts.labourCharge,
        rent: recipe.overheadCosts.rentAllocation,
        electricity: recipe.overheadCosts.electricityCharge,
        wastage: wastageAmount,
        miscellaneous: recipe.overheadCosts.miscellaneous,
        overheadSubtotal,
        makingCost,
        profitAmount,
        profitPercentage: recipe.profitMargin,
        sellingPrice
      },
      calculatedAt: new Date()
    };
  }

  // Convert units for calculation (standardize to base units)
  convertToBaseUnit(quantity: number, unit: string): number {
    switch (unit) {
      case 'gm': return quantity / 1000; // Convert gm to kg
      case 'ml': return quantity / 1000; // Convert ml to ltr
      case 'kg':
      case 'ltr':
      case 'pc':
      default: return quantity;
    }
  }

  // Calculate cost for an ingredient usage
  calculateIngredientCost(
    quantity: number,
    unit: string,
    ingredient: Ingredient
  ): number {
    let normalizedQuantity = quantity;
    let normalizedUnitPrice = ingredient.marketPrice;

    const u1 = (unit || '').trim().toLowerCase();
    const u2 = (ingredient.unit || '').trim().toLowerCase();

    // Weight conversions (gm/g vs kg)
    if ((u1 === 'gm' || u1 === 'g') && u2 === 'kg') {
      normalizedQuantity = quantity / 1000;
    } else if (u1 === 'kg' && (u2 === 'gm' || u2 === 'g')) {
      normalizedUnitPrice = ingredient.marketPrice / 1000;
    }
    // Volume conversions (ml vs ltr/l)
    else if (u1 === 'ml' && (u2 === 'ltr' || u2 === 'l')) {
      normalizedQuantity = quantity / 1000;
    } else if ((u1 === 'ltr' || u1 === 'l') && u2 === 'ml') {
      normalizedUnitPrice = ingredient.marketPrice / 1000;
    }
    // Piece / Dozen conversions
    else if ((u1 === 'pc' || u1 === 'pcs') && u2 === 'dozen') {
      normalizedQuantity = quantity / 12;
    } else if (u1 === 'dozen' && (u2 === 'pc' || u2 === 'pcs')) {
      normalizedUnitPrice = ingredient.marketPrice / 12;
    }

    return normalizedQuantity * normalizedUnitPrice;
  }

  // Export recipe as JSON
  exportRecipe(recipe: MenuItemRecipe): string {
    return JSON.stringify(recipe, null, 2);
  }

  // Import recipe from JSON
  importRecipe(jsonString: string): Observable<MenuItemRecipe> {
    try {
      const recipe = JSON.parse(jsonString) as MenuItemRecipe;
      return this.saveRecipe(recipe);
    } catch (error) {
      throw new Error('Invalid recipe JSON');
    }
  }

  // Reset to default ingredients
  resetToDefaultIngredients(): Observable<Ingredient[]> {
    const defaultIngredients = this.initializeDefaultIngredients();

    // Try to reset on server, but continue even if it fails
    return this.http.delete(`${this.apiUrl}/ingredients`)
      .pipe(
        switchMap(() => {
          // Create all default ingredients on server
          const creates = defaultIngredients.map(ing =>
            this.http.post<Ingredient>(`${this.apiUrl}/ingredients`, ing)
              .pipe(catchError(() => of(ing)))
          );
          return forkJoin(creates);
        }),
        map((ingredients) => {
          this.ingredientsSignal.set(ingredients);
          localStorage.setItem('cafe_ingredients', JSON.stringify(ingredients));
          return ingredients;
        }),
        catchError(() => {
          // Fallback to local reset
          this.ingredientsSignal.set(defaultIngredients);
          localStorage.setItem('cafe_ingredients', JSON.stringify(defaultIngredients));
          return of(defaultIngredients);
        })
      );
  }

  // Get ingredients by category
  getIngredientsByCategory(category: string): Observable<Ingredient[]> {
    return this.ingredients$.pipe(
      map(ingredients => ingredients.filter(ing =>
        ing.category === category && ing.isActive
      ))
    );
  }

  // Search ingredients
  searchIngredients(searchTerm: string): Observable<Ingredient[]> {
    return this.ingredients$.pipe(
      map(ingredients => ingredients.filter(ing =>
        ing.name.toLowerCase().includes(searchTerm.toLowerCase()) &&
        ing.isActive
      ))
    );
  }

  // ===== PRICE TRACKING METHODS =====

  // Get price history for an ingredient
  getPriceHistory(ingredientId: string, days: number = 30): Observable<any[]> {
    return this.http.get<any>(`${this.apiUrl}/ingredients/${ingredientId}/price-history?days=${days}`)
      .pipe(
        map(response => response.data || []),
        catchError(() => of([]))
      );
  }

  // Get price trends for charting
  getPriceTrends(ingredientId: string, days: number = 30): Observable<any> {
    return this.http.get<any>(`${this.apiUrl}/ingredients/${ingredientId}/price-trends?days=${days}`)
      .pipe(
        map(response => response.data || {}),
        catchError(() => of({}))
      );
  }

  // Manually refresh price for an ingredient
  refreshIngredientPrice(ingredientId: string): Observable<any> {
    return this.http.post<any>(`${this.apiUrl}/ingredients/${ingredientId}/refresh-price`, {})
      .pipe(
        catchError(error => {
          console.error('Error refreshing price:', error);
          return of({ success: false, error: error.message });
        })
      );
  }

  // Bulk refresh prices for all ingredients with auto-update enabled
  bulkRefreshPrices(): Observable<any> {
    return this.http.post<any>(`${this.apiUrl}/ingredients/bulk-refresh-prices`, {})
      .pipe(
        catchError(error => {
          console.error('Error bulk refreshing prices:', error);
          return of({ success: false, error: error.message });
        })
      );
  }

  // Toggle auto-update for an ingredient
  toggleAutoUpdate(ingredientId: string): Observable<any> {
    return this.http.post<any>(`${this.apiUrl}/ingredients/${ingredientId}/toggle-auto-update`, {})
      .pipe(
        catchError(error => {
          console.error('Error toggling auto-update:', error);
          return of({ success: false, error: error.message });
        })
      );
  }

  // Get price update settings
  getPriceUpdateSettings(): Observable<any> {
    return this.http.get<any>(`${this.apiUrl}/price-settings`)
      .pipe(
        map(response => response.data || {}),
        catchError(() => of({}))
      );
  }

  // Update price update settings
  updatePriceUpdateSettings(settings: any): Observable<any> {
    return this.http.put<any>(`${this.apiUrl}/price-settings`, settings)
      .pipe(
        catchError(error => {
          console.error('Error updating price settings:', error);
          return of({ success: false, error: error.message });
        })
      );
  }
}

