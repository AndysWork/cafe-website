import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';

@Component({
  selector: 'app-home',
  standalone: true,
  imports: [CommonModule, RouterModule],
  templateUrl: './home.component.html',
  styleUrls: ['./home.component.scss']
})
export class HomeComponent {
  features = [
    {
      icon: '🍽️',
      title: 'Menu Management',
      description: 'Easily manage your cafe menu items, prices, and availability',
      link: '/menu'
    },
    {
      icon: '📁',
      title: 'Category Organization',
      description: 'Organize your menu with categories and subcategories',
      link: '/category/crud'
    },
    {
      icon: '📤',
      title: 'Bulk Upload',
      description: 'Upload multiple categories via CSV or Excel files',
      link: '/category/upload'
    },
    {
      icon: '📊',
      title: 'Analytics',
      description: 'Track your menu performance and popular items',
      link: '#'
    }
  ];

  stats = [
    { value: '500+', label: 'Menu Items' },
    { value: '50+', label: 'Categories' },
    { value: '24/7', label: 'Availability' },
    { value: '99%', label: 'Uptime' }
  ];
}
