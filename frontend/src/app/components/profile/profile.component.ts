import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, RouterModule } from '@angular/router';
import { AuthService, User } from '../../services/auth.service';
import { AddressService, DeliveryAddress, AddAddressRequest } from '../../services/address.service';
import { FavoriteService } from '../../services/favorite.service';
import { MenuService, MenuItem } from '../../services/menu.service';
import { OrderService, Order, OrderItem } from '../../services/order.service';
import { CartService } from '../../services/cart.service';
import { UIStore } from '../../store/ui.store';
import { NotificationStore } from '../../store';

@Component({
  selector: 'app-profile',
  imports: [CommonModule, FormsModule, RouterModule],
  templateUrl: './profile.component.html',
  styleUrl: './profile.component.scss'
})
export class ProfileComponent implements OnInit {
  private readonly checkoutDraftStorageKey = 'checkout_draft';
  private readonly pendingPaymentStorageKey = 'pending_payment_recovery';

  user: User | null = null;

  // Profile update
  firstName = '';
  lastName = '';
  phoneNumber = '';
  profileMessage = '';
  profileError = '';
  isUpdatingProfile = false;

  // Profile picture
  isUploadingPicture = false;
  picturePreview: string | null = null;

  // Change password
  currentPassword = '';
  newPassword = '';
  confirmPassword = '';
  passwordMessage = '';
  passwordError = '';
  isChangingPassword = false;

  activeTab: 'profile' | 'password' | 'notifications' | 'addresses' | 'favorites' = 'profile';

  // Notification preferences
  notificationStore = inject(NotificationStore);
  isSavingPrefs = false;
  prefsMessage = '';

  // Addresses
  private addressService = inject(AddressService);
  addresses: DeliveryAddress[] = [];
  isLoadingAddresses = false;
  showAddressForm = false;
  editingAddressId: string | null = null;
  addressForm: AddAddressRequest = { label: '', fullAddress: '', city: '', pinCode: '', collectorName: '', collectorPhone: '', isDefault: false };

  // Favorites
  private favoriteService = inject(FavoriteService);
  private menuService = inject(MenuService);
  private uiStore = inject(UIStore);
  favoriteItems: MenuItem[] = [];
  isLoadingFavorites = false;
  checkoutDraft: { timestamp: string; cartItemCount: number; grandTotal: number } | null = null;
  pendingPaymentRecovery: { amount: number; reason: string; timestamp: string } | null = null;
  lastDeliveredOrder: Order | null = null;
  loadingLastOrder = false;
  isReorderingLastOrder = false;
  private menuItemMap = new Map<string, MenuItem>();

  constructor(
    private authService: AuthService,
    private orderService: OrderService,
    private cartService: CartService,
    private router: Router
  ) {}

  ngOnInit(): void {
    this.user = this.authService.getCurrentUser();
    if (this.user) {
      this.firstName = this.user.firstName || '';
      this.lastName = this.user.lastName || '';
      this.phoneNumber = this.user.phoneNumber || '';

      this.loadLatestMenuSnapshot();
      this.loadLastDeliveredOrder();
    }

    this.loadCheckoutDraft();
    this.loadPendingPaymentRecovery();
  }

  get checkoutDraftAgeMinutes(): number {
    if (!this.checkoutDraft?.timestamp) return 0;
    const ts = new Date(this.checkoutDraft.timestamp).getTime();
    if (Number.isNaN(ts)) return 0;
    return Math.max(0, Math.floor((Date.now() - ts) / 60000));
  }

  resumeCheckoutFromDraft(): void {
    this.router.navigate(['/checkout']);
  }

  dismissCheckoutDraft(): void {
    this.checkoutDraft = null;
    localStorage.removeItem(this.checkoutDraftStorageKey);
  }

  retryPendingPayment(): void {
    this.router.navigate(['/checkout']);
  }

  dismissPendingPaymentRecovery(): void {
    this.pendingPaymentRecovery = null;
    localStorage.removeItem(this.pendingPaymentStorageKey);
  }

