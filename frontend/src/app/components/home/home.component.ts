import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../../environments/environment';

@Component({
  selector: 'app-home',
  standalone: true,
  imports: [CommonModule, RouterModule],
  templateUrl: './home.component.html',
  styleUrls: ['./home.component.scss']
})
export class HomeComponent implements OnInit {
  latestMenuItems = [
    { name: 'Spicy Paneer Burger', category: 'Burgers', price: '₹149', image: '🍔', tag: 'NEW' },
    { name: 'Chocolate Milkshake', category: 'Beverages', price: '₹129', image: '🥤', tag: 'POPULAR' },
    { name: 'Veg Momos (8 pcs)', category: 'Momos', price: '₹99', image: '🥟', tag: 'BESTSELLER' },
    { name: 'English Breakfast', category: 'MTC Classic', price: '₹199', image: '🍳', tag: 'NEW' },
    { name: 'Herbal Green Tea', category: 'Tea', price: '₹79', image: '🍵', tag: 'HEALTHY' },
    { name: 'Cheesy Pasta', category: 'Pasta', price: '₹179', image: '🍝', tag: 'CHEF SPECIAL' }
  ];

  testimonials = [
    {
      name: 'Priya Sharma',
      rating: 5,
      text: 'Best cafe in town! The momos are absolutely delicious and the ambiance is perfect for hanging out with friends.',
      image: '👩',
      date: '2 days ago'
    },
    {
      name: 'Rahul Verma',
      rating: 5,
      text: 'Amazing food quality and quick service. The burgers are chef\'s kiss! Highly recommended for families.',
      image: '👨',
      date: '1 week ago'
    },
    {
      name: 'Anjali Patel',
      rating: 5,
      text: 'Love their beverages collection! The cold coffee is my go-to drink. Great value for money.',
      image: '👩‍🦰',
      date: '3 days ago'
    },
    {
      name: 'Vikram Singh',
      rating: 5,
      text: 'Cozy atmosphere and delicious food. The pasta is incredible! Perfect spot for a date night.',
      image: '🧔',
      date: '5 days ago'
    }
  ];

  foodPreparation = [
    { step: 'Fresh Ingredients', icon: '🥬', description: 'We source fresh, quality ingredients daily' },
    { step: 'Expert Chefs', icon: '👨‍🍳', description: 'Prepared by experienced culinary experts' },
    { step: 'Hygienic Kitchen', icon: '✨', description: 'Maintaining highest hygiene standards' },
    { step: 'Served Hot', icon: '🔥', description: 'Delivered fresh and hot to your table' }
  ];

  menuCategories = [
    { name: 'Burgers', icon: '🍔', count: '15+ varieties', color: '#FF6B6B' },
    { name: 'Momos', icon: '🥟', count: '8+ varieties', color: '#4ECDC4' },
    { name: 'Beverages', icon: '🥤', count: '20+ drinks', color: '#45B7D1' },
    { name: 'Pasta', icon: '🍝', count: '12+ varieties', color: '#FFA07A' },
    { name: 'Sandwiches', icon: '🥪', count: '10+ varieties', color: '#98D8C8' },
    { name: 'Tea & Coffee', icon: '☕', count: '15+ flavors', color: '#D4A574' }
  ];

  stats = [
    { value: '500+', label: 'Happy Customers', icon: '😊' },
    { value: '50+', label: 'Menu Items', icon: '🍽️' },
    { value: '5⭐', label: 'Average Rating', icon: '⭐' },
    { value: '3+', label: 'Years Serving', icon: '🎉' }
  ];

  experienceVideos = [
    { title: 'Customer Experience', thumbnail: '🎬', description: 'Watch our happy customers' },
    { title: 'Kitchen Tour', thumbnail: '🏪', description: 'Behind the scenes' },
    { title: 'Food Preparation', thumbnail: '👨‍🍳', description: 'How we make magic' }
  ];

  gallery = [
    { image: '🍔', caption: 'Signature Burgers' },
    { image: '🥟', caption: 'Fresh Momos' },
    { image: '🍝', caption: 'Creamy Pasta' },
    { image: '☕', caption: 'Aromatic Coffee' },
    { image: '🥪', caption: 'Grilled Sandwiches' },
    { image: '🍰', caption: 'Delicious Desserts' }
  ];

  currentTestimonialIndex = 0;

  constructor(private http: HttpClient) {}

  ngOnInit() {
    this.startTestimonialRotation();
  }

  startTestimonialRotation() {
    setInterval(() => {
      this.currentTestimonialIndex = (this.currentTestimonialIndex + 1) % this.testimonials.length;
    }, 5000);
  }

  getStars(rating: number): string[] {
    return Array(rating).fill('⭐');
  }

  scrollToSection(sectionId: string) {
    const element = document.getElementById(sectionId);
    element?.scrollIntoView({ behavior: 'smooth' });
  }
}
