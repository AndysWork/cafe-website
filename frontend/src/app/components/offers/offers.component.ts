import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'app-offers',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './offers.component.html',
  styleUrls: ['./offers.component.scss']
})
export class OffersComponent {
  offers = [
    {
      title: 'Burger Combo Blast',
      description: 'Get 2 Burgers + 2 Beverages at 30% OFF',
      discount: '30% OFF',
      code: 'BURGER30',
      validTill: 'Valid till Dec 31, 2025',
      icon: '🍔'
    },
    {
      title: 'Tea Lover Special',
      description: 'Buy 2 Teas, Get 1 Free',
      discount: 'BOGO',
      code: 'TEA2FOR1',
      validTill: 'Valid till Dec 25, 2025',
      icon: '🍵'
    },
    {
      title: 'Weekend Feast',
      description: 'Flat ₹100 OFF on orders above ₹500',
      discount: '₹100 OFF',
      code: 'WEEKEND100',
      validTill: 'Valid on Sat & Sun',
      icon: '🎉'
    },
    {
      title: 'First Order Delight',
      description: 'Get 50% OFF on your first order',
      discount: '50% OFF',
      code: 'FIRST50',
      validTill: 'One time use',
      icon: '⭐'
    }
  ];

  copyCode(code: string) {
    navigator.clipboard.writeText(code);
    alert(`Code "${code}" copied to clipboard!`);
  }
}
