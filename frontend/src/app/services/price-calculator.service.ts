import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, BehaviorSubject, of, forkJoin } from 'rxjs';
import { map, catchError, switchMap } from 'rxjs/operators';
import {
  Ingredient,
  MenuItemRecipe,
  IngredientUsage,
  PriceCalculation,
  PriceHistory,
  PriceUpdateSettings,
  COMMON_INGREDIENTS
} from '../models/ingredient.model';
import { environment } from '../../environments/environment';

@Injectable({
  providedIn: 'root'
})
export class PriceCalculatorService {
  private apiUrl = environment.apiUrl || '/api';

  // BehaviorSubjects for reactive data
  private ingredientsSubject = new BehaviorSubject<Ingredient[]>([]);
  private recipesSubject = new BehaviorSubject<MenuItemRecipe[]>([]);

  public ingredients$ = this.ingredientsSubject.asObservable();
  public recipes$ = this.recipesSubject.asObservable();

  constructor(private http: HttpClient) {
    this.loadIngredientsFromServer();
    this.loadRecipesFromServer();
  }

  // Load data from server
  private loadIngredientsFromServer(): void {
    this.http.get<Ingredient[]>(`${this.apiUrl}/ingredients`)
      .pipe(catchError(() => {
        // If API fails, try localStorage as fallback
        const stored = localStorage.getItem('cafe_ingredients');
        if (stored) {
          return of(JSON.parse(stored));
        }
        // If no data, initialize with defaults
        return of(this.initializeDefaultIngredients());
      }))
      .subscribe(ingredients => {
        this.ingredientsSubject.next(ingredients);
        // Cache in localStorage as backup
        localStorage.setItem('cafe_ingredients', JSON.stringify(ingredients));
      });
  }

  private loadRecipesFromServer(): void {
    this.http.get<MenuItemRecipe[]>(`${this.apiUrl}/recipes`)
      .pipe(catchError(() => {
        const stored = localStorage.getItem('cafe_recipes');
        return stored ? of(JSON.parse(stored)) : of([]);
      }))
      .subscribe(recipes => {
        this.recipesSubject.next(recipes);
        localStorage.setItem('cafe_recipes', JSON.stringify(recipes));
      });
  }

  private initializeDefaultIngredients(): Ingredient[] {
    return COMMON_INGREDIENTS.map((ing, index) => ({
      ...ing,
      id: `ing_${Date.now()}_${index}`,
      lastUpdated: new Date()
    }));
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
    return this.http.post<Ingredient>(`${this.apiUrl}/ingredients`, ingredient)
      .pipe(
        map(newIngredient => {
          const currentIngredients = this.ingredientsSubject.value;
          const updatedIngredients = [...currentIngredients, newIngredient];
          this.ingredientsSubject.next(updatedIngredients);
          localStorage.setItem('cafe_ingredients', JSON.stringify(updatedIngredients));
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
          const currentIngredients = this.ingredientsSubject.value;
          const updatedIngredients = [...currentIngredients, newIngredient];
          this.ingredientsSubject.next(updatedIngredients);
          localStorage.setItem('cafe_ingredients', JSON.stringify(updatedIngredients));
          return of(newIngredient);
        })
      );
  }

  updateIngredient(id: string, ingredient: Partial<Ingredient>): Observable<Ingredient> {
    const updateData = { ...ingredient, id, lastUpdated: new Date() };

    return this.http.put<Ingredient>(`${this.apiUrl}/ingredients/${id}`, updateData)
      .pipe(
        map(updatedIngredient => {
          const currentIngredients = this.ingredientsSubject.value;
          const index = currentIngredients.findIndex(ing => ing.id === id);
          if (index !== -1) {
            currentIngredients[index] = updatedIngredient;
            this.ingredientsSubject.next([...currentIngredients]);
            localStorage.setItem('cafe_ingredients', JSON.stringify(currentIngredients));
          }
          return updatedIngredient;
        }),
        catchError((error) => {
          console.error('Error updating ingredient, using local fallback', error);
          const currentIngredients = this.ingredientsSubject.value;
          const index = currentIngredients.findIndex(ing => ing.id === id);
          if (index !== -1) {
            const updatedIngredient = {
              ...currentIngredients[index],
              ...ingredient,
              lastUpdated: new Date()
            };
            currentIngredients[index] = updatedIngredient;
            this.ingredientsSubject.next([...currentIngredients]);
            localStorage.setItem('cafe_ingredients', JSON.stringify(currentIngredients));
            return of(updatedIngredient);
          }
          throw error;
        })
      );
  }

