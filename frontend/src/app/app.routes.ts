import { Routes } from '@angular/router';
import { CategoryUploadComponent } from './components/category-upload/category-upload.component';

export const routes: Routes = [
  { path: '', redirectTo: '/upload', pathMatch: 'full' },
  { path: 'upload', component: CategoryUploadComponent }
];
