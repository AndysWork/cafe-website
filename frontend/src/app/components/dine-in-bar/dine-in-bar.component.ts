import { Component, OnInit, OnDestroy, HostListener, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, NavigationEnd } from '@angular/router';
import { Subscription } from 'rxjs';
import { filter } from 'rxjs/operators';
import { DineInService } from '../../services/dine-in.service';
import { DineInBill } from '../../models/dine-in.model';
import { CartStore } from '../../store';
import { MenuService } from '../../services/menu.service';

@Component({
  selector: 'app-dine-in-bar',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './dine-in-bar.component.html',
  styleUrls: ['./dine-in-bar.component.scss']
})
export class DineInBarComponent implements OnInit, OnDestroy {
  public dineInService = inject(DineInService);
  public cartStore = inject(CartStore);
  public menuService = inject(MenuService);
  private router = inject(Router);

  tableNumber = '';
  bill: DineInBill | null = null;
  currentUrl = '';
  isMobileScreen = false;
  private subs: Subscription[] = [];

  ngOnInit(): void {
    this.updateScreenSize();
    this.currentUrl = this.router.url;
    this.subs.push(
      this.router.events.pipe(
        filter(event => event instanceof NavigationEnd)
      ).subscribe((event: any) => {
        this.currentUrl = (event.urlAfterRedirects || event.url || '').split('?')[0];
      }),
      this.dineInService.activeTable$.subscribe(table => {
        this.tableNumber = table;
      }),
      this.dineInService.activeBill$.subscribe(bill => {
        this.bill = bill;
      })
    );
  }

  @HostListener('window:resize')
  onResize(): void {
    this.updateScreenSize();
  }

  private updateScreenSize(): void {
    if (typeof window !== 'undefined') {
      this.isMobileScreen = window.innerWidth <= 768;
    }
  }

  ngOnDestroy(): void {
    this.subs.forEach(s => s.unsubscribe());
  }

  get isCustomerRoute(): boolean {
    const url = this.currentUrl || this.router.url;
    return !url.startsWith('/admin') &&
           !url.startsWith('/kitchen') &&
           !url.startsWith('/manager') &&
           !url.startsWith('/partner') &&
           !url.startsWith('/staff');
  }

  get isMenuPage(): boolean {
    const url = this.currentUrl || this.router.url;
    return url.startsWith('/menu') || url.startsWith('/dine-in');
  }

  get isCartOrCheckoutPage(): boolean {
    const url = this.currentUrl || this.router.url;
    return url.startsWith('/cart') || url.startsWith('/checkout');
  }

  get cartItemCount(): number {
    return this.cartStore.itemCount();
  }

  get cartTotal(): number {
    return this.cartStore.total();
  }

  get showCategories(): boolean {
    // Categories button in floating dock is ONLY for mobile screens
    return this.isMenuPage && this.isMobileScreen;
  }

  get showCart(): boolean {
    return !this.isCartOrCheckoutPage && this.cartItemCount > 0;
  }

  get showViewBill(): boolean {
    return !!this.tableNumber &&
           !!this.bill &&
           this.bill.status !== 'paid' &&
           this.bill.status !== 'cancelled' &&
           !this.dineInService.isViewingSpecificSession;
  }

  get isVisible(): boolean {
    if (!this.isCustomerRoute) return false;
    return this.showCategories || this.showCart || this.showViewBill;
  }

  get displayTableNumber(): string {
    const raw = (this.tableNumber || '').trim();
    if (raw.toLowerCase().startsWith('table')) {
      return raw.substring(5).trim();
    }
    return raw;
  }

  toggleCategories(): void {
    if (!this.isMenuPage) {
      this.router.navigate(['/menu']).then(() => {
        setTimeout(() => this.menuService.openCategoryDrawer(), 120);
      });
      return;
    }
    this.menuService.toggleCategoryDrawer();
  }

  goToCart(): void {
    this.router.navigate(['/cart']);
  }

  viewBill(): void {
    this.dineInService.openBillModal();
  }
}
