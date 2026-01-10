import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, RouterModule } from '@angular/router';
import { OutletService } from '../../services/outlet.service';
import { Outlet } from '../../models/outlet.model';

@Component({
  selector: 'app-outlet-management',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule],
  templateUrl: './outlet-management.component.html',
  styleUrls: ['./outlet-management.component.scss']
})
export class OutletManagementComponent implements OnInit {
  private outletService = inject(OutletService);
  private router = inject(Router);

  outlets: Outlet[] = [];
  isLoading = false;
  errorMessage = '';
  successMessage = '';

  // Modal state
  showModal = false;
  modalMode: 'create' | 'edit' = 'create';
  selectedOutlet: Outlet | null = null;

  // Form data
  outletForm: Partial<Outlet> = this.getEmptyForm();

  ngOnInit(): void {
    this.loadOutlets();
  }

  private getEmptyForm(): Partial<Outlet> {
    return {
      outletCode: '',
      outletName: '',
      address: '',
      city: '',
      state: '',
      phoneNumber: '',
      email: '',
      managerName: '',
      isActive: true,
      settings: {
        openingTime: '08:00',
        closingTime: '22:00',
        acceptsOnlineOrders: true,
        acceptsDineIn: true,
        acceptsTakeaway: true,
        taxPercentage: 5,
        deliveryRadius: undefined,
        minimumOrderAmount: undefined
      }
    };
  }

  loadOutlets(): void {
    this.isLoading = true;
    this.outletService.getAllOutlets().subscribe({
      next: (outlets) => {
        this.outlets = outlets;
        this.isLoading = false;
      },
      error: (error) => {
        this.errorMessage = 'Failed to load outlets';
        this.isLoading = false;
        console.error('Error loading outlets:', error);
      }
    });
  }

  openCreateModal(): void {
    this.modalMode = 'create';
    this.outletForm = this.getEmptyForm();
    this.selectedOutlet = null;
    this.showModal = true;
  }

  openEditModal(outlet: Outlet): void {
    this.modalMode = 'edit';
    this.selectedOutlet = outlet;
    // Ensure ID is available in both formats for compatibility
    if (outlet.id && !outlet._id) {
      outlet._id = outlet.id;
    }
    this.outletForm = { ...outlet };
    console.log('Editing outlet:', this.selectedOutlet);
    this.showModal = true;
  }

  closeModal(): void {
    this.showModal = false;
    this.outletForm = this.getEmptyForm();
    this.selectedOutlet = null;
  }

  saveOutlet(): void {
    if (this.modalMode === 'create') {
      this.createOutlet();
    } else {
      this.updateOutlet();
    }
  }

  private createOutlet(): void {
    console.log('Creating outlet with data:', this.outletForm);
    this.isLoading = true;
    this.outletService.createOutlet(this.outletForm).subscribe({
      next: (response) => {
        console.log('Create response:', response);
        this.successMessage = 'Outlet created successfully';
        this.isLoading = false;
        this.closeModal();
        this.loadOutlets();
        setTimeout(() => this.successMessage = '', 3000);
      },
      error: (error) => {
        console.error('Create error:', error);
        this.errorMessage = error.error?.error || 'Failed to create outlet';
        this.isLoading = false;
        setTimeout(() => this.errorMessage = '', 3000);
      }
    });
  }

  private updateOutlet(): void {
    const outletId = this.selectedOutlet?._id || this.selectedOutlet?.id;
    if (!outletId) {
      console.error('No outlet ID found for update', this.selectedOutlet);
      this.errorMessage = 'Cannot update outlet: ID not found';
      setTimeout(() => this.errorMessage = '', 3000);
      return;
    }

    console.log('Updating outlet with ID:', outletId, 'Data:', this.outletForm);
    this.isLoading = true;
    this.outletService.updateOutlet(outletId, this.outletForm).subscribe({
      next: (response) => {
        console.log('Update response:', response);
        this.successMessage = 'Outlet updated successfully';
        this.isLoading = false;
        this.closeModal();
        this.loadOutlets();
        setTimeout(() => this.successMessage = '', 3000);
      },
      error: (error) => {
        console.error('Update error:', error);
        this.errorMessage = error.error?.error || 'Failed to update outlet';
        this.isLoading = false;
        setTimeout(() => this.errorMessage = '', 3000);
      }
    });
  }

  toggleOutletStatus(outlet: Outlet): void {
    const outletId = outlet._id || outlet.id;
    if (!outletId) return;

    const action = outlet.isActive ? 'deactivate' : 'activate';
    if (!confirm(`Are you sure you want to ${action} this outlet?`)) {
      return;
    }

    this.outletService.toggleOutletStatus(outletId).subscribe({
      next: () => {
        this.successMessage = `Outlet ${action}d successfully`;
        this.loadOutlets();
        setTimeout(() => this.successMessage = '', 3000);
      },
      error: (error) => {
        this.errorMessage = error.error?.error || `Failed to ${action} outlet`;
        setTimeout(() => this.errorMessage = '', 3000);
      }
    });
  }

  deleteOutlet(outlet: Outlet): void {
    const outletId = outlet._id || outlet.id;
    if (!outletId) return;

    if (!confirm(`Are you sure you want to delete ${outlet.outletName}? This action cannot be undone.`)) {
      return;
    }

    this.outletService.deleteOutlet(outletId).subscribe({
      next: () => {
        this.successMessage = 'Outlet deleted successfully';
        this.loadOutlets();
        setTimeout(() => this.successMessage = '', 3000);
      },
      error: (error) => {
        this.errorMessage = error.error?.error || 'Failed to delete outlet';
        setTimeout(() => this.errorMessage = '', 3000);
      }
    });
  }

  viewOutletDetails(outlet: Outlet): void {
    // Navigate to outlet details page or show in modal
    console.log('View outlet details:', outlet);
  }
}
