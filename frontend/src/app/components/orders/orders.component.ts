import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'app-orders',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './orders.component.html',
  styleUrls: ['./orders.component.scss']
})
export class OrdersComponent {
  orders = [
    {
      id: '#ORD001',
      date: 'Dec 10, 2025',
      items: 'Paneer Burger, Cold Coffee',
      total: '₹278',
      status: 'Delivered'
    },
    {
      id: '#ORD002',
      date: 'Dec 9, 2025',
      items: 'Veg Momos (8 pcs)',
      total: '₹99',
      status: 'Delivered'
    },
    {
      id: '#ORD003',
      date: 'Dec 11, 2025',
      items: 'Cheesy Pasta, Herbal Tea',
      total: '₹258',
      status: 'In Progress'
    }
  ];
}
