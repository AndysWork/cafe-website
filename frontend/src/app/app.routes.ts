import { Routes } from '@angular/router';
import { HomeComponent } from './components/home/home.component';
import { CategoryUploadComponent } from './components/category-upload/category-upload.component';
import { CategoryCrudComponent } from './components/category-crud/category-crud.component';

export const routes: Routes = [
  { path: '', component: HomeComponent },
  { path: 'home', component: HomeComponent },
  { path: 'category/upload', component: CategoryUploadComponent },
  { path: 'category/crud', component: CategoryCrudComponent },
  { path: 'menu', component: HomeComponent }, // Placeholder for menu component
  { path: '**', redirectTo: '' }
];
