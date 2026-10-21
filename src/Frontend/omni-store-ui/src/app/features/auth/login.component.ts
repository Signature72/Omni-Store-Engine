import { Component, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { AuthService } from '../../core/services/auth.service';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="min-h-[80vh] flex items-center justify-center px-4 sm:px-6 lg:px-8 py-12">
      <div class="max-w-md w-full space-y-8 bg-slate-900 p-8 rounded-3xl border border-slate-800 shadow-2xl relative overflow-hidden">
        
        <div class="text-center">
          <div class="mx-auto w-12 h-12 rounded-2xl bg-emerald-500/10 border border-emerald-500/20 text-emerald-400 flex items-center justify-center font-bold text-xl mb-4">
            Ω
          </div>
          <h2 class="text-2xl font-black text-white tracking-tight">
            {{ isRegisterMode() ? 'Create an Account' : 'Welcome Back' }}
          </h2>
          <p class="mt-1 text-xs text-slate-400">
            {{ isRegisterMode() ? 'Sign up to OmniStore Engine' : 'Sign in to access your orders and checkout history' }}
          </p>
        </div>

        <div class="flex rounded-xl bg-slate-950 p-1 border border-slate-800">
          <button (click)="isRegisterMode.set(false)" class="flex-1 py-1.5 text-xs font-semibold rounded-lg transition-all" [ngClass]="!isRegisterMode() ? 'bg-slate-800 text-white shadow' : 'text-slate-400 hover:text-white'">
            Sign In
          </button>
          <button (click)="isRegisterMode.set(true)" class="flex-1 py-1.5 text-xs font-semibold rounded-lg transition-all" [ngClass]="isRegisterMode() ? 'bg-slate-800 text-white shadow' : 'text-slate-400 hover:text-white'">
            Register
          </button>
        </div>

        <div class="space-y-4">
          @if (isRegisterMode()) {
            <div>
              <label class="block text-xs font-semibold text-slate-300 mb-1">Full Name</label>
              <input type="text" [(ngModel)]="fullName" class="w-full px-4 py-2.5 rounded-xl bg-slate-950 border border-slate-800 text-white text-sm focus:border-emerald-500 focus:outline-none" placeholder="e.g. Jane Doe" />
            </div>
            <div>
              <label class="block text-xs font-semibold text-slate-300 mb-1">LINE User ID (Optional)</label>
              <input type="text" [(ngModel)]="lineUserId" class="w-full px-4 py-2.5 rounded-xl bg-slate-950 border border-slate-800 text-white text-sm focus:border-emerald-500 focus:outline-none font-mono" placeholder="e.g. U12345678" />
            </div>
          }

          <div>
            <label class="block text-xs font-semibold text-slate-300 mb-1">Email Address</label>
            <input type="email" [(ngModel)]="email" class="w-full px-4 py-2.5 rounded-xl bg-slate-950 border border-slate-800 text-white text-sm focus:border-emerald-500 focus:outline-none" placeholder="name@example.com" />
          </div>

          <div>
            <label class="block text-xs font-semibold text-slate-300 mb-1">Password</label>
            <input type="password" [(ngModel)]="password" class="w-full px-4 py-2.5 rounded-xl bg-slate-950 border border-slate-800 text-white text-sm focus:border-emerald-500 focus:outline-none" placeholder="••••••••" />
          </div>
        </div>

        @if (errorMessage()) {
          <div class="p-3 rounded-xl bg-rose-500/10 border border-rose-500/30 text-rose-400 text-xs font-medium">
            {{ errorMessage() }}
          </div>
        }

        <button 
          (click)="submitAuth()" 
          [disabled]="isLoading()"
          class="w-full py-3 rounded-xl bg-emerald-500 hover:bg-emerald-400 text-slate-950 font-bold text-sm shadow-lg shadow-emerald-500/20 transition-all flex items-center justify-center gap-2">
          {{ isLoading() ? 'Processing...' : (isRegisterMode() ? 'Complete Registration' : 'Sign In') }}
        </button>

        <div class="p-4 rounded-xl bg-slate-950/80 border border-slate-800/80 text-[11px] text-slate-400 font-mono">
          <div class="text-emerald-400 font-bold mb-1">Security Specifications:</div>
          <div>• Password: BCrypt Work Factor 12</div>
          <div>• Token: JWT HS256 + Refresh Token Rotation</div>
          <div>• Target: AuthDB (PostgreSQL)</div>
        </div>

      </div>
    </div>
  `
})
export class LoginComponent {
  public isRegisterMode = signal<boolean>(false);
  public email = 'architect@omnistore.io';
  public password = 'P@sswordEnterprise2026';
  public fullName = 'Senior Enterprise Architect';
  public lineUserId = 'U_ARCHITECT_PORTFOLIO';

  public isLoading = signal<boolean>(false);
  public errorMessage = signal<string | null>(null);

  constructor(
    private authService: AuthService,
    private router: Router
  ) {}

  public submitAuth(): void {
    this.errorMessage.set(null);
    this.isLoading.set(true);

    if (this.isRegisterMode()) {
      this.authService.register({
        email: this.email,
        password: this.password,
        fullName: this.fullName,
        lineUserId: this.lineUserId
      }).subscribe({
        next: () => {
          this.isLoading.set(false);
          this.router.navigate(['/']);
        },
        error: (err) => {
          this.isLoading.set(false);
          this.errorMessage.set(err.error?.message || 'Registration failed.');
        }
      });
    } else {
      this.authService.login({
        email: this.email,
        password: this.password
      }).subscribe({
        next: () => {
          this.isLoading.set(false);
          this.router.navigate(['/']);
        },
        error: (err) => {
          this.isLoading.set(false);
          this.errorMessage.set(err.error?.message || 'Login failed. Check your email/password or Register first.');
        }
      });
    }
  }
}
