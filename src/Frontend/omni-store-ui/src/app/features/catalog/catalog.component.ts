import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { CartService } from '../../shared/services/cart.service';
import { Product } from '../../core/models/models';

@Component({
  selector: 'app-catalog',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './catalog.component.html'
})
export class CatalogComponent implements OnInit {
  private readonly gatewayUrl = 'http://localhost:5000/api/inventory';

  public products = signal<Product[]>([]);
  public isLoading = signal<boolean>(true);
  public isSeeding = signal<boolean>(false);
  public addingMap = signal<Record<string, boolean>>({});

  constructor(
    private http: HttpClient,
    private cartService: CartService
  ) {}

  ngOnInit(): void {
    this.loadProducts();
  }

  public loadProducts(): void {
    this.isLoading.set(true);
    this.http.get<Product[]>(`${this.gatewayUrl}/products`).subscribe({
      next: (data) => {
        this.products.set(data);
        this.isLoading.set(false);
      },
      error: () => {
        this.isLoading.set(false);
      }
    });
  }

  public seedCatalog(): void {
    this.isSeeding.set(true);
    this.http.post(`${this.gatewayUrl}/seed`, {}).subscribe({
      next: () => {
        this.isSeeding.set(false);
        this.loadProducts();
      },
      error: () => {
        this.isSeeding.set(false);
      }
    });
  }

  public addToCart(product: Product): void {
    this.cartService.addItem({
      productId: product.id,
      productName: product.name,
      unitPrice: product.price,
      quantity: 1,
      imageUrl: product.imageUrl
    }).subscribe({
      next: () => {
        const current = { ...this.addingMap(), [product.id]: true };
        this.addingMap.set(current);
        setTimeout(() => {
          const reset = { ...this.addingMap(), [product.id]: false };
          this.addingMap.set(reset);
        }, 1200);
      }
    });
  }
}
