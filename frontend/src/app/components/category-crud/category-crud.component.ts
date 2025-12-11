import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../../environments/environment';

interface Category {
  Id: string;
  Name: string;
  isEditing?: boolean;
}

interface SubCategory {
  Id: string;
  CategoryId: string;
  Name: string;
  CategoryName?: string;
  isEditing?: boolean;
}

@Component({
  selector: 'app-category-crud',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './category-crud.component.html',
  styleUrls: ['./category-crud.component.scss'],
})
export class CategoryCrudComponent implements OnInit {
  categories: Category[] = [];
  subCategories: SubCategory[] = [];
  filteredSubCategories: SubCategory[] = [];

  selectedTab: 'categories' | 'subcategories' = 'categories';
  selectedCategoryId: string = '';

  // New item forms
  newCategory: string = '';
  newSubCategory: SubCategory = {
    Id: '',
    CategoryId: '',
    Name: '',
  };
  editingCategory: Category | null = null;
  editingSubCategory: SubCategory | null = null;

  // Loading states
  loading = {
    categories: false,
    subCategories: false,
    saving: false,
    deleting: false,
  };

  // Messages
  message = {
    text: '',
    type: '' as 'success' | 'error' | '',
  };

  private apiUrl = environment.apiUrl;

  constructor(private http: HttpClient) {}

  ngOnInit() {
    this.loadCategories();
    this.loadSubCategories();
  }

  // Load Data
  loadCategories() {
    console.log('Loading categories from:', `${this.apiUrl}/categories`);
    this.loading.categories = true;
    this.http.get<Category[]>(`${this.apiUrl}/categories`).subscribe({
      next: (data) => {
        console.log('Categories loaded:', data);
        this.categories = data.map((c) => ({ ...c, isEditing: false }));
        this.loading.categories = false;
        console.log('Mapped categories:', this.categories);
      },
      error: (error) => {
        console.error('Error loading categories:', error);
        this.showMessage('Failed to load categories', 'error');
        this.loading.categories = false;
      },
    });
  }

  loadSubCategories() {
    console.log('Loading subcategories from:', `${this.apiUrl}/subcategories`);
    this.loading.subCategories = true;
    this.http.get<SubCategory[]>(`${this.apiUrl}/subcategories`).subscribe({
      next: (data) => {
        console.log('SubCategories loaded:', data);
        this.subCategories = data.map((sc) => ({ ...sc, isEditing: false }));
        this.filteredSubCategories = this.subCategories;
        this.loading.subCategories = false;
        console.log('Mapped subcategories:', this.subCategories);
      },
      error: (error) => {
        console.error('Error loading subcategories:', error);
        this.showMessage('Failed to load subcategories', 'error');
        this.loading.subCategories = false;
      },
    });
  }

