export interface Ingredient {
  id?: string;
  name: string;
  category: string; // 'vegetables', 'spices', 'dairy', 'meat', 'grains', 'oils', 'others'
  marketPrice: number; // Price per unit
  unit: 'kg' | 'gm' | 'ml' | 'pc' | 'ltr'; // Measurement unit
  lastUpdated?: Date;
  isActive?: boolean;
  // Price tracking fields
  priceSource?: string; // 'manual', 'agmarknet', 'scraped', 'api'
  lastPriceFetch?: Date;
  priceChangePercentage?: number;
  previousPrice?: number;
  autoUpdateEnabled?: boolean;
  createdAt?: Date;
  updatedAt?: Date;
}

export interface PriceHistory {
  id?: string;
  ingredientId: string;
  ingredientName: string;
  price: number;
  unit: string;
  source: string;
  marketName?: string;
  recordedAt: Date;
  changePercentage?: number;
  notes?: string;
}

export interface PriceUpdateSettings {
  id?: string;
  autoUpdateEnabled: boolean;
  updateFrequencyHours: number;
  minChangePercentageToRecord: number;
  alertThresholdPercentage: number;
  enabledCategories: string[];
  lastUpdateRun?: Date;
  createdAt?: Date;
  updatedAt?: Date;
}

export interface IngredientUsage {
  ingredientId: string;
  ingredientName: string;
  quantity: number; // Amount used
  unit: 'kg' | 'gm' | 'ml' | 'pc' | 'ltr';
  unitPrice: number; // Price per unit at time of calculation
  totalCost: number; // quantity * unitPrice (converted)
}

export interface MenuItemRecipe {
  id?: string;
  menuItemId?: string;
  menuItemName: string;
  ingredients: IngredientUsage[];
  overheadCosts: {
    labourCharge: number; // Per item labour cost
    rentAllocation: number; // Allocated rent per item
    electricityCharge: number; // Electricity cost per item
    wastagePercentage: number; // Wastage % (applied to ingredient cost)
    miscellaneous: number; // Other costs
  };
  totalIngredientCost: number;
  totalOverheadCost: number;
  totalMakingCost: number; // Ingredient + Overhead
  profitMargin: number; // Profit percentage
  suggestedSellingPrice: number; // Making cost + profit
  actualSellingPrice?: number; // User can override
  notes?: string;
  createdAt?: Date;
  updatedAt?: Date;
}

export interface PriceCalculation {
  recipeId: string;
  recipeName: string;
  breakdown: {
    ingredients: IngredientUsage[];
    ingredientSubtotal: number;
    labour: number;
    rent: number;
    electricity: number;
    wastage: number;
    miscellaneous: number;
    overheadSubtotal: number;
    makingCost: number;
    profitAmount: number;
    profitPercentage: number;
    sellingPrice: number;
  };
  calculatedAt: Date;
}

export const INGREDIENT_CATEGORIES = [
  { value: 'vegetables', label: 'Vegetables' },
  { value: 'spices', label: 'Spices & Condiments' },
  { value: 'dairy', label: 'Dairy Products' },
  { value: 'meat', label: 'Meat & Seafood' },
  { value: 'grains', label: 'Grains & Pulses' },
  { value: 'oils', label: 'Oils & Fats' },
  { value: 'beverages', label: 'Beverages' },
  { value: 'others', label: 'Others' }
];

export const MEASUREMENT_UNITS = [
  { value: 'kg', label: 'Kilogram (kg)' },
  { value: 'gm', label: 'Gram (gm)' },
  { value: 'ltr', label: 'Liter (ltr)' },
  { value: 'ml', label: 'Milliliter (ml)' },
  { value: 'pc', label: 'Piece (pc)' }
];