  reorderLastOrder(): void {
    if (!this.lastDeliveredOrder || this.isReorderingLastOrder) return;

    this.isReorderingLastOrder = true;

    let added = 0;
    let substituted = 0;
    let skipped = 0;

    for (const item of this.lastDeliveredOrder.items || []) {
      const resolved = this.resolveReorderItem(item);
      if (!resolved) {
        skipped += 1;
        continue;
      }

      if (resolved.substituted) substituted += 1;

      this.cartService.addItem({
        menuItemId: resolved.menuItem.id,
        name: resolved.menuItem.name,
        description: resolved.menuItem.description,
        categoryName: resolved.menuItem.categoryName || item.categoryName,
        price: this.getWebPrice(resolved.menuItem) || item.price,
        imageUrl: resolved.menuItem.imageUrl,
        packagingCharge: resolved.menuItem.packagingCharge || 0
      }, item.quantity || 1);

      added += 1;
    }

    this.isReorderingLastOrder = false;

    if (added === 0) {
      this.uiStore.warning('No available items found for your last order.');
      return;
    }

    const parts: string[] = [`${added} item(s) added`];
    if (substituted > 0) parts.push(`${substituted} substituted`);
    if (skipped > 0) parts.push(`${skipped} skipped`);
    this.uiStore.success(`Reorder ready: ${parts.join(', ')}.`);
    this.router.navigate(['/cart']);
  }

  switchTab(tab: 'profile' | 'password' | 'notifications' | 'addresses' | 'favorites'): void {
    this.activeTab = tab;
    this.clearMessages();
    if (tab === 'notifications') {
      this.notificationStore.loadPreferences();
    }
    if (tab === 'addresses' && this.addresses.length === 0) {
      this.loadAddresses();
    }
    if (tab === 'favorites' && this.favoriteItems.length === 0) {
      this.loadFavorites();
    }
  }

  clearMessages(): void {
    this.profileMessage = '';
    this.profileError = '';
    this.passwordMessage = '';
    this.passwordError = '';
    this.prefsMessage = '';
  }

  togglePreference(key: string, event: Event): void {
    const checked = (event.target as HTMLInputElement).checked;
    this.notificationStore.updatePreferences({ [key]: checked });
    this.prefsMessage = 'Preferences saved!';
    setTimeout(() => this.prefsMessage = '', 2000);
  }

  updateProfile(): void {
    this.clearMessages();
    this.isUpdatingProfile = true;

    const updateData: any = {};

    // Only send non-empty values
    if (this.firstName?.trim()) {
      updateData.firstName = this.firstName.trim();
    }
    if (this.lastName?.trim()) {
      updateData.lastName = this.lastName.trim();
    }
    if (this.phoneNumber?.trim()) {
      updateData.phoneNumber = this.phoneNumber.trim();
    }

    this.authService.updateProfile(updateData).subscribe({
      next: () => {
        this.isUpdatingProfile = false;
        this.profileMessage = 'Profile updated successfully!';
        this.user = this.authService.getCurrentUser();
        setTimeout(() => this.profileMessage = '', 3000);
      },
      error: (error) => {
        this.isUpdatingProfile = false;
        this.profileError = error.error?.error || 'Failed to update profile';
        setTimeout(() => this.profileError = '', 5000);
      }
    });
  }

  changePassword(): void {
    this.clearMessages();

    if (this.newPassword !== this.confirmPassword) {
      this.passwordError = 'Passwords do not match';
      return;
    }

    if (this.newPassword.length < 6) {
      this.passwordError = 'Password must be at least 6 characters';
      return;
    }

    this.isChangingPassword = true;

    this.authService.changePassword(
      this.currentPassword,
      this.newPassword,
      this.confirmPassword
    ).subscribe({
      next: () => {
        this.isChangingPassword = false;
        this.passwordMessage = 'Password changed successfully!';
        this.currentPassword = '';
        this.newPassword = '';
        this.confirmPassword = '';
        setTimeout(() => this.passwordMessage = '', 3000);
      },
      error: (error) => {
        this.isChangingPassword = false;
        this.passwordError = error.error?.error || 'Failed to change password';
        setTimeout(() => this.passwordError = '', 5000);
      }
    });
  }

  onProfilePictureSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input?.files?.[0];
    if (!file) return;

