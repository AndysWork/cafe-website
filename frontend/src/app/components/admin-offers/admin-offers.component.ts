import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { OffersService, Offer } from '../../services/offers.service';

@Component({
  selector: 'app-admin-offers',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './admin-offers.component.html',
  styleUrls: ['./admin-offers.component.scss']
})
export class AdminOffersComponent implements OnInit {
  offers: Offer[] = [];
  loading = true;
  showModal = false;
  isEditMode = false;
  currentOffer: Offer | null = null;

  offerForm: Offer = this.getEmptyOffer();

  constructor(private offersService: OffersService) {}

  ngOnInit() {
    this.loadOffers();
  }

  loadOffers() {
    this.loading = true;
    this.offersService.getAllOffers().subscribe({
      next: (offers) => {
        this.offers = offers;
        this.loading = false;
      },
      error: (err) => {
        console.error('Error loading offers:', err);
        alert('Failed to load offers');
        this.loading = false;
      }
    });
  }

  getEmptyOffer(): Offer {
    const now = new Date();
    const later = new Date();
    later.setDate(later.getDate() + 30);

    return {
      title: '',
      description: '',
      discountType: 'percentage',
      discountValue: 0,
      code: '',
      icon: '🎁',
      minOrderAmount: undefined,
      maxDiscount: undefined,
      validFrom: now,
      validTill: later,
      isActive: true,
      usageLimit: undefined,
      usageCount: 0,
      applicableCategories: []
    };
  }

  openCreateModal() {
    this.isEditMode = false;
    this.offerForm = this.getEmptyOffer();
    this.showModal = true;
  }

  openEditModal(offer: Offer) {
    this.isEditMode = true;
    this.currentOffer = offer;
    this.offerForm = { ...offer };
    this.showModal = true;
  }

  closeModal() {
    this.showModal = false;
    this.currentOffer = null;
    this.offerForm = this.getEmptyOffer();
  }

  saveOffer() {
    if (this.isEditMode && this.currentOffer?.id) {
      this.offersService.updateOffer(this.currentOffer.id, this.offerForm).subscribe({
        next: () => {
          alert('Offer updated successfully!');
          this.loadOffers();
          this.closeModal();
        },
        error: (err) => {
          console.error('Error updating offer:', err);
          alert('Failed to update offer');
        }
      });
    } else {
      this.offersService.createOffer(this.offerForm).subscribe({
        next: () => {
          alert('Offer created successfully!');
          this.loadOffers();
          this.closeModal();
        },
        error: (err) => {
          console.error('Error creating offer:', err);
          alert('Failed to create offer');
        }
      });
    }
  }

  deleteOffer(id: string) {
    if (confirm('Are you sure you want to delete this offer?')) {
      this.offersService.deleteOffer(id).subscribe({
        next: () => {
          alert('Offer deleted successfully!');
          this.loadOffers();
        },
        error: (err) => {
          console.error('Error deleting offer:', err);
          alert('Failed to delete offer');
        }
      });
    }
  }

  toggleActive(offer: Offer) {
    if (offer.id) {
      const updated = { ...offer, isActive: !offer.isActive };
      this.offersService.updateOffer(offer.id, updated).subscribe({
        next: () => {
          this.loadOffers();
        },
        error: (err) => {
          console.error('Error toggling offer status:', err);
          alert('Failed to update offer status');
        }
      });
    }
  }

  getDiscountDisplay(offer: Offer): string {
    switch (offer.discountType) {
      case 'percentage':
        return `${offer.discountValue}%`;
      case 'flat':
        return `₹${offer.discountValue}`;
      case 'bogo':
        return 'BOGO';
      default:
        return '-';
    }
  }

  formatDate(date: Date): string {
    return new Date(date).toLocaleDateString();
  }
}