  deleteIngredient(id: string): Observable<boolean> {
    return this.http.delete(`${this.apiUrl}/ingredients/${id}`)
      .pipe(
        map(() => {
          const currentIngredients = this.ingredientsSubject.value;
          const filteredIngredients = currentIngredients.filter(ing => ing.id !== id);
          this.ingredientsSubject.next(filteredIngredients);
          localStorage.setItem('cafe_ingredients', JSON.stringify(filteredIngredients));
          return true;
        }),
        catchError((error) => {
          console.error('Error deleting ingredient, using local fallback', error);
          const currentIngredients = this.ingredientsSubject.value;
          const filteredIngredients = currentIngredients.filter(ing => ing.id !== id);
          this.ingredientsSubject.next(filteredIngredients);
          localStorage.setItem('cafe_ingredients', JSON.stringify(filteredIngredients));
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
    if (recipe.id) {
      // Update existing recipe
      return this.http.put<MenuItemRecipe>(`${this.apiUrl}/recipes/${recipe.id}`, recipe)
        .pipe(
          map(updatedRecipe => {
            const currentRecipes = this.recipesSubject.value;
            const index = currentRecipes.findIndex(r => r.id === recipe.id);
            if (index !== -1) {
              currentRecipes[index] = updatedRecipe;
              this.recipesSubject.next([...currentRecipes]);
              localStorage.setItem('cafe_recipes', JSON.stringify(currentRecipes));
            }
            return updatedRecipe;
          }),
          catchError((error) => {
            console.error('Error updating recipe, using local fallback', error);
            const updatedRecipe = { ...recipe, updatedAt: new Date() };
            const currentRecipes = this.recipesSubject.value;
            const index = currentRecipes.findIndex(r => r.id === recipe.id);
            if (index !== -1) {
              currentRecipes[index] = updatedRecipe;
              this.recipesSubject.next([...currentRecipes]);
              localStorage.setItem('cafe_recipes', JSON.stringify(currentRecipes));
            }
            return of(updatedRecipe);
          })
        );
    } else {
      // Create new recipe
      return this.http.post<MenuItemRecipe>(`${this.apiUrl}/recipes`, recipe)
        .pipe(
          map(newRecipe => {
            const currentRecipes = this.recipesSubject.value;
            const updatedRecipes = [...currentRecipes, newRecipe];
            this.recipesSubject.next(updatedRecipes);
            localStorage.setItem('cafe_recipes', JSON.stringify(updatedRecipes));
            return newRecipe;
          }),
          catchError((error) => {
            console.error('Error creating recipe, using local fallback', error);
            const newRecipe: MenuItemRecipe = {
              ...recipe,
              id: `recipe_${Date.now()}_${Math.random().toString(36).substr(2, 9)}`,
              createdAt: new Date(),
              updatedAt: new Date()
            };
            const currentRecipes = this.recipesSubject.value;
            const updatedRecipes = [...currentRecipes, newRecipe];
            this.recipesSubject.next(updatedRecipes);
            localStorage.setItem('cafe_recipes', JSON.stringify(updatedRecipes));
            return of(newRecipe);
          })
        );
    }
  }

  deleteRecipe(id: string): Observable<boolean> {
    return this.http.delete(`${this.apiUrl}/recipes/${id}`)
      .pipe(
        map(() => {
          const currentRecipes = this.recipesSubject.value;
          const filteredRecipes = currentRecipes.filter(recipe => recipe.id !== id);
          this.recipesSubject.next(filteredRecipes);
          localStorage.setItem('cafe_recipes', JSON.stringify(filteredRecipes));
          return true;
        }),
        catchError((error) => {
          console.error('Error deleting recipe, using local fallback', error);
          const currentRecipes = this.recipesSubject.value;
          const filteredRecipes = currentRecipes.filter(recipe => recipe.id !== id);
          this.recipesSubject.next(filteredRecipes);
          localStorage.setItem('cafe_recipes', JSON.stringify(filteredRecipes));
          return of(true);
        })
      );
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
    // Normalize units
    let normalizedQuantity = quantity;
    let normalizedUnitPrice = ingredient.marketPrice;

    // Handle conversions
    if (unit === 'gm' && ingredient.unit === 'kg') {
      normalizedQuantity = quantity / 1000;
    } else if (unit === 'kg' && ingredient.unit === 'gm') {
      normalizedUnitPrice = ingredient.marketPrice / 1000;
    } else if (unit === 'ml' && ingredient.unit === 'ltr') {
      normalizedQuantity = quantity / 1000;
    } else if (unit === 'ltr' && ingredient.unit === 'ml') {
      normalizedUnitPrice = ingredient.marketPrice / 1000;
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
          this.ingredientsSubject.next(ingredients);
          localStorage.setItem('cafe_ingredients', JSON.stringify(ingredients));
          return ingredients;
        }),
        catchError(() => {
          // Fallback to local reset
          this.ingredientsSubject.next(defaultIngredients);
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

