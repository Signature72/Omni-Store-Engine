import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { CartService } from './cart.service';

describe('CartService', () => {
  let service: CartService;
  let httpTesting: HttpTestingController;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [
        CartService,
        provideHttpClient(),
        provideHttpClientTesting()
      ]
    });

    service = TestBed.inject(CartService);
    httpTesting = TestBed.inject(HttpTestingController);

    // Handle initial loadCart request
    const req = httpTesting.expectOne((r) => r.url.includes('/api/cart/'));
    req.flush({ cartId: 'test_cart', items: [], totalAmount: 0, totalItems: 0 });
  });

  afterEach(() => {
    httpTesting.verify();
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  it('should generate a persistent cartId in localStorage', () => {
    const cartId = service.getOrCreateCartId();
    expect(cartId).toBeTruthy();
    expect(localStorage.getItem('omni_cart_id')).toBe(cartId);
  });

  it('should compute totalAmount and totalItems correctly', () => {
    service.updateState({
      cartId: 'test_cart',
      items: [
        { productId: 'p1', productName: 'Item A', unitPrice: 100, quantity: 2, imageUrl: '' },
        { productId: 'p2', productName: 'Item B', unitPrice: 50, quantity: 3, imageUrl: '' }
      ],
      totalAmount: 0,
      totalItems: 0
    });

    expect(service.itemCount()).toBe(5);
    expect(service.totalAmount()).toBe(350);
  });
});
