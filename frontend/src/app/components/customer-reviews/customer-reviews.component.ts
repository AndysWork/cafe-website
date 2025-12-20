import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReviewsService, Review } from '../../services/reviews.service';

@Component({
  selector: 'app-customer-reviews',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './customer-reviews.component.html',
  styleUrls: ['./customer-reviews.component.scss']
})
export class CustomerReviewsComponent implements OnInit {
  reviews: Review[] = [];
  isLoading = true;
  errorMessage = '';
  currentIndex = 0;
  itemsPerPage = 3;

  // Expose Math to template
  Math = Math;

  constructor(private reviewsService: ReviewsService) {}

  ngOnInit(): void {
    this.loadReviews();
  }

  loadReviews(): void {
    this.isLoading = true;
    this.reviewsService.getFiveStarReviews(20).subscribe({
      next: (response) => {
        if (response.success) {
          this.reviews = response.data;
        }
        this.isLoading = false;
      },
      error: (error) => {
        console.error('Error loading reviews:', error);
        this.errorMessage = 'Failed to load reviews';
        this.isLoading = false;
      }
    });
  }

  get visibleReviews(): Review[] {
    return this.reviews.slice(this.currentIndex, this.currentIndex + this.itemsPerPage);
  }

  get canGoNext(): boolean {
    return this.currentIndex + this.itemsPerPage < this.reviews.length;
  }

  get canGoPrevious(): boolean {
    return this.currentIndex > 0;
  }

  nextReviews(): void {
    if (this.canGoNext) {
      this.currentIndex += this.itemsPerPage;
    }
  }

  previousReviews(): void {
    if (this.canGoPrevious) {
      this.currentIndex -= this.itemsPerPage;
    }
  }

  formatDate(date: Date): string {
    return new Date(date).toLocaleDateString('en-IN', {
      day: 'numeric',
      month: 'short',
      year: 'numeric'
    });
  }

  getItemsList(items: Array<{quantity: number; itemName: string}>): string {
    return items.map(item => `${item.quantity}x ${item.itemName}`).join(', ');
  }
}
