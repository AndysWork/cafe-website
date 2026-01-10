import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { OutletService } from '../../services/outlet.service';
import { AuthService } from '../../services/auth.service';
import { Outlet } from '../../models/outlet.model';

@Component({
  selector: 'app-outlet-selector',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="outlet-selector" *ngIf="isVisible">
      <!-- <label for="outlet-select" class="outlet-label">
        <i class="fas fa-store"></i>
      </label> -->
      <select
        id="outlet-select"
        class="outlet-dropdown"
        [(ngModel)]="selectedOutletId"
        (change)="onOutletChange()"
        [disabled]="outlets.length <= 1">
        <option [value]="''" disabled>Select an outlet</option>
        <option
          *ngFor="let outlet of outlets"
          [value]="outlet._id || outlet.id">
          {{ outlet.outletCode }} - {{ outlet.outletName }}
        </option>
      </select>
    </div>
  `,
  styles: [`
    .outlet-selector {
      display: flex;
      align-items: center;
      gap: 0.5rem;
      padding: 0.4rem 0.75rem;
      background-color: #f8f9fa;
      border-radius: 0.375rem;
      border: 1px solid #dee2e6;
    }

    .outlet-label {
      display: flex;
      align-items: center;
      gap: 0.35rem;
      font-weight: 600;
      color: #495057;
      font-size: 0.8rem;
      margin: 0;
      white-space: nowrap;
    }

    .outlet-label i {
      color: #6c757d;
      font-size: 0.9rem;
    }

    .outlet-dropdown {
      padding: 0.4rem 0.75rem;
      border: 1px solid #ced4da;
      border-radius: 0.25rem;
      font-size: 0.8rem;
      background-color: white;
      cursor: pointer;
      transition: border-color 0.15s ease-in-out;
      min-width: 150px;
      max-width: 180px;
    }

    .outlet-dropdown:hover:not(:disabled) {
      border-color: #80bdff;
    }

    .outlet-dropdown:focus {
      border-color: #80bdff;
      outline: 0;
      box-shadow: 0 0 0 0.2rem rgba(0, 123, 255, 0.25);
    }

    .outlet-dropdown:disabled {
      background-color: #e9ecef;
      cursor: not-allowed;
      opacity: 0.6;
    }

    @media (max-width: 768px) {
      .outlet-selector {
        flex-direction: column;
        align-items: stretch;
        gap: 0.5rem;
      }

      .outlet-dropdown {
        width: 100%;
      }
    }
  `]
})
export class OutletSelectorComponent implements OnInit {
  private outletService = inject(OutletService);
  private authService = inject(AuthService);

  outlets: Outlet[] = [];
  selectedOutletId: string = '';
  isVisible: boolean = false;

  ngOnInit(): void {
    // Only show selector if user is logged in
    this.authService.currentUser$.subscribe(user => {
      this.isVisible = !!user;
      if (user && user.assignedOutlets && user.assignedOutlets.length > 0) {
        // Initialize outlets from user data
        this.outletService.initializeOutlets(user.assignedOutlets);
        this.loadOutlets();
      }
    });

    // Subscribe to available outlets
    this.outletService.availableOutlets$.subscribe(outlets => {
      this.outlets = outlets;
    });

    // Subscribe to selected outlet
    this.outletService.selectedOutlet$.subscribe(outlet => {
      if (outlet) {
        this.selectedOutletId = outlet._id || outlet.id || '';
      }
    });
  }

  private loadOutlets(): void {
    // Load active outlets for the user
    this.outletService.getActiveOutlets().subscribe({
      next: (outlets) => {
        this.outlets = outlets;

        // Auto-select first outlet if none is selected
        if (!this.selectedOutletId && outlets.length > 0) {
          this.selectedOutletId = outlets[0]._id || outlets[0].id || '';
          this.onOutletChange();
        }
      },
      error: (error) => {
        console.error('Error loading outlets:', error);
      }
    });
  }

  onOutletChange(): void {
    const selected = this.outlets.find(o => (o._id || o.id) === this.selectedOutletId);
    if (selected) {
      this.outletService.selectOutlet(selected);
      // Note: Components should subscribe to selectedOutlet$ and react to changes
      // Removed page reload for better UX
    }
  }
}
