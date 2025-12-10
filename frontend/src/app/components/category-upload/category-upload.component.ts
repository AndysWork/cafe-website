import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpClient, HttpEventType } from '@angular/common/http';
import { environment } from '../../../environments/environment';

interface UploadResult {
  success: boolean;
  categoriesProcessed: number;
  subCategoriesProcessed: number;
  errors: string[];
  message: string;
}

@Component({
  selector: 'app-category-upload',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './category-upload.component.html',
  styleUrls: ['./category-upload.component.scss']
})
export class CategoryUploadComponent {
  selectedFile: File | null = null;
  uploadProgress: number = 0;
  isUploading: boolean = false;
  uploadResult: UploadResult | null = null;
  errorMessage: string = '';
  isDragging: boolean = false;

  constructor(private http: HttpClient) {}

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
      this.errorMessage = 'Invalid file type. Please upload .xlsx, .xls, or .csv files only.';
      this.selectedFile = null;
      return;
    }

    if (file.size > 5 * 1024 * 1024) { // 5MB limit
      this.errorMessage = 'File size exceeds 5MB limit.';
      this.selectedFile = null;
      return;
    }

    this.selectedFile = file;
    this.errorMessage = '';
    this.uploadResult = null;
  }

  uploadFile(): void {
    if (!this.selectedFile) {
      this.errorMessage = 'Please select a file first.';
      return;
    }

    this.isUploading = true;
    this.uploadProgress = 0;
    this.uploadResult = null;
    this.errorMessage = '';

    const formData = new FormData();
    formData.append('file', this.selectedFile);
    formData.append('uploadedBy', 'Admin'); // You can get this from auth service

    const apiUrl = `${environment.apiUrl}/upload/categories`;

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
        }
      },
      error: (error) => {
        this.errorMessage = error.error?.error || 'Upload failed. Please try again.';
        this.isUploading = false;
        this.uploadProgress = 0;
      }
    });
  }

  downloadTemplate(format: 'csv' | 'excel'): void {
    const apiUrl = `${environment.apiUrl}/upload/categories/template?format=${format}`;
    window.open(apiUrl, '_blank');
  }

  clearFile(): void {
    this.selectedFile = null;
    this.uploadResult = null;
    this.errorMessage = '';
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
