import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../../environments/environment';

@Component({
  selector: 'app-menu-upload',
  imports: [CommonModule, FormsModule],
  templateUrl: './menu-upload.component.html',
  styleUrl: './menu-upload.component.scss'
})
export class MenuUploadComponent {
  selectedFile: File | null = null;
  uploading = false;
  uploadResult: any = null;
  error: string | null = null;
  dragOver = false;
  clearExisting = false;

  constructor(private http: HttpClient) {}

  onFileSelected(event: any): void {
    const file = event.target.files[0];
    if (file && this.isValidFile(file)) {
      this.selectedFile = file;
      this.error = null;
      this.uploadResult = null;
    } else {
      this.error = 'Please select a valid Excel file (.xlsx or .xls)';
      this.selectedFile = null;
    }
  }

  onDragOver(event: DragEvent): void {
    event.preventDefault();
    event.stopPropagation();
    this.dragOver = true;
  }

  onDragLeave(event: DragEvent): void {
    event.preventDefault();
    event.stopPropagation();
    this.dragOver = false;
  }

  onDrop(event: DragEvent): void {
    event.preventDefault();
    event.stopPropagation();
    this.dragOver = false;

    const files = event.dataTransfer?.files;
    if (files && files.length > 0) {
      const file = files[0];
      if (this.isValidFile(file)) {
        this.selectedFile = file;
        this.error = null;
        this.uploadResult = null;
      } else {
        this.error = 'Please select a valid Excel file (.xlsx or .xls)';
      }
    }
  }

  isValidFile(file: File): boolean {
    const validExtensions = ['.xlsx', '.xls'];
    const fileName = file.name.toLowerCase();
    return validExtensions.some(ext => fileName.endsWith(ext));
  }

  uploadFile(): void {
    if (!this.selectedFile) {
      this.error = 'Please select a file first';
      return;
    }

    this.uploading = true;
    this.error = null;
    this.uploadResult = null;

    const formData = new FormData();
    formData.append('file', this.selectedFile);

    const url = this.clearExisting
      ? `${environment.apiUrl}/menu/upload?clearExisting=true`
      : `${environment.apiUrl}/menu/upload`;

    this.http.post(url, formData)
      .subscribe({
        next: (response: any) => {
          this.uploading = false;
          this.uploadResult = response;
          this.selectedFile = null;
        },
        error: (error) => {
          this.uploading = false;
          this.error = error.error?.error || 'Failed to upload file. Please try again.';
          console.error('Upload error:', error);
        }
      });
  }

  clearSelection(): void {
    this.selectedFile = null;
    this.uploadResult = null;
    this.error = null;
  }

  downloadTemplate(): void {
    // Create a sample template with example data
    const template =
      'category_name,subcategory_name,catalogue_name,variant_name,current_price,description\n' +
      'Beverages,Hot Drinks,Cappuccino,250ml,120,Classic Italian coffee with steamed milk foam\n' +
      'Beverages,Hot Drinks,Cappuccino,350ml,150,Classic Italian coffee with steamed milk foam\n' +
      'Beverages,Cold Drinks,Iced Latte,Regular,130,Refreshing iced latte with milk\n' +
      'Beverages,Cold Drinks,Iced Latte,Large,160,Refreshing iced latte with milk\n' +
      'Food,Breakfast,Sandwich,Regular,80,Grilled vegetable sandwich\n';

    const blob = new Blob([template], { type: 'text/csv' });
    const url = window.URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = 'menu_upload_template.csv';
    a.click();
    window.URL.revokeObjectURL(url);
  }
}
