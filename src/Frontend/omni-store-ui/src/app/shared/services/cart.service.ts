import { Injectable, computed, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { BehaviorSubject, Observable, tap, catchError, of } from 'rxjs';
import { Cart, CartItem } from '../../core/models/models';

@Injectable({
  providedIn: 'root'
})
export class CartService {
  private readonly gatewayUrl = 'http://localhost:5000/api/cart';
  private readonly cartIdKey = 'omni_cart_id';

  public cartSignal = signal<Cart>({ cartId: this.getOrCreateCartId(), items: [], totalAmount: 0, totalItems: 0 });
  public itemCount = computed(() => this.cartSignal().totalItems);
  public totalAmount = computed(() => this.cartSignal().totalAmount);

  private cartSubject = new BehaviorSubject<Cart>(this.cartSignal());
  public cart$ = this.cartSubject.asObservable();

  constructor(private http: HttpClient) {
    this.loadCart();
  }

  public getOrCreateCartId(): string {
    let cartId = localStorage.getItem(this.cartIdKey);
    if (!cartId) {
      cartId = 'cart_' + Math.random().toString(36).substring(2, 12);
      localStorage.setItem(this.cartIdKey, cartId);
    }
    return cartId;
  }

  public loadCart(): void {
    const cartId = this.getOrCreateCartId();
    this.http.get<Cart>(`${this.gatewayUrl}/${cartId}`).pipe(
      catchError(() => of({ cartId, items: [], totalAmount: 0, totalItems: 0 } as Cart))
    ).subscribe(cart => {
      this.updateState(cart);
    });
  }

  public addItem(item: { productId: string; productName: string; unitPrice: number; quantity: number; imageUrl?: string }): Observable<Cart> {
    const cartId = this.getOrCreateCartId();
    return this.http.post<Cart>(`${this.gatewayUrl}/${cartId}/items`, item).pipe(
      tap(cart => this.updateState(cart))
    );
  }

  public updateQuantity(productId: string, quantity: number): Observable<Cart> {
    const cartId = this.getOrCreateCartId();
    return this.http.put<Cart>(`${this.gatewayUrl}/${cartId}/items/${productId}`, { quantity }).pipe(
      tap(cart => this.updateState(cart))
    );
  }

  public removeItem(productId: string): Observable<Cart> {
    const cartId = this.getOrCreateCartId();
    return this.http.delete<Cart>(`${this.gatewayUrl}/${cartId}/items/${productId}`).pipe(
      tap(cart => this.updateState(cart))
    );
  }

  public clearCart(): Observable<void> {
    const cartId = this.getOrCreateCartId();
    return this.http.delete<void>(`${this.gatewayUrl}/${cartId}`).pipe(
      tap(() => {
        this.updateState({ cartId, items: [], totalAmount: 0, totalItems: 0 });
      })
    );
  }

  public updateState(cart: Cart): void {
    const totalAmount = cart.items.reduce((sum, item) => sum + (item.unitPrice * item.quantity), 0);
    const totalItems = cart.items.reduce((sum, item) => sum + item.quantity, 0);

    const fullCart: Cart = {
      ...cart,
      totalAmount,
      totalItems
    };

    this.cartSignal.set(fullCart);
    this.cartSubject.next(fullCart);
  }
}