    // Validate file type
    const allowedTypes = ['image/jpeg', 'image/png', 'image/webp', 'image/gif'];
    if (!allowedTypes.includes(file.type)) {
      this.profileError = 'Only JPEG, PNG, WebP, and GIF images are allowed';
      setTimeout(() => this.profileError = '', 5000);
      return;
    }

    // Validate file size (5MB)
    if (file.size > 5 * 1024 * 1024) {
      this.profileError = 'Image must be less than 5MB';
      setTimeout(() => this.profileError = '', 5000);
      return;
    }

    this.isUploadingPicture = true;
    this.clearMessages();

    this.authService.uploadProfilePicture(file).subscribe({
      next: () => {
        this.isUploadingPicture = false;
        this.user = this.authService.getCurrentUser();
        this.profileMessage = 'Profile picture updated!';
        setTimeout(() => this.profileMessage = '', 3000);
      },
      error: (error) => {
        this.isUploadingPicture = false;
        this.profileError = error.error?.error || 'Failed to upload profile picture';
        setTimeout(() => this.profileError = '', 5000);
      }
    });

    // Reset input so the same file can be re-selected
    input.value = '';
  }

  removeProfilePicture(): void {
    if (!this.user?.profilePictureUrl) return;

    this.isUploadingPicture = true;
    this.clearMessages();

    this.authService.deleteProfilePicture().subscribe({
      next: () => {
        this.isUploadingPicture = false;
        this.user = this.authService.getCurrentUser();
        this.profileMessage = 'Profile picture removed!';
        setTimeout(() => this.profileMessage = '', 3000);
      },
      error: (error) => {
        this.isUploadingPicture = false;
        this.profileError = error.error?.error || 'Failed to remove profile picture';
        setTimeout(() => this.profileError = '', 5000);
      }
    });
  }

  // ── Addresses ──

  loadAddresses(): void {
    this.isLoadingAddresses = true;
    this.addressService.getMyAddresses().subscribe({
      next: (addresses) => {
        this.addresses = addresses;
        this.isLoadingAddresses = false;
      },
      error: () => {
        this.uiStore.error('Failed to load addresses');
        this.isLoadingAddresses = false;
      }
    });
  }

  openAddressForm(address?: DeliveryAddress): void {
    if (address) {
      this.editingAddressId = address.id;
      this.addressForm = {
        label: address.label,
        fullAddress: address.fullAddress,
        city: address.city || '',
        pinCode: address.pinCode || '',
        collectorName: address.collectorName,
        collectorPhone: address.collectorPhone,
        isDefault: address.isDefault
      };
    } else {
      this.editingAddressId = null;
      this.addressForm = { label: '', fullAddress: '', city: '', pinCode: '', collectorName: '', collectorPhone: '', isDefault: false };
    }
    this.showAddressForm = true;
  }

  cancelAddressForm(): void {
    this.showAddressForm = false;
    this.editingAddressId = null;
  }

  saveAddress(): void {
    if (!this.addressForm.label.trim() || !this.addressForm.fullAddress.trim() ||
        !this.addressForm.collectorName.trim() || !this.addressForm.collectorPhone.trim()) {
      this.uiStore.warning('Please fill in all required fields');
      return;
    }

    if (this.editingAddressId) {
      this.addressService.updateAddress(this.editingAddressId, this.addressForm).subscribe({
        next: () => {
          this.uiStore.success('Address updated');
          this.showAddressForm = false;
          this.editingAddressId = null;
          this.loadAddresses();
        },
        error: () => this.uiStore.error('Failed to update address')
      });
    } else {
      this.addressService.addAddress(this.addressForm).subscribe({
        next: () => {
          this.uiStore.success('Address added');
          this.showAddressForm = false;
          this.loadAddresses();
        },
        error: () => this.uiStore.error('Failed to add address')
      });
    }
  }

  deleteAddress(addressId: string): void {
    this.addressService.deleteAddress(addressId).subscribe({
      next: () => {
        this.uiStore.success('Address deleted');
        this.addresses = this.addresses.filter(a => a.id !== addressId);
      },
      error: () => this.uiStore.error('Failed to delete address')
    });
  }

  setDefaultAddress(addressId: string): void {
    this.addressService.updateAddress(addressId, { isDefault: true }).subscribe({
      next: () => {
        this.uiStore.success('Default address updated');
        this.loadAddresses();
      },
      error: () => this.uiStore.error('Failed to set default address')
    });
  }

  // ── Favorites ──

  loadFavorites(): void {
    this.isLoadingFavorites = true;
    this.favoriteService.getMyFavorites().subscribe({
      next: (ids) => {
        if (ids.length === 0) {
          this.favoriteItems = [];
          this.isLoadingFavorites = false;
          return;
        }
        this.menuService.getMenuItems().subscribe({
          next: (items) => {
            this.favoriteItems = items.filter(i => ids.includes(i.id));
            this.isLoadingFavorites = false;
          },
          error: () => {
            this.isLoadingFavorites = false;
            this.uiStore.error('Failed to load menu items');
          }
        });
      },
      error: () => {
        this.isLoadingFavorites = false;
        this.uiStore.error('Failed to load favorites');
      }
    });
  }

  removeFavorite(itemId: string): void {
    this.favoriteService.toggleFavorite(itemId).subscribe({
      next: () => {
        this.favoriteItems = this.favoriteItems.filter(i => i.id !== itemId);
        this.uiStore.success('Removed from favorites');
      },
      error: () => this.uiStore.error('Failed to remove favorite')
    });
  }

  getWebPrice(item: MenuItem): number {
    return item.webPrice || item.shopSellingPrice || item.onlinePrice || 0;
  }

  private loadCheckoutDraft(): void {
    const raw = localStorage.getItem(this.checkoutDraftStorageKey);
    if (!raw) {
      this.checkoutDraft = null;
      return;
    }

    try {
      const parsed = JSON.parse(raw);
      this.checkoutDraft = {
        timestamp: parsed.timestamp || new Date().toISOString(),
        cartItemCount: Number(parsed.cartItemCount || 0),
        grandTotal: Number(parsed.grandTotal || 0)
      };
    } catch {
      this.checkoutDraft = null;
      localStorage.removeItem(this.checkoutDraftStorageKey);
    }
  }

  private loadLatestMenuSnapshot(): void {
    this.menuService.getMenuItems().subscribe({
      next: (items) => {
        this.menuItemMap = new Map((items || []).map(item => [item.id, item]));
      },
      error: () => {
        this.menuItemMap = new Map();
      }
    });
  }

  private loadPendingPaymentRecovery(): void {
    const raw = localStorage.getItem(this.pendingPaymentStorageKey);
    if (!raw) {
      this.pendingPaymentRecovery = null;
      return;
    }

    try {
      this.pendingPaymentRecovery = JSON.parse(raw);
    } catch {
      this.pendingPaymentRecovery = null;
      localStorage.removeItem(this.pendingPaymentStorageKey);
    }
  }

  private loadLastDeliveredOrder(): void {
    this.loadingLastOrder = true;
    this.orderService.getMyOrders().subscribe({
      next: (orders) => {
        this.lastDeliveredOrder = [...(orders || [])]
          .filter(o => o.status === 'delivered' && (o.items?.length || 0) > 0)
          .sort((a, b) => new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime())[0] || null;
        this.loadingLastOrder = false;
      },
      error: () => {
        this.lastDeliveredOrder = null;
        this.loadingLastOrder = false;
      }
    });
  }

  private resolveReorderItem(orderItem: OrderItem): { menuItem: MenuItem; substituted: boolean } | null {
    const direct = this.menuItemMap.get(orderItem.menuItemId);
    if (direct && direct.isAvailable !== false) {
      return { menuItem: direct, substituted: false };
    }

    const availableItems = [...this.menuItemMap.values()].filter(m => m.isAvailable !== false);
    if (!availableItems.length) return null;

    const sameCategory = availableItems.find(m => !!orderItem.categoryId && !!m.categoryId && m.categoryId === orderItem.categoryId);
    if (sameCategory) return { menuItem: sameCategory, substituted: true };

    const seed = (orderItem.name || '').trim().toLowerCase();
    const seedWord = seed.split(' ').find(Boolean) || '';
    if (seedWord) {
      const similar = availableItems.find(m => (m.name || '').toLowerCase().includes(seedWord));
      if (similar) return { menuItem: similar, substituted: true };
    }

    return { menuItem: availableItems[0], substituted: true };
  }
}
