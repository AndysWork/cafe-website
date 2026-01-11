import { Component, OnInit, OnDestroy, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { DiscountCouponService, DiscountCoupon } from '../../services/discount-coupon.service';
import { OutletService } from '../../services/outlet.service';
import { Subscription } from 'rxjs';
import { filter } from 'rxjs/operators';

@Component({
  selector: 'app-discount-mapping',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './discount-mapping.component.html',
  styleUrls: ['./discount-mapping.component.scss']
})
export class DiscountMappingComponent implements OnInit, OnDestroy {
  private outletService = inject(OutletService);
  private outletSubscription?: Subscription;

  coupons: DiscountCoupon[] = [];
  loading = true;
  error: string | null = null;

  // Filtered views
  zomatoCoupons: DiscountCoupon[] = [];
  swiggyCoupons: DiscountCoupon[] = [];

  constructor(private couponService: DiscountCouponService) {}

  ngOnInit() {
    // Subscribe to outlet changes
    this.outletSubscription = this.outletService.selectedOutlet$
      .pipe(filter(outlet => outlet !== null))
      .subscribe(() => {
        this.loadDiscountCoupons();
      });

    // Load immediately if outlet is already selected
    if (this.outletService.getSelectedOutlet()) {
      this.loadDiscountCoupons();
    }
  }

  ngOnDestroy(): void {
    this.outletSubscription?.unsubscribe();
  }

  loadDiscountCoupons() {
    this.loading = true;
    this.error = null;

    this.couponService.getDiscountCoupons().subscribe({
      next: (response) => {
        this.coupons = response.data;
        this.zomatoCoupons = this.coupons.filter(c => c.platform === 'Zomato');
        this.swiggyCoupons = this.coupons.filter(c => c.platform === 'Swiggy');
        this.loading = false;
      },
      error: (err) => {
        console.error('Error loading discount coupons:', err);
        this.error = 'Failed to load discount coupons';
        this.loading = false;
      }
    });
  }

  formatCurrency(amount: number): string {
    return '₹' + amount.toFixed(2);
  }

  formatDate(date: string): string {
    return new Date(date).toLocaleDateString('en-IN', {
      day: '2-digit',
      month: 'short',
      year: 'numeric'
    });
  }

  getTotalUsageCount(coupons: DiscountCoupon[]): number {
    return coupons.reduce((sum, coupon) => sum + coupon.usageCount, 0);
  }

  getTotalDiscountAmount(coupons: DiscountCoupon[]): number {
    return coupons.reduce((sum, coupon) => sum + coupon.totalDiscountAmount, 0);
  }

  toggleCouponStatus(coupon: DiscountCoupon) {
    const newStatus = !coupon.isActive;
    const action = newStatus ? 'activate' : 'deactivate';

    if (!confirm(`Are you sure you want to ${action} the coupon "${coupon.couponCode}" for ${coupon.platform}?`)) {
      return;
    }

    this.couponService.updateCouponStatus(coupon.couponCode, coupon.platform, newStatus)
      .subscribe({
        next: (response) => {
          if (response.success) {
            coupon.isActive = newStatus;
            alert(response.message || `Coupon ${action}d successfully!`);
          }
        },
        error: (err) => {
          console.error('Error updating coupon status:', err);
          alert('Failed to update coupon status. Please try again.');
        }
      });
  }

  updateMaxValue(coupon: DiscountCoupon) {
    const inputValue = prompt(
      `Enter max discount value for "${coupon.couponCode}" (leave empty for unlimited):`,
      coupon.maxValue?.toString() || ''
    );

    if (inputValue === null) return; // User cancelled

    const maxValue = inputValue.trim() === '' ? null : parseFloat(inputValue);

    if (maxValue !== null && (isNaN(maxValue) || maxValue < 0 || maxValue > 10000)) {
      alert('Please enter a valid number between 0 and 10000, or leave empty for unlimited');
      return;
    }

    // If coupon has an ID, use the existing endpoint
    if (coupon.id) {
      this.couponService.updateCouponMaxValue(coupon.id, maxValue)
        .subscribe({
          next: (response) => {
            if (response.success) {
              coupon.maxValue = maxValue === null ? undefined : maxValue;
              alert(response.message || 'Max value updated successfully!');
            }
          },
          error: (err) => {
            console.error('Error updating max value:', err);
            alert('Failed to update max value. Please try again.');
          }
        });
    } else {
      // If no ID exists, create/update the coupon record first (which will assign an ID)
      // Then update the max value
      this.couponService.updateCouponStatus(coupon.couponCode, coupon.platform, coupon.isActive ?? true)
        .subscribe({
          next: (statusResponse) => {
            // Reload to get the ID, then update max value
            this.loadDiscountCoupons();

            // Give it a moment to reload, then prompt to try again
            setTimeout(() => {
              const reloadedCoupon = [...this.zomatoCoupons, ...this.swiggyCoupons]
                .find(c => c.couponCode === coupon.couponCode && c.platform === coupon.platform);

              if (reloadedCoupon?.id) {
                this.couponService.updateCouponMaxValue(reloadedCoupon.id, maxValue)
                  .subscribe({
                    next: (response) => {
                      if (response.success) {
                        coupon.maxValue = maxValue === null ? undefined : maxValue;
                        reloadedCoupon.maxValue = maxValue === null ? undefined : maxValue;
                        alert(response.message || 'Max value updated successfully!');
                      }
                    },
                    error: (err) => {
                      console.error('Error updating max value:', err);
                      alert('Failed to update max value. Please try again.');
                    }
                  });
              } else {
                alert('Could not retrieve coupon ID. Please refresh and try again.');
              }
            }, 1000);
          },
          error: (err) => {
            console.error('Error initializing coupon record:', err);
            alert('Failed to initialize coupon record. Please try again.');
          }
        });
    }
  }

  updateDiscountPercentage(coupon: DiscountCoupon) {
    const inputValue = prompt(
      `Enter discount percentage for "${coupon.couponCode}" (leave empty to unset):`,
      coupon.discountPercentage?.toString() || ''
    );

    if (inputValue === null) return; // User cancelled

    const discountPercentage = inputValue.trim() === '' ? null : parseFloat(inputValue);

    if (discountPercentage !== null && (isNaN(discountPercentage) || discountPercentage < 0 || discountPercentage > 100)) {
      alert('Please enter a valid percentage between 0 and 100, or leave empty to unset');
      return;
    }

    // If coupon has an ID, use the existing endpoint
    if (coupon.id) {
      this.couponService.updateCouponDiscountPercentage(coupon.id, discountPercentage)
        .subscribe({
          next: (response) => {
            if (response.success) {
              coupon.discountPercentage = discountPercentage === null ? undefined : discountPercentage;
              alert(response.message || 'Discount percentage updated successfully!');
            }
          },
          error: (err) => {
            console.error('Error updating discount percentage:', err);
            alert('Failed to update discount percentage. Please try again.');
          }
        });
    } else {
      // If no ID exists, create/update the coupon record first
      this.couponService.updateCouponStatus(coupon.couponCode, coupon.platform, coupon.isActive ?? true)
        .subscribe({
          next: (statusResponse) => {
            this.loadDiscountCoupons();

            setTimeout(() => {
              const reloadedCoupon = [...this.zomatoCoupons, ...this.swiggyCoupons]
                .find(c => c.couponCode === coupon.couponCode && c.platform === coupon.platform);

              if (reloadedCoupon?.id) {
                this.couponService.updateCouponDiscountPercentage(reloadedCoupon.id, discountPercentage)
                  .subscribe({
                    next: (response) => {
                      if (response.success) {
                        coupon.discountPercentage = discountPercentage === null ? undefined : discountPercentage;
                        reloadedCoupon.discountPercentage = discountPercentage === null ? undefined : discountPercentage;
                        alert(response.message || 'Discount percentage updated successfully!');
                      }
                    },
                    error: (err) => {
                      console.error('Error updating discount percentage:', err);
                      alert('Failed to update discount percentage. Please try again.');
                    }
                  });
              } else {
                alert('Could not retrieve coupon ID. Please refresh and try again.');
              }
            }, 1000);
          },
          error: (err) => {
            console.error('Error initializing coupon record:', err);
            alert('Failed to initialize coupon record. Please try again.');
          }
        });
    }
  }
}
