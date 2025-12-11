import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../../environments/environment';

interface Category {
  id: string;
  name: string;
  isEditing?: boolean;
}

interface SubCategory {
  id: string;
  categoryId: string;
  name: string;
  categoryName?: string;
  isEditing?: boolean;
}

@Component({
  selector: 'app-category-crud',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './category-crud.component.html',
  styleUrls: ['./category-crud.component.scss']
})
export class CategoryCrudComponent implements OnInit {
  categories: Category[] = [];
  subCategories: SubCategory[] = [];
  filteredSubCategories: SubCategory[] = [];

  selectedTab: 'categories' | 'subcategories' = 'categories';
  selectedCategoryId: string = '';

  // New item forms
  newCategory: string = '';
  newSubCategory = {
    categoryId: '',
    name: ''
  };

  // Edit forms
  editingCategory: Category | null = null;
  editingSubCategory: SubCategory | null = null;

  // Loading states
  loading = {
    categories: false,
    subCategories: false,
    saving: false,
    deleting: false
  };

  // Messages
  message = {
    text: '',
    type: '' as 'success' | 'error' | ''
  };

  private apiUrl = environment.apiUrl;

  constructor(private http: HttpClient) {}

  ngOnInit() {
    this.loadCategories();
    this.loadSubCategories();
  }

  // Load Data
  loadCategories() {
    this.loading.categories = true;
    this.http.get<Category[]>(`${this.apiUrl}/categories`)
      .subscribe({
        next: (data) => {
          this.categories = data.map(c => ({ ...c, isEditing: false }));
          this.loading.categories = false;
        },
        error: (error) => {
          this.showMessage('Failed to load categories', 'error');
          this.loading.categories = false;
          console.error('Error loading categories:', error);
        }
      });
  }

  loadSubCategories() {
    this.loading.subCategories = true;
    this.http.get<SubCategory[]>(`${this.apiUrl}/subcategories`)
      .subscribe({
        next: (data) => {
          this.subCategories = data.map(sc => ({ ...sc, isEditing: false }));
          this.filteredSubCategories = this.subCategories;
          this.loading.subCategories = false;
        },
        error: (error) => {
          this.showMessage('Failed to load subcategories', 'error');
          this.loading.subCategories = false;
          console.error('Error loading subcategories:', error);
        }
      });
  }

  // Filter SubCategories by Category
  filterSubCategories() {
    if (this.selectedCategoryId) {
      this.filteredSubCategories = this.subCategories.filter(
        sc => sc.categoryId === this.selectedCategoryId
      );
    } else {
      this.filteredSubCategories = this.subCategories;
    }
  }

  // Create Operations
  createCategory() {
    if (!this.newCategory.trim()) {
      this.showMessage('Please enter a category name', 'error');
      return;
    }

    this.loading.saving = true;
    this.http.post<Category>(`${this.apiUrl}/categories`, { name: this.newCategory })
      .subscribe({
        next: (category) => {
          this.categories.push({ ...category, isEditing: false });
          this.newCategory = '';
          this.showMessage('Category created successfully', 'success');
          this.loading.saving = false;
        },
        error: (error) => {
          this.showMessage('Failed to create category', 'error');
          this.loading.saving = false;
          console.error('Error creating category:', error);
        }
      });
  }

  createSubCategory() {
    if (!this.newSubCategory.categoryId || !this.newSubCategory.name.trim()) {
      this.showMessage('Please select a category and enter a name', 'error');
      return;
    }

    this.loading.saving = true;
    this.http.post<SubCategory>(`${this.apiUrl}/subcategories`, this.newSubCategory)
      .subscribe({
        next: (subCategory) => {
          this.subCategories.push({ ...subCategory, isEditing: false });
          this.filterSubCategories();
          this.newSubCategory = { categoryId: '', name: '' };
          this.showMessage('Subcategory created successfully', 'success');
          this.loading.saving = false;
        },
        error: (error) => {
          this.showMessage('Failed to create subcategory', 'error');
          this.loading.saving = false;
          console.error('Error creating subcategory:', error);
        }
      });
  }

