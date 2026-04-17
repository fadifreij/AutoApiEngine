import {
  Component, inject, signal, ChangeDetectionStrategy,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink, ActivatedRoute } from '@angular/router';
import { ReactiveFormsModule, FormBuilder, Validators } from '@angular/forms';
import { MockAuthService } from '../../services/mock-auth.service';
import { AuthStore } from '../../store/auth.store';

@Component({
  selector: 'app-signin',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    CommonModule, RouterLink, ReactiveFormsModule,
  ],
  template: `
    <div class="auth-shell">

      <!-- ── Background ──────────────────────────────────────────────────── -->
      <div class="auth-bg" aria-hidden="true">
        <div class="auth-bg__grid"></div>
        <div class="auth-bg__glow auth-bg__glow--1"></div>
        <div class="auth-bg__glow auth-bg__glow--2"></div>
        <div class="auth-bg__glow auth-bg__glow--3"></div>
      </div>

      <!-- ── Left brand panel ────────────────────────────────────────────── -->
      <aside class="auth-brand">

        <!-- Decorative floating shapes -->
        <div class="brand-decor" aria-hidden="true">
          <div class="decor-ring decor-ring--1"></div>
          <div class="decor-ring decor-ring--2"></div>
          <div class="decor-dot decor-dot--1"></div>
          <div class="decor-dot decor-dot--2"></div>
          <div class="decor-dot decor-dot--3"></div>
        </div>

        <div class="auth-brand__logo">
          <svg viewBox="0 0 32 32" width="28" height="28" fill="none" aria-hidden="true">
            <path d="M16 2 L28 9 L28 23 L16 30 L4 23 L4 9 Z" stroke="url(#logo-grad)" stroke-width="2" fill="none"/>
            <path d="M16 8 L22 11.5 L22 18.5 L16 22 L10 18.5 L10 11.5 Z" fill="url(#logo-grad)" opacity="0.3"/>
            <defs>
              <linearGradient id="logo-grad" x1="0%" y1="0%" x2="100%" y2="100%">
                <stop offset="0%" stop-color="#38bdf8"/>
                <stop offset="100%" stop-color="#818cf8"/>
              </linearGradient>
            </defs>
          </svg>
          <span class="auth-brand__name">Auto API Engine</span>
        </div>

        <div class="auth-brand__body">
          <h1 class="auth-brand__headline">
            Manage APIs,<br>
            <em>effortlessly.</em>
          </h1>
          <p class="auth-brand__sub">
            One platform to design, deploy, and monitor your entire API ecosystem.
            Built for teams that move fast.
          </p>

          <!-- Stats row -->
          <div class="stats-row">
            @for (s of stats; track s.label) {
              <div class="stat-card">
                <span class="stat-card__value">{{ s.value }}</span>
                <span class="stat-card__label">{{ s.label }}</span>
              </div>
            }
          </div>

          <!-- Illustration cards -->
          <div class="mini-cards">
            <div class="mini-card">
              <img src="assets/illustrations/api-flow.svg" width="280" height="140" alt="" loading="lazy"/>
              <div class="mini-card__label">
                <svg viewBox="0 0 20 20" fill="currentColor" width="14" height="14"><path fill-rule="evenodd" d="M11.3 1.046A1 1 0 0112 2v5h4a1 1 0 01.82 1.573l-7 10A1 1 0 018 18v-5H4a1 1 0 01-.82-1.573l7-10a1 1 0 011.12-.38z" clip-rule="evenodd"/></svg>
                API Orchestration
              </div>
            </div>
            <div class="mini-card">
              <img src="assets/illustrations/database-stack.svg" width="280" height="140" alt="" loading="lazy"/>
              <div class="mini-card__label">
                <svg viewBox="0 0 20 20" fill="currentColor" width="14" height="14"><path d="M3 12v3c0 1.657 3.134 3 7 3s7-1.343 7-3v-3c0 1.657-3.134 3-7 3s-7-1.343-7-3z"/><path d="M3 7v3c0 1.657 3.134 3 7 3s7-1.343 7-3V7c0 1.657-3.134 3-7 3S3 8.657 3 7z"/><path d="M17 5c0 1.657-3.134 3-7 3S3 6.657 3 5s3.134-3 7-3 7 1.343 7 3z"/></svg>
                Data Layer
              </div>
            </div>
          </div>

          <!-- Testimonial -->
          <div class="testimonial">
            <div class="testimonial__stars" aria-label="5 stars">
              @for (i of [1,2,3,4,5]; track i) {
                <svg viewBox="0 0 20 20" fill="currentColor" width="14" height="14">
                  <path d="M9.049 2.927c.3-.921 1.603-.921 1.902 0l1.07 3.292a1 1 0 00.95.69h3.462c.969 0 1.371 1.24.588 1.81l-2.8 2.034a1 1 0 00-.364 1.118l1.07 3.292c.3.921-.755 1.688-1.54 1.118l-2.8-2.034a1 1 0 00-1.175 0l-2.8 2.034c-.784.57-1.838-.197-1.539-1.118l1.07-3.292a1 1 0 00-.364-1.118L2.98 8.72c-.783-.57-.38-1.81.588-1.81h3.461a1 1 0 00.951-.69l1.07-3.292z"/>
                </svg>
              }
            </div>
            <p class="testimonial__quote">
              "Auto API Engine cut our integration time by 70%.
              The dashboard gives us real-time visibility we never had before."
            </p>
            <div class="testimonial__author">
              <div class="testimonial__avatar">SL</div>
              <div>
                <div class="testimonial__name">Sarah Lin</div>
                <div class="testimonial__role">VP Engineering, CloudBridge</div>
              </div>
            </div>
          </div>
        </div>

        <!-- Footer -->
        <div class="auth-brand__footer">
          <div class="footer-top">
            <span class="status-badge">
              <span class="status-dot"></span>
              All systems operational
            </span>
          </div>
          <div class="tech-logos">
            @for (t of techTags; track t) {
              <span class="tech-logo">{{ t }}</span>
            }
          </div>
        </div>
      </aside>

      <!-- ── Right form panel ────────────────────────────────────────────── -->
      <main class="auth-form-panel">
        <div class="auth-card">

          <header class="auth-card__header">
            <h2>Welcome back</h2>
            <p>Sign in to your organisation workspace</p>
          </header>

          <!-- Session-expired banner -->
          @if (sessionExpired()) {
            <div class="session-banner">
              <i class="pi pi-clock"></i>
              Your session expired. Please sign in again.
            </div>
          }

          <form [formGroup]="form" (ngSubmit)="submit()" novalidate>

            <!-- Email field -->
            <div class="field">
              <label for="email">Email</label>
              <div class="input-wrap" [class.input-wrap--invalid]="isDirty('email')">
                <svg class="input-icon" viewBox="0 0 20 20" fill="currentColor" width="18" height="18" aria-hidden="true">
                  <path d="M2.003 5.884L10 9.882l7.997-3.998A2 2 0 0016 4H4a2 2 0 00-1.997 1.884z"/>
                  <path d="M18 8.118l-8 4-8-4V14a2 2 0 002 2h12a2 2 0 002-2V8.118z"/>
                </svg>
                <input
                  id="email"
                  type="email"
                  formControlName="email"
                  placeholder="jane&#64;acme.com"
                  class="auth-input"
                  [class.ng-invalid]="isDirty('email')"
                  autocomplete="email"
                />
              </div>
              @if (isDirty('email')) {
                <small class="field-error">Enter a valid email address</small>
              }
            </div>

            <!-- Password field -->
            <div class="field">
              <div class="field__label-row">
                <label for="password">Password</label>
                <a href="/auth/forgot-password" class="forgot-link">Forgot password?</a>
              </div>
              <div class="input-wrap" [class.input-wrap--invalid]="isDirty('password')">
                <svg class="input-icon" viewBox="0 0 20 20" fill="currentColor" width="18" height="18" aria-hidden="true">
                  <path fill-rule="evenodd" d="M5 9V7a5 5 0 0110 0v2a2 2 0 012 2v5a2 2 0 01-2 2H5a2 2 0 01-2-2v-5a2 2 0 012-2zm8-2v2H7V7a3 3 0 016 0z" clip-rule="evenodd"/>
                </svg>
                <input
                  id="password"
                  [type]="showPassword() ? 'text' : 'password'"
                  formControlName="password"
                  placeholder="Your password"
                  class="auth-input"
                  autocomplete="current-password"
                />
                <button
                  type="button"
                  class="toggle-pw"
                  (click)="showPassword.set(!showPassword())"
                  [attr.aria-label]="showPassword() ? 'Hide password' : 'Show password'"
                >
                  @if (showPassword()) {
                    <svg viewBox="0 0 20 20" fill="currentColor" width="18" height="18"><path fill-rule="evenodd" d="M3.707 2.293a1 1 0 00-1.414 1.414l14 14a1 1 0 001.414-1.414l-1.473-1.473A10.014 10.014 0 0019.542 10C18.268 5.943 14.478 3 10 3a9.958 9.958 0 00-4.512 1.074l-1.78-1.781zm4.261 4.26l1.514 1.515a2.003 2.003 0 012.45 2.45l1.514 1.514a4 4 0 00-5.478-5.478z" clip-rule="evenodd"/><path d="M12.454 16.697L9.75 13.992a4 4 0 01-3.742-3.741L2.335 6.578A9.98 9.98 0 00.458 10c1.274 4.057 5.065 7 9.542 7 .847 0 1.669-.105 2.454-.303z"/></svg>
                  } @else {
                    <svg viewBox="0 0 20 20" fill="currentColor" width="18" height="18"><path d="M10 12a2 2 0 100-4 2 2 0 000 4z"/><path fill-rule="evenodd" d="M.458 10C1.732 5.943 5.522 3 10 3s8.268 2.943 9.542 7c-1.274 4.057-5.064 7-9.542 7S1.732 14.057.458 10zM14 10a4 4 0 11-8 0 4 4 0 018 0z" clip-rule="evenodd"/></svg>
                  }
                </button>
              </div>
              @if (isDirty('password')) {
                <small class="field-error">Password is required</small>
              }
            </div>

            @if (serverError()) {
              <div class="server-error">
                <svg viewBox="0 0 20 20" fill="currentColor" width="16" height="16"><path fill-rule="evenodd" d="M8.257 3.099c.765-1.36 2.722-1.36 3.486 0l5.58 9.92c.75 1.334-.213 2.98-1.742 2.98H4.42c-1.53 0-2.493-1.646-1.743-2.98l5.58-9.92zM11 13a1 1 0 11-2 0 1 1 0 012 0zm-1-8a1 1 0 00-1 1v3a1 1 0 002 0V6a1 1 0 00-1-1z" clip-rule="evenodd"/></svg>
                {{ serverError() }}
              </div>
            }

            @if (debugStatus()) {
              <div style="background:#1e293b;border:1px solid #38bdf8;border-radius:8px;padding:0.75rem;margin-bottom:0.75rem;color:#38bdf8;font-size:0.8rem;word-break:break-all;">DEBUG: {{ debugStatus() }}</div>
            }

            <!-- Submit button -->
            <button
              type="submit"
              class="submit-btn"
              [disabled]="authStore.isLoading()"
              (click)="submit()"
            >
              @if (authStore.isLoading()) {
                <svg class="spinner" viewBox="0 0 24 24" width="20" height="20" aria-hidden="true">
                  <circle cx="12" cy="12" r="10" stroke="currentColor" stroke-width="3" fill="none" stroke-linecap="round" stroke-dasharray="31.4 31.4"/>
                </svg>
                <span>Signing in…</span>
              } @else {
                <span>Sign in</span>
                <svg viewBox="0 0 20 20" fill="currentColor" width="18" height="18" aria-hidden="true">
                  <path fill-rule="evenodd" d="M10.293 3.293a1 1 0 011.414 0l6 6a1 1 0 010 1.414l-6 6a1 1 0 01-1.414-1.414L14.586 11H3a1 1 0 110-2h11.586l-4.293-4.293a1 1 0 010-1.414z" clip-rule="evenodd"/>
                </svg>
              }
            </button>

          </form>

          <div class="auth-divider" role="separator" aria-hidden="true">
            <span class="divider-text">Don't have an account?</span>
          </div>

          <a routerLink="/auth/signup" class="signup-link">
            <svg viewBox="0 0 20 20" fill="currentColor" width="16" height="16" aria-hidden="true">
              <path fill-rule="evenodd" d="M4 4a2 2 0 012-2h8a2 2 0 012 2v12a1 1 0 110 2h-3a1 1 0 01-1-1v-2a1 1 0 00-1-1H9a1 1 0 00-1 1v2a1 1 0 01-1 1H4a1 1 0 110-2V4zm3 1h2v2H7V5zm2 4H7v2h2V9zm2-4h2v2h-2V5zm2 4h-2v2h2V9z" clip-rule="evenodd"/>
            </svg>
            Create your organisation
          </a>

          <!-- Trusted by strip -->
          <div class="trusted-strip">
            <span class="trusted-strip__label">Trusted by teams at</span>
            <div class="trusted-strip__logos">
              @for (c of companies; track c) {
                <span class="trusted-logo">{{ c }}</span>
              }
            </div>
          </div>

        </div>

        <p class="legal-note">
          By signing in you agree to our
          <a href="/terms">Terms</a> and <a href="/privacy">Privacy Policy</a>.
        </p>
      </main>

    </div>
  `,
  styles: [`
    @import url('https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600;700&family=Sora:wght@500;600;700&display=swap');

    :host { display: block; }

    /* ── Shell — two-panel layout ────────────────────────────────────────── */
    .auth-shell {
      min-height: 100dvh;
      display: grid;
      grid-template-columns: 1fr 1fr;
      font-family: 'Inter', system-ui, -apple-system, sans-serif;
      background: #060b18;
      position: relative;
      overflow: hidden;
      color: #e2e8f0;
    }

    /* ── Ambient background ──────────────────────────────────────────────── */
    .auth-bg {
      position: fixed;
      inset: 0;
      pointer-events: none;
      z-index: 0;
    }

    .auth-bg__grid {
      position: absolute;
      inset: 0;
      background-image:
        linear-gradient(rgba(56, 189, 248, 0.06) 1px, transparent 1px),
        linear-gradient(90deg, rgba(56, 189, 248, 0.06) 1px, transparent 1px);
      background-size: 48px 48px;
      mask-image: radial-gradient(ellipse 60% 55% at 50% 50%, black 20%, transparent 100%);
    }

    .auth-bg__glow {
      position: absolute;
      border-radius: 50%;
      filter: blur(120px);

      &--1 { width: 700px; height: 700px; top: -20%; left: -10%; background: rgba(99, 102, 241, 0.12); }
      &--2 { width: 500px; height: 500px; bottom: -15%; right: -5%; background: rgba(14, 165, 233, 0.1); }
      &--3 { width: 350px; height: 350px; top: 40%; left: 30%; background: rgba(16, 185, 129, 0.07); }
    }

    /* ── Brand panel (left) ─────────────────────────────────────────────── */
    .auth-brand {
      position: relative;
      z-index: 1;
      display: flex;
      flex-direction: column;
      padding: 2rem 2.5rem;
      border-right: 1px solid rgba(148, 163, 184, 0.08);
      overflow: hidden;
    }

    /* Decorative shapes */
    .brand-decor {
      position: absolute;
      inset: 0;
      pointer-events: none;
    }

    .decor-ring {
      position: absolute;
      border-radius: 50%;
      border: 1px solid rgba(129, 140, 248, 0.12);

      &--1 { width: 300px; height: 300px; top: -60px; right: -80px; animation: ring-float 20s ease-in-out infinite; }
      &--2 { width: 200px; height: 200px; bottom: 15%; left: -50px; border-color: rgba(56, 189, 248, 0.1); animation: ring-float 16s ease-in-out infinite reverse; }
    }

    .decor-dot {
      position: absolute;
      border-radius: 50%;
      background: rgba(129, 140, 248, 0.25);

      &--1 { width: 6px; height: 6px; top: 18%; right: 25%; animation: dot-drift 8s ease-in-out infinite; }
      &--2 { width: 4px; height: 4px; top: 55%; right: 12%; background: rgba(56, 189, 248, 0.3); animation: dot-drift 6s ease-in-out infinite 1s; }
      &--3 { width: 5px; height: 5px; bottom: 25%; left: 20%; background: rgba(16, 185, 129, 0.25); animation: dot-drift 7s ease-in-out infinite 2s; }
    }

    @keyframes ring-float {
      0%, 100% { transform: translate(0, 0) rotate(0deg); }
      50% { transform: translate(12px, -15px) rotate(8deg); }
    }

    @keyframes dot-drift {
      0%, 100% { transform: translateY(0); opacity: 0.4; }
      50% { transform: translateY(-12px); opacity: 1; }
    }

    .auth-brand__logo {
      display: flex;
      align-items: center;
      gap: 0.55rem;
    }

    .auth-brand__name {
      font-size: 1rem;
      font-weight: 600;
      color: #e2e8f0;
      letter-spacing: -0.01em;
    }

    .auth-brand__body {
      flex: 1;
      display: flex;
      flex-direction: column;
      justify-content: center;
      gap: 1.5rem;
      padding: 2rem 0;
    }

    .auth-brand__headline {
      font-family: 'Sora', system-ui, sans-serif;
      font-size: clamp(1.8rem, 3vw, 2.6rem);
      font-weight: 700;
      line-height: 1.15;
      color: #f1f5f9;
      margin: 0;
      letter-spacing: -0.03em;

      em {
        font-style: italic;
        background: linear-gradient(135deg, #818cf8, #38bdf8);
        -webkit-background-clip: text;
        -webkit-text-fill-color: transparent;
        background-clip: text;
      }
    }

    .auth-brand__sub {
      font-size: 0.9rem;
      color: #64748b;
      line-height: 1.7;
      margin: 0;
      max-width: 380px;
    }

    /* ── Stats row ──────────────────────────────────────────────────────── */
    .stats-row {
      display: grid;
      grid-template-columns: repeat(3, 1fr);
      gap: 0.75rem;
    }

    .stat-card {
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: 0.2rem;
      padding: 0.85rem 0.5rem;
      border-radius: 12px;
      background: rgba(15, 23, 42, 0.5);
      border: 1px solid rgba(148, 163, 184, 0.08);
      transition: border-color 0.2s;

      &:hover { border-color: rgba(129, 140, 248, 0.2); }
    }

    .stat-card__value {
      font-family: 'Sora', system-ui, sans-serif;
      font-size: 1.35rem;
      font-weight: 700;
      letter-spacing: -0.02em;
      background: linear-gradient(135deg, #818cf8, #38bdf8);
      -webkit-background-clip: text;
      -webkit-text-fill-color: transparent;
      background-clip: text;
    }

    .stat-card__label {
      font-size: 0.68rem;
      color: #64748b;
      text-align: center;
      line-height: 1.3;
    }

    /* ── Mini illustration cards ─────────────────────────────────────────── */
    .mini-cards {
      display: grid;
      grid-template-columns: 1fr 1fr;
      gap: 0.75rem;
    }

    .mini-card {
      border-radius: 12px;
      border: 1px solid rgba(148, 163, 184, 0.1);
      background: rgba(15, 23, 42, 0.5);
      overflow: hidden;
      transition: border-color 0.2s;

      &:hover { border-color: rgba(129, 140, 248, 0.2); }

      img {
        display: block;
        width: 100%;
        height: auto;
        opacity: 0.85;
      }
    }

    .mini-card__label {
      display: flex;
      align-items: center;
      gap: 0.35rem;
      padding: 0.5rem 0.7rem;
      font-size: 0.72rem;
      font-weight: 500;
      color: #94a3b8;
    }

    /* ── Testimonial ────────────────────────────────────────────────────── */
    .testimonial {
      padding: 1rem 1.1rem;
      border-radius: 14px;
      background: rgba(15, 23, 42, 0.5);
      border: 1px solid rgba(148, 163, 184, 0.08);
    }

    .testimonial__stars {
      display: flex;
      gap: 0.15rem;
      margin-bottom: 0.5rem;
      color: #fbbf24;
    }

    .testimonial__quote {
      margin: 0 0 0.65rem;
      font-size: 0.82rem;
      color: #cbd5e1;
      line-height: 1.6;
      font-style: italic;
    }

    .testimonial__author {
      display: flex;
      align-items: center;
      gap: 0.6rem;
    }

    .testimonial__avatar {
      width: 32px; height: 32px;
      border-radius: 50%;
      background: linear-gradient(135deg, #6366f1, #0ea5e9);
      display: flex;
      align-items: center;
      justify-content: center;
      font-size: 0.65rem;
      font-weight: 700;
      color: #fff;
    }

    .testimonial__name {
      font-size: 0.78rem;
      font-weight: 600;
      color: #e2e8f0;
    }

    .testimonial__role {
      font-size: 0.68rem;
      color: #64748b;
    }

    /* ── Footer ─────────────────────────────────────────────────────────── */
    .auth-brand__footer {
      display: flex;
      flex-direction: column;
      gap: 0.65rem;
      padding-top: 1.5rem;
      border-top: 1px solid rgba(148, 163, 184, 0.08);
    }

    .footer-top {
      display: flex;
      align-items: center;
      gap: 0.75rem;
    }

    .status-badge {
      display: flex;
      align-items: center;
      gap: 0.4rem;
      font-size: 0.72rem;
      color: #34d399;
      font-weight: 500;
    }

    .status-dot {
      width: 7px; height: 7px;
      border-radius: 50%;
      background: #34d399;
      box-shadow: 0 0 6px rgba(52, 211, 153, 0.5);
      animation: pulse-dot 2s ease-in-out infinite;
    }

    @keyframes pulse-dot {
      0%, 100% { opacity: 1; }
      50% { opacity: 0.5; }
    }

    .tech-logos {
      display: flex;
      gap: 0.45rem;
      flex-wrap: wrap;
    }

    .tech-logo {
      font-size: 0.65rem;
      font-weight: 500;
      color: #475569;
      padding: 0.2rem 0.5rem;
      border-radius: 6px;
      border: 1px solid rgba(148, 163, 184, 0.1);
      background: rgba(15, 23, 42, 0.4);
      letter-spacing: 0.02em;
    }

    /* ── Form panel (right) ─────────────────────────────────────────────── */
    .auth-form-panel {
      position: relative;
      z-index: 1;
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      padding: 2rem 1.5rem;
      overflow-y: auto;
    }

    .auth-card {
      width: 100%;
      max-width: 420px;
      background: rgba(15, 23, 42, 0.65);
      border: 1px solid rgba(148, 163, 184, 0.12);
      border-radius: 20px;
      padding: 2rem 2rem 1.5rem;
      box-shadow:
        0 25px 60px rgba(0, 0, 0, 0.45),
        0 0 0 1px rgba(255, 255, 255, 0.03) inset;
      backdrop-filter: blur(20px);
      animation: card-in 0.5s cubic-bezier(0.16, 1, 0.3, 1) both;
    }

    @keyframes card-in {
      from { opacity: 0; transform: translateY(20px) scale(0.97); }
      to   { opacity: 1; transform: translateY(0) scale(1); }
    }

    .auth-card__header {
      margin-bottom: 1.75rem;
      text-align: center;

      h2 {
        margin: 0 0 0.3rem;
        font-size: 1.5rem;
        font-weight: 700;
        font-family: 'Sora', system-ui, sans-serif;
        color: #f1f5f9;
        letter-spacing: -0.03em;
      }

      p {
        margin: 0;
        font-size: 0.85rem;
        color: #64748b;
      }
    }

    /* ── Session banner ──────────────────────────────────────────────────── */
    .session-banner {
      display: flex;
      align-items: center;
      gap: 0.5rem;
      padding: 0.65rem 0.9rem;
      background: rgba(245, 158, 11, 0.08);
      border: 1px solid rgba(245, 158, 11, 0.25);
      border-radius: 10px;
      color: #f59e0b;
      font-size: 0.8rem;
      margin-bottom: 1.25rem;
    }

    /* ── Fields ──────────────────────────────────────────────────────────── */
    .field {
      display: flex;
      flex-direction: column;
      gap: 0.45rem;
      margin-bottom: 1.15rem;

      label {
        font-size: 0.78rem;
        font-weight: 500;
        color: #94a3b8;
        letter-spacing: 0.01em;
      }

      &__label-row {
        display: flex;
        justify-content: space-between;
        align-items: center;
      }
    }

    .input-wrap {
      position: relative;
      display: flex;
      align-items: center;
      width: 100%;
      height: 2.85rem;
      border: 1px solid rgba(148, 163, 184, 0.18);
      border-radius: 12px;
      background: rgba(15, 23, 42, 0.6);
      transition: border-color 0.2s ease, box-shadow 0.2s ease;

      &:focus-within {
        border-color: rgba(99, 102, 241, 0.6);
        box-shadow: 0 0 0 3px rgba(99, 102, 241, 0.12);
      }
    }

    .input-wrap--invalid {
      border-color: rgba(239, 68, 68, 0.5) !important;
      box-shadow: 0 0 0 3px rgba(239, 68, 68, 0.08) !important;
    }

    .input-icon {
      position: absolute;
      left: 0.85rem;
      color: #475569;
      flex-shrink: 0;
      pointer-events: none;
      transition: color 0.2s;
    }

    .input-wrap:focus-within .input-icon {
      color: #818cf8;
    }

    .auth-input {
      width: 100%;
      height: 100%;
      border: 0;
      border-radius: 12px;
      background: transparent;
      color: #f1f5f9;
      font-family: inherit;
      font-size: 0.9rem;
      line-height: 1.2;
      padding: 0 0.85rem 0 2.65rem;
      outline: none;

      &::placeholder { color: #475569; }
    }

    .toggle-pw {
      position: absolute;
      right: 0.6rem;
      display: flex;
      align-items: center;
      justify-content: center;
      width: 32px;
      height: 32px;
      border: none;
      border-radius: 8px;
      background: transparent;
      color: #475569;
      cursor: pointer;
      transition: color 0.15s, background 0.15s;

      &:hover { color: #94a3b8; background: rgba(148, 163, 184, 0.08); }
    }

    .field-error {
      color: #f87171;
      font-size: 0.72rem;
      padding-left: 0.1rem;
    }

    .forgot-link {
      font-size: 0.76rem;
      color: #818cf8;
      text-decoration: none;
      transition: color 0.15s;
      &:hover { color: #a5b4fc; text-decoration: underline; }
    }

    /* ── Server error ────────────────────────────────────────────────────── */
    .server-error {
      display: flex;
      align-items: center;
      gap: 0.5rem;
      padding: 0.65rem 0.9rem;
      background: rgba(239, 68, 68, 0.08);
      border: 1px solid rgba(239, 68, 68, 0.2);
      border-radius: 10px;
      color: #f87171;
      font-size: 0.82rem;
      margin-bottom: 1rem;
    }

    /* ── Submit button ───────────────────────────────────────────────────── */
    .submit-btn {
      display: flex;
      align-items: center;
      justify-content: center;
      gap: 0.5rem;
      width: 100%;
      height: 2.85rem;
      margin-top: 0.25rem;
      font-family: inherit;
      font-size: 0.9rem;
      font-weight: 600;
      letter-spacing: -0.01em;
      color: #ffffff;
      border: none;
      border-radius: 12px;
      background: linear-gradient(135deg, #6366f1 0%, #0ea5e9 50%, #10b981 100%);
      background-size: 200% 200%;
      cursor: pointer;
      transition: transform 0.15s ease, box-shadow 0.15s ease, opacity 0.15s;
      box-shadow: 0 4px 15px rgba(99, 102, 241, 0.25);

      &:hover:not(:disabled) {
        transform: translateY(-1px);
        box-shadow: 0 6px 20px rgba(99, 102, 241, 0.35);
      }

      &:active:not(:disabled) { transform: translateY(0); }

      &:disabled { opacity: 0.5; cursor: not-allowed; }

      span, svg { color: #ffffff; }
    }

    .spinner { animation: spin 0.7s linear infinite; }
    @keyframes spin { to { transform: rotate(360deg); } }

    /* ── Divider / links ─────────────────────────────────────────────────── */
    .auth-divider {
      position: relative;
      margin: 1.25rem 0 0.9rem;
      text-align: center;

      &::before {
        content: '';
        position: absolute;
        left: 0; right: 0; top: 50%;
        height: 1px;
        background: linear-gradient(90deg, transparent, rgba(148, 163, 184, 0.2), transparent);
      }
    }

    .divider-text {
      position: relative;
      z-index: 1;
      padding: 0 0.75rem;
      background: rgba(15, 23, 42, 0.65);
      font-size: 0.78rem;
      color: #64748b;
      white-space: nowrap;
    }

    .signup-link {
      display: flex;
      align-items: center;
      justify-content: center;
      gap: 0.45rem;
      color: #94a3b8;
      text-decoration: none;
      font-size: 0.85rem;
      font-weight: 500;
      padding: 0.7rem;
      border-radius: 12px;
      border: 1px solid rgba(148, 163, 184, 0.15);
      background: rgba(148, 163, 184, 0.03);
      transition: border-color 0.15s, background 0.15s, color 0.15s;

      &:hover {
        border-color: rgba(129, 140, 248, 0.4);
        background: rgba(99, 102, 241, 0.06);
        color: #c7d2fe;
      }
    }

    /* ── Trusted by strip ────────────────────────────────────────────────── */
    .trusted-strip {
      margin-top: 1.25rem;
      text-align: center;
    }

    .trusted-strip__label {
      font-size: 0.68rem;
      color: #475569;
      text-transform: uppercase;
      letter-spacing: 0.06em;
      font-weight: 500;
    }

    .trusted-strip__logos {
      display: flex;
      justify-content: center;
      gap: 0.75rem;
      margin-top: 0.5rem;
      flex-wrap: wrap;
    }

    .trusted-logo {
      font-size: 0.72rem;
      font-weight: 600;
      color: #334155;
      letter-spacing: 0.02em;
    }

    /* ── Legal note ──────────────────────────────────────────────────────── */
    .legal-note {
      font-size: 0.72rem;
      color: #475569;
      text-align: center;
      margin: 1rem 0 0;

      a {
        color: #818cf8;
        text-decoration: none;
        &:hover { text-decoration: underline; }
      }
    }

    /* ── Responsive ──────────────────────────────────────────────────────── */
    @media (max-width: 980px) {
      .auth-shell { grid-template-columns: 1fr; }
      .auth-brand { display: none; }
    }

    @media (max-width: 640px) {
      .auth-form-panel { padding: 1.25rem 1rem; }

      .auth-card {
        padding: 1.5rem;
        border-radius: 16px;
      }

      .auth-card__header h2 { font-size: 1.3rem; }
    }
  `],
})
export class SigninComponent {
  readonly authStore = inject(AuthStore);
  private readonly authService = inject(MockAuthService);
  private readonly route = inject(ActivatedRoute);