// Predefined common ingredients with typical prices (in INR)
export const COMMON_INGREDIENTS: Ingredient[] = [
  // Vegetables
  { name: 'Onion', category: 'vegetables', marketPrice: 40, unit: 'kg', isActive: true },
  { name: 'Tomato', category: 'vegetables', marketPrice: 50, unit: 'kg', isActive: true },
  { name: 'Potato', category: 'vegetables', marketPrice: 30, unit: 'kg', isActive: true },
  { name: 'Green Chilli', category: 'vegetables', marketPrice: 80, unit: 'kg', isActive: true },
  { name: 'Ginger', category: 'vegetables', marketPrice: 120, unit: 'kg', isActive: true },
  { name: 'Garlic', category: 'vegetables', marketPrice: 100, unit: 'kg', isActive: true },
  { name: 'Capsicum', category: 'vegetables', marketPrice: 60, unit: 'kg', isActive: true },
  { name: 'Carrot', category: 'vegetables', marketPrice: 45, unit: 'kg', isActive: true },

  // Spices
  { name: 'Turmeric Powder', category: 'spices', marketPrice: 200, unit: 'kg', isActive: true },
  { name: 'Red Chilli Powder', category: 'spices', marketPrice: 250, unit: 'kg', isActive: true },
  { name: 'Coriander Powder', category: 'spices', marketPrice: 180, unit: 'kg', isActive: true },
  { name: 'Cumin Seeds', category: 'spices', marketPrice: 400, unit: 'kg', isActive: true },
  { name: 'Garam Masala', category: 'spices', marketPrice: 500, unit: 'kg', isActive: true },
  { name: 'Salt', category: 'spices', marketPrice: 20, unit: 'kg', isActive: true },
  { name: 'Black Pepper', category: 'spices', marketPrice: 600, unit: 'kg', isActive: true },

  // Dairy
  { name: 'Milk', category: 'dairy', marketPrice: 60, unit: 'ltr', isActive: true },
  { name: 'Butter', category: 'dairy', marketPrice: 500, unit: 'kg', isActive: true },
  { name: 'Ghee', category: 'dairy', marketPrice: 550, unit: 'kg', isActive: true },
  { name: 'Paneer', category: 'dairy', marketPrice: 350, unit: 'kg', isActive: true },
  { name: 'Curd', category: 'dairy', marketPrice: 70, unit: 'kg', isActive: true },
  { name: 'Cheese', category: 'dairy', marketPrice: 400, unit: 'kg', isActive: true },

  // Meat & Seafood
  { name: 'Chicken', category: 'meat', marketPrice: 180, unit: 'kg', isActive: true },
  { name: 'Mutton', category: 'meat', marketPrice: 600, unit: 'kg', isActive: true },
  { name: 'Fish', category: 'meat', marketPrice: 300, unit: 'kg', isActive: true },
  { name: 'Prawns', category: 'meat', marketPrice: 500, unit: 'kg', isActive: true },
  { name: 'Eggs', category: 'meat', marketPrice: 6, unit: 'pc', isActive: true },

  // Grains & Pulses
  { name: 'Rice', category: 'grains', marketPrice: 50, unit: 'kg', isActive: true },
  { name: 'Wheat Flour', category: 'grains', marketPrice: 40, unit: 'kg', isActive: true },
  { name: 'Chickpeas', category: 'grains', marketPrice: 80, unit: 'kg', isActive: true },
  { name: 'Lentils (Dal)', category: 'grains', marketPrice: 100, unit: 'kg', isActive: true },
  { name: 'Bread', category: 'grains', marketPrice: 40, unit: 'kg', isActive: true },

  // Oils
  { name: 'Cooking Oil', category: 'oils', marketPrice: 150, unit: 'ltr', isActive: true },
  { name: 'Olive Oil', category: 'oils', marketPrice: 600, unit: 'ltr', isActive: true },

  // Beverages
  { name: 'Tea Powder', category: 'beverages', marketPrice: 400, unit: 'kg', isActive: true },
  { name: 'Coffee Powder', category: 'beverages', marketPrice: 500, unit: 'kg', isActive: true },
  { name: 'Sugar', category: 'beverages', marketPrice: 45, unit: 'kg', isActive: true },

  // Others
  { name: 'Vinegar', category: 'others', marketPrice: 80, unit: 'ltr', isActive: true },
  { name: 'Soy Sauce', category: 'others', marketPrice: 120, unit: 'ltr', isActive: true },
  { name: 'Tomato Ketchup', category: 'others', marketPrice: 150, unit: 'kg', isActive: true }
];