  // Filter SubCategories by Category
  filterSubCategories() {
    if (this.selectedCategoryId) {
      this.filteredSubCategories = this.subCategories.filter(
        (sc) => sc.CategoryId === this.selectedCategoryId
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
    this.http
      .post<Category>(`${this.apiUrl}/categories`, { Name: this.newCategory })
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
        },
      });
  }

  createSubCategory() {
    if (!this.newSubCategory.CategoryId || !this.newSubCategory.Name.trim()) {
      this.showMessage('Please select a category and enter a name', 'error');
      return;
    }

    this.loading.saving = true;
    this.http
      .post<SubCategory>(`${this.apiUrl}/subcategories`, this.newSubCategory)
      .subscribe({
        next: (subCategory) => {
          this.subCategories.push({ ...subCategory, isEditing: false });
          this.filterSubCategories();
          this.newSubCategory = { Id: '', CategoryId: '', Name: '' };
          this.showMessage('Subcategory created successfully', 'success');
          this.loading.saving = false;
        },
        error: (error) => {
          this.showMessage('Failed to create subcategory', 'error');
          this.loading.saving = false;
          console.error('Error creating subcategory:', error);
        },
      });
  }

  // Update Operations
  startEdit(item: Category | SubCategory, type: 'category' | 'subcategory') {
    if (type === 'category') {
      this.editingCategory = { ...(item as Category) };
    } else {
      this.editingSubCategory = { ...(item as SubCategory) };
    }
    item.isEditing = true;
  }

  cancelEdit(item: Category | SubCategory) {
    item.isEditing = false;
    this.editingCategory = null;
    this.editingSubCategory = null;
  }

  saveCategory(category: Category) {
    if (!this.editingCategory || !this.editingCategory.Name.trim()) {
      this.showMessage('Please enter a valid name', 'error');
      return;
    }

    this.loading.saving = true;
    this.http
      .put(`${this.apiUrl}/categories/${category.Id}`, {
        Name: this.editingCategory.Name,
      })
      .subscribe({
        next: () => {
          category.Name = this.editingCategory!.Name;
          category.isEditing = false;
          this.editingCategory = null;
          this.showMessage('Category updated successfully', 'success');
          this.loading.saving = false;
        },
        error: (error) => {
          this.showMessage('Failed to update category', 'error');
          this.loading.saving = false;
          console.error('Error updating category:', error);
        },
      });
  }

  saveSubCategory(subCategory: SubCategory) {
    if (!this.editingSubCategory || !this.editingSubCategory.Name.trim()) {
      this.showMessage('Please enter a valid name', 'error');
      return;
    }

    this.loading.saving = true;
    const payload = {
      CategoryId: this.editingSubCategory.CategoryId,
      Name: this.editingSubCategory.Name,
    };

    this.http
      .put(`${this.apiUrl}/subcategories/${subCategory.Id}`, payload)
      .subscribe({
        next: () => {
          subCategory.Name = this.editingSubCategory!.Name;
          subCategory.CategoryId = this.editingSubCategory!.CategoryId;
          this.editingSubCategory = null;
          this.filterSubCategories();
          this.showMessage('Subcategory updated successfully', 'success');
          this.loading.saving = false;
        },
        error: (error) => {
          this.showMessage('Failed to update subcategory', 'error');
          this.loading.saving = false;
          console.error('Error updating subcategory:', error);
        },
      });
  }

  // Delete Operations
  deleteCategory(category: Category) {
    if (!confirm(`Are you sure you want to delete "${category.Name}"?`)) {
      return;
    }

    this.loading.deleting = true;
    this.http.delete(`${this.apiUrl}/categories/${category.Id}`).subscribe({
      next: () => {
        this.categories = this.categories.filter((c) => c.Id !== category.Id);
        this.showMessage('Category deleted successfully', 'success');
        this.loading.deleting = false;
      },
      error: (error) => {
        this.showMessage('Failed to delete category', 'error');
        this.loading.deleting = false;
        console.error('Error deleting category:', error);
      },
    });
  }

  deleteSubCategory(subCategory: SubCategory) {
    if (!confirm(`Are you sure you want to delete "${subCategory.Name}"?`)) {
      return;
    }

    this.loading.deleting = true;
    this.http
      .delete(`${this.apiUrl}/subcategories/${subCategory.Id}`)
      .subscribe({
        next: () => {
          this.subCategories = this.subCategories.filter(
            (sc) => sc.Id !== subCategory.Id
          );
          this.filterSubCategories();
          this.showMessage('Subcategory deleted successfully', 'success');
          this.loading.deleting = false;
        },
        error: (error) => {
          this.showMessage('Failed to delete subcategory', 'error');
          this.loading.deleting = false;
          console.error('Error deleting subcategory:', error);
        },
      });
  }

  // Utility
  getCategoryName(categoryId: string): string {
    const category = this.categories.find((c) => c.Id === categoryId);
    return category ? category.Name : 'Unknown';
  }

  showMessage(text: string, type: 'success' | 'error') {
    this.message = { text, type };
    setTimeout(() => {
      this.message = { text: '', type: '' };
    }, 3000);
  }
}
