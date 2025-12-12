import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient, HttpEventType } from '@angular/common/http';
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

interface UploadResult {
  Success: boolean;
  CategoriesProcessed: number;
  SubCategoriesProcessed: number;
  Errors: string[];
  Message: string;
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
    id: '',
    categoryId: '',
    name: '',
  };
  editingCategory: Category | null = null;
  editingSubCategory: SubCategory | null = null;

  // Upload properties
  showUploadModal = false;
  selectedFile: File | null = null;
  uploadProgress: number = 0;
  isUploading: boolean = false;
  uploadResult: UploadResult | null = null;
  uploadError: string = '';
  isDragging: boolean = false;

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
        console.log('Categories loaded - raw data:', data);
        console.log('Categories count:', data?.length);
        console.log('First category:', data?.[0]);
        this.categories = data.map((c) => ({ ...c, isEditing: false }));
        this.loading.categories = false;
        console.log('Mapped categories:', this.categories);
        console.log('Categories array length:', this.categories.length);
      },
      error: (error) => {
        console.error('Error loading categories:', error);
        console.error('Error details:', JSON.stringify(error, null, 2));
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
        console.log('SubCategories loaded - raw data:', data);
        console.log('SubCategories count:', data?.length);
        console.log('First subcategory:', data?.[0]);
        this.subCategories = data.map((sc) => ({ ...sc, isEditing: false }));
        this.filteredSubCategories = this.subCategories;
        this.loading.subCategories = false;
        console.log('Mapped subcategories:', this.subCategories);
        console.log('SubCategories array length:', this.subCategories.length);
      },
      error: (error) => {
        console.error('Error loading subcategories:', error);
        console.error('Error details:', JSON.stringify(error, null, 2));
        this.showMessage('Failed to load subcategories', 'error');
        this.loading.subCategories = false;
      },
    });
  }

  // Filter SubCategories by Category
  filterSubCategories() {
    if (this.selectedCategoryId) {
      this.filteredSubCategories = this.subCategories.filter(
        (sc) => sc.categoryId === this.selectedCategoryId
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
      .post<Category>(`${this.apiUrl}/categories`, { name: this.newCategory })
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
    if (!this.newSubCategory.categoryId || !this.newSubCategory.name.trim()) {
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
          this.newSubCategory = { id: '', categoryId: '', name: '' };
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
    if (!this.editingCategory || !this.editingCategory.name.trim()) {
      this.showMessage('Please enter a valid name', 'error');
      return;
    }

    this.loading.saving = true;
    this.http
      .put(`${this.apiUrl}/categories/${category.id}`, {
        name: this.editingCategory.name,
      })
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
        },
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
      name: this.editingSubCategory.name,
    };

    this.http
      .put(`${this.apiUrl}/subcategories/${subCategory.id}`, payload)
      .subscribe({
        next: () => {
          subCategory.name = this.editingSubCategory!.name;
          subCategory.categoryId = this.editingSubCategory!.categoryId;
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
    if (!confirm(`Are you sure you want to delete "${category.name}"?`)) {
      return;
    }

    this.loading.deleting = true;
    this.http.delete(`${this.apiUrl}/categories/${category.id}`).subscribe({
      next: () => {
        this.categories = this.categories.filter((c) => c.id !== category.id);
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
    if (!confirm(`Are you sure you want to delete "${subCategory.name}"?`)) {
      return;
    }

    this.loading.deleting = true;
    this.http
      .delete(`${this.apiUrl}/subcategories/${subCategory.id}`)
      .subscribe({
        next: () => {
          this.subCategories = this.subCategories.filter(
            (sc) => sc.id !== subCategory.id
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
    const category = this.categories.find((c) => c.id === categoryId);
    return category ? category.name : 'Unknown';
  }

  showMessage(text: string, type: 'success' | 'error') {
    this.message = { text, type };
    setTimeout(() => {
      this.message = { text: '', type: '' };
    }, 3000);
  }

  // Upload methods
  openUploadModal(): void {
    this.showUploadModal = true;
    this.selectedFile = null;
    this.uploadResult = null;
    this.uploadError = '';
    this.uploadProgress = 0;
  }

  closeUploadModal(): void {
    this.showUploadModal = false;
    this.selectedFile = null;
    this.uploadResult = null;
    this.uploadError = '';
  }

  onDragOver(event: DragEvent): void {
    event.preventDefault();
    event.stopPropagation();
    this.isDragging = true;
  }

  onDragLeave(event: DragEvent): void {
    event.preventDefault();
    event.stopPropagation();
    this.isDragging = false;
  }

  onDrop(event: DragEvent): void {
    event.preventDefault();
    event.stopPropagation();
    this.isDragging = false;

    const files = event.dataTransfer?.files;
    if (files && files.length > 0) {
      this.handleFile(files[0]);
    }
  }

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    if (input.files && input.files.length > 0) {
      this.handleFile(input.files[0]);
    }
  }

  private handleFile(file: File): void {
    const validExtensions = ['.xlsx', '.xls', '.csv'];
    const fileExtension = file.name.toLowerCase().substring(file.name.lastIndexOf('.'));

    if (!validExtensions.includes(fileExtension)) {
      this.uploadError = 'Invalid file type. Please upload .xlsx, .xls, or .csv files only.';
      this.selectedFile = null;
      return;
    }

    if (file.size > 5 * 1024 * 1024) {
      this.uploadError = 'File size exceeds 5MB limit.';
      this.selectedFile = null;
      return;
    }

    this.selectedFile = file;
    this.uploadError = '';
    this.uploadResult = null;
  }

  uploadFile(): void {
    if (!this.selectedFile) {
      this.uploadError = 'Please select a file first.';
      return;
    }

    this.isUploading = true;
    this.uploadProgress = 0;
    this.uploadResult = null;
    this.uploadError = '';

    const formData = new FormData();
    formData.append('file', this.selectedFile);
    formData.append('uploadedBy', 'Admin');

    const apiUrl = `${this.apiUrl}/upload/categories`;

    this.http.post<UploadResult>(apiUrl, formData, {
      reportProgress: true,
      observe: 'events'
    }).subscribe({
      next: (event) => {
        if (event.type === HttpEventType.UploadProgress && event.total) {
          this.uploadProgress = Math.round((100 * event.loaded) / event.total);
        } else if (event.type === HttpEventType.Response) {
          this.uploadResult = event.body;
          this.isUploading = false;
          this.selectedFile = null;
          this.uploadProgress = 0;
          // Reload categories and subcategories after successful upload
          this.loadCategories();
          this.loadSubCategories();
        }
      },
      error: (error) => {
        this.uploadError = error.error?.error || error.error?.message || error.statusText || 'Upload failed. Please try again.';
        this.isUploading = false;
        this.uploadProgress = 0;
      }
    });
  }

  downloadTemplate(format: 'csv' | 'excel'): void {
    const apiUrl = `${this.apiUrl}/upload/categories/template?format=${format}`;
    window.open(apiUrl, '_blank');
  }

  clearFile(): void {
    this.selectedFile = null;
    this.uploadResult = null;
    this.uploadError = '';
    this.uploadProgress = 0;
  }

  getFileIcon(): string {
    if (!this.selectedFile) return '';
    const ext = this.selectedFile.name.toLowerCase().substring(this.selectedFile.name.lastIndexOf('.'));
    return ext === '.csv' ? '📄' : '📊';
  }

  formatFileSize(bytes: number): string {
    if (bytes === 0) return '0 Bytes';
    const k = 1024;
    const sizes = ['Bytes', 'KB', 'MB'];
    const i = Math.floor(Math.log(bytes) / Math.log(k));
    return Math.round(bytes / Math.pow(k, i) * 100) / 100 + ' ' + sizes[i];
  }
}
