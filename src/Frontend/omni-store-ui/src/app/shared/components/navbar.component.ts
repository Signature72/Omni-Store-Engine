import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { CartService } from '../services/cart.service';
import { AuthService } from '../../core/services/auth.service';

@Component({
  selector: 'app-navbar',
  standalone: true,
  imports: [CommonModule, RouterLink, RouterLinkActive],
  template: `
    <header class="sticky top-0 z-50 backdrop-blur-md bg-slate-900/80 border-b border-slate-800">
      <div class="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
        <div class="flex items-center justify-between h-16">
          
          <!-- Logo & Portfolio Badges -->
          <div class="flex items-center gap-3">
            <a routerLink="/" class="flex items-center gap-2 group">
              <div class="w-10 h-10 rounded-xl bg-gradient-to-tr from-emerald-500 to-teal-400 flex items-center justify-center shadow-lg shadow-emerald-500/20 group-hover:scale-105 transition-transform">
                <svg class="w-6 h-6 text-slate-950 font-bold" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                  <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2.5" d="M16 11V7a4 4 0 00-8 0v4M5 9h14l1 12H4L5 9z" />
                </svg>
              </div>
              <div>
                <span class="text-xl font-extrabold tracking-tight bg-gradient-to-r from-white via-slate-100 to-emerald-400 bg-clip-text text-transparent">
                  OmniStore
                </span>
                <span class="text-xs ml-1.5 px-2 py-0.5 rounded-full bg-emerald-500/10 text-emerald-400 border border-emerald-500/20 font-mono font-medium">
                  ENGINE v1.0
                </span>
              </div>
            </a>
          </div>

          <!-- Navigation Links -->
          <nav class="hidden md:flex items-center gap-6">
            <a routerLink="/" routerLinkActive="text-emerald-400 font-semibold" [routerLinkActiveOptions]="{exact: true}" class="text-sm text-slate-300 hover:text-white transition-colors">
              Product Catalog
            </a>
            <a routerLink="/checkout" routerLinkActive="text-emerald-400 font-semibold" class="text-sm text-slate-300 hover:text-white transition-colors">
              Checkout Flow
            </a>
          </nav>

          <!-- Action Buttons: Cart & Auth -->
          <div class="flex items-center gap-4">
            <!-- Cart Icon with reactive signal count -->
            <a routerLink="/checkout" class="relative p-2 text-slate-300 hover:text-white transition-colors rounded-lg hover:bg-slate-800">
              <svg class="w-6 h-6" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M3 3h2l.4 2M7 13h10l4-8H5.4M7 13L5.4 5M7 13l-2.293 2.293c-.63.63-.184 1.707.707 1.707H17m0 0a2 2 0 100 4 2 2 0 000-4zm-8 2a2 2 0 11-4 0 2 2 0 014 0z" />
              </svg>
              @if (cartService.itemCount() > 0) {
                <span class="absolute -top-1 -right-1 px-1.5 py-0.5 text-xs font-bold rounded-full bg-emerald-500 text-slate-950 animate-pulse">
                  {{ cartService.itemCount() }}
                </span>
              }
            </a>

            <!-- Auth Status -->
            @if (authService.currentUserSignal(); as user) {
              <div class="flex items-center gap-3 pl-2 border-l border-slate-800">
                <div class="text-right">
                  <div class="text-xs font-medium text-slate-200">{{ user.fullName }}</div>
                  <div class="text-[10px] text-emerald-400 font-mono">{{ user.role }}</div>
                </div>
                <button (click)="authService.logout()" class="text-xs px-2.5 py-1.5 rounded-md bg-slate-800 hover:bg-slate-700 text-slate-300 hover:text-white transition-colors">
                  Logout
                </button>
              </div>
            } @else {
              <a routerLink="/login" class="text-xs px-3.5 py-2 rounded-lg bg-emerald-500 hover:bg-emerald-400 text-slate-950 font-semibold shadow-md shadow-emerald-500/20 transition-all">
                Sign In
              </a>
            }
          </div>

        </div>
      </div>
    </header>
  `
})
export class NavbarComponent {
  constructor(
    public cartService: CartService,
    public authService: AuthService
  ) {}
}
