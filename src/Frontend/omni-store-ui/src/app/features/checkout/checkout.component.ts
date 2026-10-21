import { Component, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { RouterLink } from '@angular/router';
import { CartService } from '../../shared/services/cart.service';
import { AuthService } from '../../core/services/auth.service';
import { CheckoutResponse } from '../../core/models/models';

@Component({
  selector: 'app-checkout',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink],
  templateUrl: './checkout.component.html'
})
export class CheckoutComponent {
  private readonly gatewayUrl = 'http://localhost:5000/api/orders';

  public customerName = 'Alex Henderson';
  public customerEmail = 'alex.henderson@example.com';
  public customerLineUserId = 'U_ENTERPRISE_DEV_PORTFOLIO';
  public paymentMethod = 'CreditCard';

  public isProcessing = signal<boolean>(false);
  public errorMessage = signal<string | null>(null);
  public completedOrder = signal<CheckoutResponse | null>(null);

  constructor(
    private http: HttpClient,
    public cartService: CartService,
    private authService: AuthService
  ) {
    const user = this.authService.currentUserSignal();
    if (user) {
      this.customerName = user.fullName;
      this.customerEmail = user.email;
    }
  }

  public increment(item: any): void {
    this.cartService.updateQuantity(item.productId, item.quantity + 1).subscribe();
  }

  public decrement(item: any): void {
    if (item.quantity > 1) {
      this.cartService.updateQuantity(item.productId, item.quantity - 1).subscribe();
    } else {
      this.cartService.removeItem(item.productId).subscribe();
    }
  }

  public processCheckout(): void {
    this.errorMessage.set(null);
    this.isProcessing.set(true);

    const user = this.authService.currentUserSignal();
    const userId = user ? user.userId : '00000000-0000-0000-0000-000000000001';

    const payload = {
      cartId: this.cartService.getOrCreateCartId(),
      userId: userId,
      customerName: this.customerName,
      customerEmail: this.customerEmail,
      customerLineUserId: this.customerLineUserId,
      paymentMethod: this.paymentMethod
    };

    this.http.post<CheckoutResponse>(`${this.gatewayUrl}/checkout`, payload).subscribe({
      next: (response) => {
        this.isProcessing.set(false);
        this.completedOrder.set(response);
      },
      error: (err) => {
        this.isProcessing.set(false);
        const serverError = err.error?.message || err.message || 'Checkout failed.';
        this.errorMessage.set(serverError);
      }
    });
  }

  public resetOrder(): void {
    this.completedOrder.set(null);
    this.cartService.loadCart();
  }
}
