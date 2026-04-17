import { CommonModule } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject, signal,
} from '@angular/core';
import {
  AbstractControl,
  FormBuilder,
  ReactiveFormsModule,
  Validators,
} from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MockAuthService } from '../../services/mock-auth.service';
import { AuthStore } from '../../store/auth.store';

function passwordMatch(control: AbstractControl) {
  const pass = control.get('password');
  const confirm = control.get('confirmPassword');
  if (!pass || !confirm) return null;
  return pass.value !== confirm.value ? { mismatch: true } : null;
}

@Component({
  selector: 'app-signup',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CommonModule, RouterLink, ReactiveFormsModule],
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

        <div class="auth-brand__topbar">
          <div class="auth-brand__logo">
            <svg viewBox="0 0 32 32" width="28" height="28" fill="none" aria-hidden="true">
              <path d="M16 2 L28 9 L28 23 L16 30 L4 23 L4 9 Z" stroke="url(#sg)" stroke-width="2" fill="none"/>
              <path d="M16 8 L22 11.5 L22 18.5 L16 22 L10 18.5 L10 11.5 Z" fill="url(#sg)" opacity="0.3"/>
              <defs><linearGradient id="sg" x1="0%" y1="0%" x2="100%" y2="100%">
                <stop offset="0%" stop-color="#38bdf8"/><stop offset="100%" stop-color="#818cf8"/>
              </linearGradient></defs>
            </svg>
            <span class="auth-brand__name">Auto API Engine</span>
          </div>

          <a routerLink="/auth/signin" class="auth-brand__login-btn">
            <span>Login</span>
            <svg viewBox="0 0 20 20" fill="currentColor" width="16" height="16" aria-hidden="true">
              <path fill-rule="evenodd" d="M10.293 3.293a1 1 0 011.414 0l6 6a1 1 0 010 1.414l-6 6a1 1 0 01-1.414-1.414L14.586 11H3a1 1 0 110-2h11.586l-4.293-4.293a1 1 0 010-1.414z" clip-rule="evenodd"/>
            </svg>
          </a>
        </div>

        <div class="auth-brand__body">
          <h1 class="auth-brand__headline">
            Your databases,<br>
            <em>orchestrated.</em>
          </h1>
          <p class="auth-brand__sub">
            Connect any SQL Server, MySQL, PostgreSQL or SQLite database.
            Browse schemas, run queries, and manage backups — all from one place.
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

          <!-- Features -->
          <ul class="auth-brand__features">
            @for (f of features; track f.text) {
              <li>
                <span class="feat-icon">
                  <svg viewBox="0 0 20 20" fill="currentColor" width="16" height="16"><path [attr.d]="f.icon"/></svg>
                </span>
                <span>{{ f.text }}</span>
              </li>
            }
          </ul>

          <!-- Testimonial card -->
          <div class="testimonial">
            <div class="testimonial__stars" aria-label="5 stars">
              @for (i of [1,2,3,4,5]; track i) {
                <svg viewBox="0 0 20 20" fill="currentColor" width="14" height="14">
                  <path d="M9.049 2.927c.3-.921 1.603-.921 1.902 0l1.07 3.292a1 1 0 00.95.69h3.462c.969 0 1.371 1.24.588 1.81l-2.8 2.034a1 1 0 00-.364 1.118l1.07 3.292c.3.921-.755 1.688-1.54 1.118l-2.8-2.034a1 1 0 00-1.175 0l-2.8 2.034c-.784.57-1.838-.197-1.539-1.118l1.07-3.292a1 1 0 00-.364-1.118L2.98 8.72c-.783-.57-.38-1.81.588-1.81h3.461a1 1 0 00.951-.69l1.07-3.292z"/>
                </svg>
              }
            </div>
            <p class="testimonial__quote">
              "We migrated 12 production databases in a single afternoon.
              The schema explorer alone saved us weeks of documentation work."
            </p>
            <div class="testimonial__author">
              <div class="testimonial__avatar">MK</div>
              <div>
                <div class="testimonial__name">Marcus Kowalski</div>
                <div class="testimonial__role">CTO, DataForge Systems</div>
              </div>
            </div>
          </div>
        </div>

        <!-- Tech logos strip -->
        <div class="auth-brand__footer">
          <div class="footer-top">
            <span class="trial-badge">Trial plan</span>
            <span class="auth-brand__plan-note">Free for 14 days · No credit card required</span>
          </div>
          <div class="tech-logos">
            @for (db of databases; track db) {
              <span class="tech-logo">{{ db }}</span>
            }
          </div>
        </div>
      </aside>

      <!-- ── Right form panel ────────────────────────────────────────────── -->
      <main class="auth-form-panel">
        <div class="auth-card">

          <header class="auth-card__header">
            <h2>Create your account</h2>
            <p>One account = one organisation. You can invite teammates after setup.</p>
          </header>

          <form [formGroup]="form" (ngSubmit)="submit()" novalidate autocomplete="off">

            <!-- Organisation name -->
            <div class="field">
              <label for="orgName">Organisation name</label>
              <div class="input-wrap" [class.input-wrap--invalid]="isDirty('organizationName')">
                <svg class="input-icon" viewBox="0 0 20 20" fill="currentColor" width="18" height="18" aria-hidden="true">
                  <path fill-rule="evenodd" d="M4 4a2 2 0 012-2h8a2 2 0 012 2v12a1 1 0 110 2h-3a1 1 0 01-1-1v-2a1 1 0 00-1-1H9a1 1 0 00-1 1v2a1 1 0 01-1 1H4a1 1 0 110-2V4zm3 1h2v2H7V5zm2 4H7v2h2V9zm2-4h2v2h-2V5zm2 4h-2v2h2V9z" clip-rule="evenodd"/>
                </svg>
                <input id="orgName" type="text" formControlName="organizationName"
                  placeholder="Acme Corp" class="auth-input" autocomplete="organization"/>
              </div>
              @if (isDirty('organizationName')) {
                <small class="field-error">Organisation name is required</small>
              }
            </div>

            <!-- First / Last name -->
            <div class="field-row">
              <div class="field">
                <label for="firstName">First name</label>
                <div class="input-wrap" [class.input-wrap--invalid]="isDirty('firstName')">
                  <input id="firstName" type="text" formControlName="firstName"
                    placeholder="Jane" class="auth-input auth-input--no-icon" autocomplete="given-name"/>
                </div>
                @if (isDirty('firstName')) { <small class="field-error">Required</small> }
              </div>
              <div class="field">
                <label for="lastName">Last name</label>
                <div class="input-wrap" [class.input-wrap--invalid]="isDirty('lastName')">
                  <input id="lastName" type="text" formControlName="lastName"
                    placeholder="Smith" class="auth-input auth-input--no-icon" autocomplete="family-name"/>
                </div>
                @if (isDirty('lastName')) { <small class="field-error">Required</small> }
              </div>
            </div>

            <!-- Email -->
            <div class="field">
              <label for="email">Work email</label>
              <div class="input-wrap" [class.input-wrap--invalid]="isDirty('email')">
                <svg class="input-icon" viewBox="0 0 20 20" fill="currentColor" width="18" height="18" aria-hidden="true">
                  <path d="M2.003 5.884L10 9.882l7.997-3.998A2 2 0 0016 4H4a2 2 0 00-1.997 1.884z"/>
                  <path d="M18 8.118l-8 4-8-4V14a2 2 0 002 2h12a2 2 0 002-2V8.118z"/>
                </svg>
                <input id="email" type="email" formControlName="email"
                  placeholder="jane&#64;acme.com" class="auth-input" autocomplete="email"/>
              </div>
              @if (isDirty('email')) {
                <small class="field-error">
                  {{ form.get('email')?.errors?.['required'] ? 'Email is required' : 'Enter a valid email address' }}
                </small>
              }
            </div>

            <!-- Password -->
            <div formGroupName="passwords" class="field">
              <label for="password">Password</label>
              <div class="input-wrap" [class.input-wrap--invalid]="isDirtyNested('passwords', 'password')">
                <svg class="input-icon" viewBox="0 0 20 20" fill="currentColor" width="18" height="18" aria-hidden="true">
                  <path fill-rule="evenodd" d="M5 9V7a5 5 0 0110 0v2a2 2 0 012 2v5a2 2 0 01-2 2H5a2 2 0 01-2-2v-5a2 2 0 012-2zm8-2v2H7V7a3 3 0 016 0z" clip-rule="evenodd"/>
                </svg>
                <input id="password" [type]="showPw() ? 'text' : 'password'"
                  formControlName="password" placeholder="Min. 8 characters" class="auth-input" autocomplete="new-password"/>
                <button type="button" class="toggle-pw" (click)="showPw.set(!showPw())"
                  [attr.aria-label]="showPw() ? 'Hide password' : 'Show password'">
                  @if (showPw()) {
                    <svg viewBox="0 0 20 20" fill="currentColor" width="18" height="18"><path fill-rule="evenodd" d="M3.707 2.293a1 1 0 00-1.414 1.414l14 14a1 1 0 001.414-1.414l-1.473-1.473A10.014 10.014 0 0019.542 10C18.268 5.943 14.478 3 10 3a9.958 9.958 0 00-4.512 1.074l-1.78-1.781zm4.261 4.26l1.514 1.515a2.003 2.003 0 012.45 2.45l1.514 1.514a4 4 0 00-5.478-5.478z" clip-rule="evenodd"/><path d="M12.454 16.697L9.75 13.992a4 4 0 01-3.742-3.741L2.335 6.578A9.98 9.98 0 00.458 10c1.274 4.057 5.065 7 9.542 7 .847 0 1.669-.105 2.454-.303z"/></svg>
                  } @else {
                    <svg viewBox="0 0 20 20" fill="currentColor" width="18" height="18"><path d="M10 12a2 2 0 100-4 2 2 0 000 4z"/><path fill-rule="evenodd" d="M.458 10C1.732 5.943 5.522 3 10 3s8.268 2.943 9.542 7c-1.274 4.057-5.064 7-9.542 7S1.732 14.057.458 10zM14 10a4 4 0 11-8 0 4 4 0 018 0z" clip-rule="evenodd"/></svg>
                  }
                </button>
              </div>
              @if (isDirtyNested('passwords', 'password')) {
                <small class="field-error">Password must be at least 8 characters</small>
              }
            </div>

            <!-- Confirm password -->
            <div formGroupName="passwords" class="field">
              <label for="confirmPassword">Confirm password</label>
              <div class="input-wrap" [class.input-wrap--invalid]="passwordMismatch()">
                <svg class="input-icon" viewBox="0 0 20 20" fill="currentColor" width="18" height="18" aria-hidden="true">
                  <path fill-rule="evenodd" d="M5 9V7a5 5 0 0110 0v2a2 2 0 012 2v5a2 2 0 01-2 2H5a2 2 0 01-2-2v-5a2 2 0 012-2zm8-2v2H7V7a3 3 0 016 0z" clip-rule="evenodd"/>
                </svg>
                <input id="confirmPassword" [type]="showConfirmPw() ? 'text' : 'password'"
                  formControlName="confirmPassword" placeholder="Repeat password" class="auth-input" autocomplete="new-password"/>
                <button type="button" class="toggle-pw" (click)="showConfirmPw.set(!showConfirmPw())"
                  [attr.aria-label]="showConfirmPw() ? 'Hide password' : 'Show password'">
                  @if (showConfirmPw()) {
                    <svg viewBox="0 0 20 20" fill="currentColor" width="18" height="18"><path fill-rule="evenodd" d="M3.707 2.293a1 1 0 00-1.414 1.414l14 14a1 1 0 001.414-1.414l-1.473-1.473A10.014 10.014 0 0019.542 10C18.268 5.943 14.478 3 10 3a9.958 9.958 0 00-4.512 1.074l-1.78-1.781zm4.261 4.26l1.514 1.515a2.003 2.003 0 012.45 2.45l1.514 1.514a4 4 0 00-5.478-5.478z" clip-rule="evenodd"/><path d="M12.454 16.697L9.75 13.992a4 4 0 01-3.742-3.741L2.335 6.578A9.98 9.98 0 00.458 10c1.274 4.057 5.065 7 9.542 7 .847 0 1.669-.105 2.454-.303z"/></svg>
                  } @else {
                    <svg viewBox="0 0 20 20" fill="currentColor" width="18" height="18"><path d="M10 12a2 2 0 100-4 2 2 0 000 4z"/><path fill-rule="evenodd" d="M.458 10C1.732 5.943 5.522 3 10 3s8.268 2.943 9.542 7c-1.274 4.057-5.064 7-9.542 7S1.732 14.057.458 10zM14 10a4 4 0 11-8 0 4 4 0 018 0z" clip-rule="evenodd"/></svg>
                  }
                </button>
              </div>
              @if (passwordMismatch()) {
                <small class="field-error">Passwords do not match</small>
              }
            </div>

            <!-- Terms -->
            <label class="checkbox-wrap">
              <input type="checkbox" formControlName="terms" class="checkbox-input"/>
              <span class="checkbox-box">
                <svg viewBox="0 0 12 12" width="12" height="12" fill="none" aria-hidden="true">
                  <path d="M2.5 6L5 8.5L9.5 3.5" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/>
                </svg>
              </span>
              <span class="checkbox-label">
                I agree to the
                <a href="/terms" target="_blank">Terms of Service</a>
                and
                <a href="/privacy" target="_blank">Privacy Policy</a>
              </span>
            </label>

            <!-- Server error -->
            @if (serverError()) {
              <div class="server-error">
                <svg viewBox="0 0 20 20" fill="currentColor" width="16" height="16"><path fill-rule="evenodd" d="M8.257 3.099c.765-1.36 2.722-1.36 3.486 0l5.58 9.92c.75 1.334-.213 2.98-1.742 2.98H4.42c-1.53 0-2.493-1.646-1.743-2.98l5.58-9.92zM11 13a1 1 0 11-2 0 1 1 0 012 0zm-1-8a1 1 0 00-1 1v3a1 1 0 002 0V6a1 1 0 00-1-1z" clip-rule="evenodd"/></svg>
                {{ serverError() }}
              </div>
            }

            <!-- Submit -->
            <button type="submit" class="submit-btn"
              [disabled]="authStore.isLoading()">
              @if (authStore.isLoading()) {
                <svg class="spinner" viewBox="0 0 24 24" width="20" height="20" aria-hidden="true">
                  <circle cx="12" cy="12" r="10" stroke="currentColor" stroke-width="3" fill="none" stroke-linecap="round" stroke-dasharray="31.4 31.4"/>
                </svg>
                <span>Creating account…</span>
              } @else {
                <span>Create account</span>
                <svg viewBox="0 0 20 20" fill="currentColor" width="18" height="18" aria-hidden="true">
                  <path fill-rule="evenodd" d="M10.293 3.293a1 1 0 011.414 0l6 6a1 1 0 010 1.414l-6 6a1 1 0 01-1.414-1.414L14.586 11H3a1 1 0 110-2h11.586l-4.293-4.293a1 1 0 010-1.414z" clip-rule="evenodd"/>
                </svg>
              }
            </button>

          </form>

          <!-- Divider -->
          <div class="auth-divider" role="separator" aria-hidden="true">
            <span class="divider-text">Already have an account?</span>
          </div>

          <a routerLink="/auth/signin" class="signin-link">
            Sign in instead
            <svg viewBox="0 0 20 20" fill="currentColor" width="16" height="16" aria-hidden="true">
              <path fill-rule="evenodd" d="M10.293 3.293a1 1 0 011.414 0l6 6a1 1 0 010 1.414l-6 6a1 1 0 01-1.414-1.414L14.586 11H3a1 1 0 110-2h11.586l-4.293-4.293a1 1 0 010-1.414z" clip-rule="evenodd"/>
            </svg>
          </a>

        </div>
      </main>

    </div>
  `,
  styles: [`
    @import url('https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600;700&family=Sora:wght@500;600;700&display=swap');

    :host { display: block; }

    /* ── Shell ──────────────────────────────────────────────────────────── */
    .auth-shell {
      min-height: 100dvh;
      display: grid;
      grid-template-columns: 1fr 1fr;
      position: relative;
      background: #060b18;
      overflow: hidden;
      font-family: 'Inter', system-ui, -apple-system, sans-serif;
      color: #e2e8f0;
    }

    /* ── Background ────────────────────────────────────────────────────── */
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

    /* ── Decorative floating shapes ─────────────────────────────────────── */
    .brand-decor {
      position: absolute;
      inset: 0;
      pointer-events: none;
      overflow: hidden;
    }

    .decor-ring {
      position: absolute;
      border-radius: 50%;
      border: 1px solid rgba(129, 140, 248, 0.12);

      &--1 {
        width: 300px; height: 300px;
        top: -60px; right: -80px;
        animation: ring-float 20s ease-in-out infinite;
      }
      &--2 {
        width: 200px; height: 200px;
        bottom: 15%; left: -50px;
        border-color: rgba(56, 189, 248, 0.1);
        animation: ring-float 16s ease-in-out infinite reverse;
      }
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

    .auth-brand__topbar {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 1rem;
    }

    .auth-brand__name {
      font-size: 1rem;
      font-weight: 600;
      color: #e2e8f0;
      letter-spacing: -0.01em;
    }

    .auth-brand__login-btn {
      display: inline-flex;
      align-items: center;
      gap: 0.35rem;
      padding: 0.55rem 0.85rem;
      border-radius: 999px;
      border: 1px solid rgba(148, 163, 184, 0.18);
      background: rgba(15, 23, 42, 0.55);
      color: #cbd5e1;
      text-decoration: none;
      font-size: 0.82rem;
      font-weight: 600;
      transition: border-color 0.15s, background 0.15s, color 0.15s;

      &:hover {
        border-color: rgba(129, 140, 248, 0.45);
        background: rgba(99, 102, 241, 0.08);
        color: #e2e8f0;
      }
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

    /* ── Features ───────────────────────────────────────────────────────── */
    .auth-brand__features {
      list-style: none;
      margin: 0;
      padding: 0;
      display: grid;
      grid-template-columns: 1fr 1fr;
      gap: 0.6rem;

      li {
        display: flex;
        align-items: center;
        gap: 0.6rem;
        color: #94a3b8;
        font-size: 0.8rem;
        padding: 0.55rem 0.65rem;
        border-radius: 10px;
        background: rgba(15, 23, 42, 0.4);
        border: 1px solid rgba(148, 163, 184, 0.06);
        transition: border-color 0.2s, background 0.2s;

        &:hover {
          border-color: rgba(129, 140, 248, 0.15);
          background: rgba(15, 23, 42, 0.6);
        }
      }
    }

    .feat-icon {
      width: 28px; height: 28px;
      border-radius: 7px;
      background: rgba(99, 102, 241, 0.1);
      display: flex;
      align-items: center;
      justify-content: center;
      flex-shrink: 0;
      color: #818cf8;
    }

    /* ── Testimonial ────────────────────────────────────────────────────── */
    .testimonial {
      padding: 1.1rem 1.2rem;
      border-radius: 14px;
      background: rgba(15, 23, 42, 0.5);
      border: 1px solid rgba(148, 163, 184, 0.08);
    }

    .testimonial__stars {
      display: flex;
      gap: 0.15rem;
      margin-bottom: 0.6rem;
      color: #fbbf24;
    }

    .testimonial__quote {
      margin: 0 0 0.75rem;
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
      letter-spacing: 0.02em;
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
      gap: 0.75rem;
      padding-top: 1.5rem;
      border-top: 1px solid rgba(148, 163, 184, 0.08);
    }

    .footer-top {
      display: flex;
      align-items: center;
      gap: 0.75rem;
    }

    .trial-badge {
      font-size: 0.68rem;
      font-weight: 600;
      text-transform: uppercase;
      letter-spacing: 0.05em;
      padding: 0.2rem 0.55rem;
      border-radius: 6px;
      background: rgba(16, 185, 129, 0.12);
      color: #34d399;
    }

    .auth-brand__plan-note {
      font-size: 0.78rem;
      color: #64748b;
    }

    .tech-logos {
      display: flex;
      gap: 0.5rem;
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
      align-items: center;
      justify-content: center;
      padding: 2rem 1.5rem;
      overflow-y: auto;
    }

    .auth-card {
      width: 100%;
      max-width: 440px;
      background: rgba(15, 23, 42, 0.65);
      border: 1px solid rgba(148, 163, 184, 0.12);
      border-radius: 20px;
      padding: 2rem 2rem 1.75rem;
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
      margin-bottom: 1.5rem;

      h2 {
        margin: 0 0 0.3rem;
        font-size: 1.4rem;
        font-weight: 700;
        font-family: 'Sora', system-ui, sans-serif;
        color: #f1f5f9;
        letter-spacing: -0.03em;
      }

      p {
        margin: 0;
        font-size: 0.82rem;
        color: #64748b;
        line-height: 1.5;
      }
    }

    /* ── Fields ─────────────────────────────────────────────────────────── */
    .field {
      display: flex;
      flex-direction: column;
      gap: 0.4rem;
      margin-bottom: 1rem;

      label:not(.checkbox-wrap) {
        font-size: 0.78rem;
        font-weight: 500;
        color: #94a3b8;
        letter-spacing: 0.01em;
      }
    }

    .field-row {
      display: grid;
      grid-template-columns: 1fr 1fr;
      gap: 0.75rem;
    }

    .input-wrap {
      position: relative;
      display: flex;
      align-items: center;
      width: 100%;
      height: 2.7rem;
      border: 1px solid rgba(148, 163, 184, 0.18);
      border-radius: 10px;
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
      left: 0.75rem;
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
      border-radius: 10px;
      background: transparent;
      color: #f1f5f9;
      font-family: inherit;
      font-size: 0.88rem;
      line-height: 1.2;
      padding: 0 0.75rem 0 2.5rem;
      outline: none;

      &::placeholder { color: #475569; }
    }

    .auth-input--no-icon {
      padding-left: 0.75rem;
    }

    .toggle-pw {
      position: absolute;
      right: 0.5rem;
      display: flex;
      align-items: center;
      justify-content: center;
      width: 30px;
      height: 30px;
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

    /* ── Checkbox ───────────────────────────────────────────────────────── */
    .checkbox-wrap {
      display: flex;
      align-items: flex-start;
      gap: 0.6rem;
      margin-bottom: 1.25rem;
      cursor: pointer;
    }

    .checkbox-input {
      position: absolute;
      opacity: 0;
      width: 0;
      height: 0;
    }

    .checkbox-box {
      flex-shrink: 0;
      width: 20px;
      height: 20px;
      margin-top: 1px;
      border-radius: 6px;
      border: 1px solid rgba(148, 163, 184, 0.25);
      background: rgba(15, 23, 42, 0.6);
      display: flex;
      align-items: center;
      justify-content: center;
      color: transparent;
      transition: border-color 0.15s, background 0.15s, color 0.15s;
    }

    .checkbox-input:checked + .checkbox-box {
      border-color: #818cf8;
      background: #6366f1;
      color: #ffffff;
    }

    .checkbox-input:focus-visible + .checkbox-box {
      box-shadow: 0 0 0 3px rgba(99, 102, 241, 0.2);
    }

    .checkbox-label {
      font-size: 0.8rem;
      color: #94a3b8;
      line-height: 1.5;

      a {
        color: #818cf8;
        text-decoration: none;
        &:hover { text-decoration: underline; }
      }
    }

    /* ── Server error ───────────────────────────────────────────────────── */
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

    /* ── Submit button ──────────────────────────────────────────────────── */
    .submit-btn {
      display: flex;
      align-items: center;
      justify-content: center;
      gap: 0.5rem;
      width: 100%;
      height: 2.75rem;
      margin-top: 0.15rem;
      font-family: inherit;
      font-size: 0.88rem;
      font-weight: 600;
      letter-spacing: -0.01em;
      color: #ffffff;
      border: none;
      border-radius: 10px;
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

      &:disabled {
        opacity: 0.5;
        cursor: not-allowed;
      }

      span, svg { color: #ffffff; }
    }

    .spinner { animation: spin 0.7s linear infinite; }
    @keyframes spin { to { transform: rotate(360deg); } }

    /* ── Divider / link ─────────────────────────────────────────────────── */
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

    .signin-link {
      display: flex;
      align-items: center;
      justify-content: center;
      gap: 0.35rem;
      color: #818cf8;
      text-decoration: none;
      font-size: 0.85rem;
      font-weight: 500;
      transition: color 0.15s;

      &:hover { color: #a5b4fc; }
    }

    /* ── Responsive ─────────────────────────────────────────────────────── */
    @media (max-width: 900px) {
      .auth-shell { grid-template-columns: 1fr; }
      .auth-brand { display: none; }
      .auth-form-panel { padding: 1.5rem 1rem; }
    }
  `],
})
export class SignupComponent {
  readonly authStore = inject(AuthStore);
  private readonly authService = inject(MockAuthService);

  readonly serverError = signal<string | null>(null);
  readonly showPw = signal(false);
  readonly showConfirmPw = signal(false);

  readonly stats = [
    { value: '10K+', label: 'Databases managed' },
    { value: '99.9%', label: 'Uptime SLA' },
    { value: '<50ms', label: 'Avg. response' },
  ];

  readonly databases = ['SQL Server', 'PostgreSQL', 'MySQL', 'SQLite', 'MariaDB', 'REST API'];

  readonly features = [
    { icon: 'M3 12v3c0 1.657 3.134 3 7 3s7-1.343 7-3v-3c0 1.657-3.134 3-7 3s-7-1.343-7-3z M3 7v3c0 1.657 3.134 3 7 3s7-1.343 7-3V7c0 1.657-3.134 3-7 3S3 8.657 3 7z M17 5c0 1.657-3.134 3-7 3S3 6.657 3 5s3.134-3 7-3 7 1.343 7 3z', text: 'SQL Server, MySQL, PostgreSQL & SQLite' },
    { icon: 'M3 17a1 1 0 011-1h12a1 1 0 110 2H4a1 1 0 01-1-1zM6.293 6.707a1 1 0 010-1.414l3-3a1 1 0 011.414 0l3 3a1 1 0 01-1.414 1.414L11 5.414V13a1 1 0 11-2 0V5.414L7.707 6.707a1 1 0 01-1.414 0z', text: 'Upload .bak, .sql and .dump backups' },
    { icon: 'M7 3a1 1 0 000 2h6a1 1 0 100-2H7zM4 7a1 1 0 011-1h10a1 1 0 110 2H5a1 1 0 01-1-1zM2 11a2 2 0 012-2h12a2 2 0 012 2v4a2 2 0 01-2 2H4a2 2 0 01-2-2v-4z', text: 'Visual schema tree explorer' },
    { icon: 'M13 6a3 3 0 11-6 0 3 3 0 016 0zM18 8a2 2 0 11-4 0 2 2 0 014 0zM14 15a4 4 0 00-8 0v3h8v-3zM6 8a2 2 0 11-4 0 2 2 0 014 0zM16 18v-3a5.972 5.972 0 00-.75-2.906A3.005 3.005 0 0119 15v3h-3zM4.75 12.094A5.973 5.973 0 004 15v3H1v-3a3 3 0 013.75-2.906z', text: 'Multi-user with role-based access' },
  ];

  readonly form = inject(FormBuilder).group({
    organizationName: ['', [Validators.required, Validators.minLength(2)]],
    firstName: ['', [Validators.required]],
    lastName: ['', [Validators.required]],
    email: ['', [Validators.required, Validators.email]],
    passwords: inject(FormBuilder).group(
      {
        password: ['', [Validators.required, Validators.minLength(8)]],
        confirmPassword: ['', [Validators.required]],
      },
      { validators: passwordMatch }
    ),
    terms: [false, [Validators.requiredTrue]],
  });

  isDirty(field: string): boolean {
    const ctrl = this.form.get(field);
    return !!(ctrl?.invalid && (ctrl.dirty || ctrl.touched));
  }

  isDirtyNested(group: string, field: string): boolean {
    const ctrl = this.form.get(`${group}.${field}`);
    return !!(ctrl?.invalid && (ctrl.dirty || ctrl.touched));
  }

  passwordMismatch = computed(() => {
    const grp = this.form.get('passwords');
    return grp?.errors?.['mismatch'] && grp?.dirty;
  });

  submit(): void {
    this.form.markAllAsTouched();
    const raw = this.form.getRawValue();

    if (!raw.email || !raw.passwords.password || !raw.firstName || !raw.organizationName) {
      return;
    }

    this.serverError.set(null);

    this.authService.register({
      firstName: raw.firstName,
      lastName: raw.lastName ?? '',
      organizationName: raw.organizationName,
      email: raw.email,
      password: raw.passwords.password,
    }).subscribe({
      next: () => { /* navigation handled in service */ },
      error: (err) => {
        this.serverError.set(
          err?.error?.message ?? 'Registration failed. Please try again.'
        );
      },
    });
  }
}
