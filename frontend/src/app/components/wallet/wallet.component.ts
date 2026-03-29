import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { WalletService, WalletResponse, WalletTransaction } from '../../services/wallet.service';
import { UIStore } from '../../store/ui.store';

@Component({
  selector: 'app-wallet',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './wallet.component.html',
  styleUrls: ['./wallet.component.scss']
})
export class WalletComponent implements OnInit {
  private walletService = inject(WalletService);
  private uiStore = inject(UIStore);

  walletData: WalletResponse | null = null;
  transactions: WalletTransaction[] = [];
  loading = true;
  showRecharge = false;
  rechargeAmount: number = 500;
  processing = false;

  presetAmounts = [100, 200, 500, 1000, 2000];

  ngOnInit() {
    this.loadWallet();
  }

  loadWallet() {
    this.loading = true;
    this.walletService.getMyWallet().subscribe({
      next: (data) => {
        this.walletData = data;
        this.transactions = data.recentTransactions || [];
        this.loading = false;
      },
      error: () => {
        this.uiStore.error('Failed to load wallet');
        this.loading = false;
      }
    });
  }

  loadMoreTransactions() {
    const page = Math.floor(this.transactions.length / 20) + 1;
    this.walletService.getTransactions(page).subscribe({
      next: (txns) => {
        this.transactions = [...this.transactions, ...txns];
      }
    });
  }

  selectAmount(amount: number) {
    this.rechargeAmount = amount;
  }

  rechargeWallet() {
    if (!this.rechargeAmount || this.rechargeAmount < 10) {
      this.uiStore.error('Minimum recharge amount is ₹10');
      return;
    }
    this.processing = true;
    this.walletService.rechargeWallet({ amount: this.rechargeAmount, paymentMethod: 'online' }).subscribe({
      next: () => {
        this.uiStore.success(`₹${this.rechargeAmount} added to wallet`);
        this.showRecharge = false;
        this.processing = false;
        this.loadWallet();
      },
      error: () => {
        this.uiStore.error('Recharge failed');
        this.processing = false;
      }
    });
  }

  formatDate(dateStr?: string): string {
    if (!dateStr) return '-';
    return new Date(dateStr).toLocaleDateString('en-IN', { day: '2-digit', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit' });
  }
}