  readonly serverError = signal<string | null>(null);
  readonly sessionExpired = signal(false);
  readonly showPassword = signal(false);
  readonly debugStatus = signal<string | null>(null);

  readonly stats = [
    { value: '10K+', label: 'Databases managed' },
    { value: '99.9%', label: 'Uptime SLA' },
    { value: '<50ms', label: 'Avg. response' },
  ];

  readonly techTags = ['REST', 'GraphQL', 'gRPC', 'WebSocket', 'OAuth 2.0', 'JWT'];
  readonly companies = ['Acme Corp', 'DataForge', 'CloudBridge', 'NexGen', 'Stackline'];

  readonly form = inject(FormBuilder).group({
    email:    ['', [Validators.required, Validators.email]],
    password: ['', [Validators.required]],
  });

  constructor() {
    this.route.queryParams.subscribe((p) => {
      if (p['reason'] === 'session_expired') this.sessionExpired.set(true);
    });
  }

  isDirty(field: string): boolean {
    const ctrl = this.form.get(field);
    return !!(ctrl?.invalid && (ctrl.dirty || ctrl.touched));
  }

  submit(): void {
    this.debugStatus.set('submit() called');
    this.form.markAllAsTouched();
    const { email, password } = this.form.getRawValue();

    if (!email || !password) {
      this.debugStatus.set('Empty fields: email=' + email + ' pw=' + password);
      return;
    }

    this.serverError.set(null);
    this.debugStatus.set('Calling login with ' + email + '...');

    this.authService.login({ email, password }).subscribe({
      next: (res) => {
        this.debugStatus.set('Login OK! Navigating to /dashboard...');
      },
      error: (err) => {
        this.debugStatus.set('Login ERROR: ' + (err?.message ?? JSON.stringify(err)));
        this.serverError.set(
          err?.error?.message ?? 'Invalid email or password.'
        );
      },
    });
  }
}
