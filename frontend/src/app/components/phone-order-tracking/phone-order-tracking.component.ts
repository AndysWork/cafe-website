import { CommonModule } from '@angular/common';
import { Component, OnDestroy, OnInit, inject } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { Meta } from '@angular/platform-browser';
import { HttpErrorResponse } from '@angular/common/http';
import { Subscription, combineLatest, exhaustMap, filter, of, switchMap, timer, catchError, tap } from 'rxjs';
import { PhoneOrderService, PhoneOrderTracking } from '../../services/phone-order.service';
import { downloadFile } from '../../utils/file-download';

@Component({
  selector: 'app-phone-order-tracking',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './phone-order-tracking.component.html',
  styleUrls: ['./phone-order-tracking.component.scss']
})
export class PhoneOrderTrackingComponent implements OnInit, OnDestroy {
  private readonly route = inject(ActivatedRoute);
  private readonly service = inject(PhoneOrderService);
  private readonly meta = inject(Meta);
  private subscription?: Subscription;
  private invoiceSubscription?: Subscription;
  private orderId: string | null = null;
  private token: string | null = null;
  private previousReferrer: string | null = null;
  private trackingUnavailable = false;
  tracking: PhoneOrderTracking | null = null;
  error = '';
  loading = true;
  lastChecked: Date | null = null;
  invoiceText = '';
  invoiceError = '';
  invoiceLoading = false;
  showInvoice = false;
  readonly steps = ['pending', 'confirmed', 'preparing', 'ready', 'out-for-delivery', 'delivered'];
  readonly labels: Record<string, string> = {
    pending: 'Order received', confirmed: 'Kitchen confirmed', preparing: 'Preparing your food',
    ready: 'Packed and ready', 'out-for-delivery': 'Out for delivery', delivered: 'Delivered',
    cancelled: 'Order cancelled'
  };

  ngOnInit(): void {
    this.previousReferrer = this.meta.getTag('name="referrer"')?.content ?? null;
    this.meta.updateTag({ name: 'referrer', content: 'no-referrer' });
    this.subscription = combineLatest([this.route.paramMap, this.route.queryParamMap]).pipe(
      switchMap(([params, query]) => {
        this.invoiceSubscription?.unsubscribe();
        this.invoiceText = '';
        this.invoiceError = '';
        this.invoiceLoading = false;
        this.showInvoice = false;
        this.tracking = null;
        this.error = '';
        this.loading = true;
        this.trackingUnavailable = false;
        const id = this.orderId = params.get('id');
        const token = this.token = query.get('token');
        if (!id || !token) {
          this.error = 'This tracking link is incomplete. Contact the cafe for a new link.';
          this.loading = false;
          return of(null);
        }
        return timer(0, 15000).pipe(
          filter(() => !this.trackingUnavailable && (!this.tracking || !['delivered', 'cancelled'].includes(this.tracking.status))),
          exhaustMap(() => this.service.track(id, token).pipe(
            tap(() => { this.error = ''; }),
            catchError((error: unknown) => {
              this.trackingUnavailable = error instanceof HttpErrorResponse && [400, 401, 403, 404].includes(error.status);
              this.error = this.trackingUnavailable
                ? 'This tracking link is invalid or no longer available. Please contact the cafe.'
                : 'Tracking could not be refreshed. Check your connection; we will retry automatically.';
              return of(null);
            })
          ))
        );
      })
    ).subscribe(tracking => {
      this.loading = false;
      if (tracking) {
        this.tracking = tracking;
        this.lastChecked = new Date();
      }
    });
  }

  ngOnDestroy(): void {
    this.subscription?.unsubscribe();
    this.invoiceSubscription?.unsubscribe();
    if (this.previousReferrer === null) this.meta.removeTag('name="referrer"');
    else this.meta.updateTag({ name: 'referrer', content: this.previousReferrer });
  }

  toggleInvoice(): void {
    if (this.invoiceLoading || !this.tracking || !this.orderId || !this.token) return;
    if (this.showInvoice) {
      this.showInvoice = false;
      return;
    }
    this.invoiceLoading = true;
    this.invoiceError = '';
    this.invoiceSubscription = this.service.invoice(this.orderId, this.token).subscribe({
      next: invoice => {
        this.invoiceText = invoice.invoiceText;
        this.showInvoice = true;
        this.invoiceLoading = false;
      },
      error: (error: unknown) => {
        this.invoiceLoading = false;
        this.invoiceError = error instanceof HttpErrorResponse && [400, 401, 403, 404].includes(error.status)
          ? 'This invoice link is invalid or no longer available. Please contact the cafe.'
          : 'The invoice could not be loaded. Please try again.';
      }
    });
  }

  downloadInvoice(): void {
    if (this.showInvoice && this.invoiceText && this.orderId)
      downloadFile(this.invoiceText, `invoice-${this.orderId.slice(-6)}.txt`, 'text/plain');
  }

  completed(step: string): boolean {
    return !!this.tracking && this.tracking.status !== 'cancelled'
      && this.steps.indexOf(step) <= this.steps.indexOf(this.tracking.status);
  }
}