  // Update Operations
  startEdit(item: Category | SubCategory, type: 'category' | 'subcategory') {
    if (type === 'category') {
      this.editingCategory = { ...item as Category };
    } else {
      this.editingSubCategory = { ...item as SubCategory };
    }
    item.isEditing = true;
  }

  cancelEdit(item: Category | SubCategory) {
    item.isEditing = false;
    this.editingCategory = null;
    this.editingSubCategory = null;
  }

  saveCategory(category: Category) {
    if (!this.editingCategory || !this.editingCategory.name.trim()) {
      this.showMessage('Please enter a valid name', 'error');
      return;
    }

    this.loading.saving = true;
    this.http.put(`${this.apiUrl}/categories/${category.id}`, { name: this.editingCategory.name })
      .subscribe({
        next: () => {
          category.name = this.editingCategory!.name;
          category.isEditing = false;
          this.editingCategory = null;
          this.showMessage('Category updated successfully', 'success');
          this.loading.saving = false;
        },
        error: (error) => {
          this.showMessage('Failed to update category', 'error');
          this.loading.saving = false;
          console.error('Error updating category:', error);
        }
      });
  }

  saveSubCategory(subCategory: SubCategory) {
    if (!this.editingSubCategory || !this.editingSubCategory.name.trim()) {
      this.showMessage('Please enter a valid name', 'error');
      return;
    }

    this.loading.saving = true;
    const payload = {
      categoryId: this.editingSubCategory.categoryId,
      name: this.editingSubCategory.name
    };

    this.http.put(`${this.apiUrl}/subcategories/${subCategory.id}`, payload)
      .subscribe({
        next: () => {
          subCategory.name = this.editingSubCategory!.name;
          subCategory.categoryId = this.editingSubCategory!.categoryId;
          subCategory.isEditing = false;
          this.editingSubCategory = null;
          this.filterSubCategories();
          this.showMessage('Subcategory updated successfully', 'success');
          this.loading.saving = false;
        },
        error: (error) => {
          this.showMessage('Failed to update subcategory', 'error');
          this.loading.saving = false;
          console.error('Error updating subcategory:', error);
        }
      });
  }

  // Delete Operations
  deleteCategory(category: Category) {
    if (!confirm(`Are you sure you want to delete "${category.name}"?`)) {
      return;
    }

    this.loading.deleting = true;
    this.http.delete(`${this.apiUrl}/categories/${category.id}`)
      .subscribe({
        next: () => {
          this.categories = this.categories.filter(c => c.id !== category.id);
          this.showMessage('Category deleted successfully', 'success');
          this.loading.deleting = false;
        },
        error: (error) => {
          this.showMessage('Failed to delete category', 'error');
          this.loading.deleting = false;
          console.error('Error deleting category:', error);
        }
      });
  }

  deleteSubCategory(subCategory: SubCategory) {
    if (!confirm(`Are you sure you want to delete "${subCategory.name}"?`)) {
      return;
    }

    this.loading.deleting = true;
    this.http.delete(`${this.apiUrl}/subcategories/${subCategory.id}`)
      .subscribe({
        next: () => {
          this.subCategories = this.subCategories.filter(sc => sc.id !== subCategory.id);
          this.filterSubCategories();
          this.showMessage('Subcategory deleted successfully', 'success');
          this.loading.deleting = false;
        },
        error: (error) => {
          this.showMessage('Failed to delete subcategory', 'error');
          this.loading.deleting = false;
          console.error('Error deleting subcategory:', error);
        }
      });
  }

  // Utility
  getCategoryName(categoryId: string): string {
    const category = this.categories.find(c => c.id === categoryId);
    return category ? category.name : 'Unknown';
  }

  showMessage(text: string, type: 'success' | 'error') {
    this.message = { text, type };
    setTimeout(() => {
      this.message = { text: '', type: '' };
    }, 3000);
  }
}
