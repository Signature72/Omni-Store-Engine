export interface User {
  userId: string;
  email: string;
  fullName: string;
  role: string;
  lineUserId?: string;
  createdAtUtc?: string;
}

export interface AuthResponse {
  userId: string;
  email: string;
  fullName: string;
  role: string;
  accessToken: string;
  refreshToken: string;
  expiresAtUtc: string;
}

export interface Product {
  id: string;
  sku: string;
  name: string;
  description: string;
  price: number;
  availableStock: number;
  reservedStock: number;
  imageUrl: string;
}

export interface CartItem {
  productId: string;
  productName: string;
  unitPrice: number;
  quantity: number;
  imageUrl: string;
  subTotal?: number;
}

export interface Cart {
  cartId: string;
  items: CartItem[];
  totalAmount: number;
  totalItems: number;
  updatedAtUtc?: string;
}

export interface CheckoutRequest {
  cartId: string;
  userId: string;
  customerName: string;
  customerEmail: string;
  customerLineUserId?: string;
  paymentMethod: string;
}

export interface CheckoutResponse {
  orderId: string;
  totalAmount: number;
  status: string;
  transactionId: string;
  createdAtUtc: string;
  message: string;
}
