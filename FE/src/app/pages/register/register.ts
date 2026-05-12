import { CommonModule } from '@angular/common';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { ChangeDetectorRef, Component, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { catchError, finalize, throwError, timeout } from 'rxjs';
import { environment } from '../../../environments/environment';
import { AuthService } from '../../shared/auth/auth.service';

@Component({
  selector: 'app-register',
  standalone: true,
  imports: [RouterLink, FormsModule, CommonModule],
  templateUrl: './register.html',
  styleUrl: './register.scss'
})
export class Register {
  private authService = inject(AuthService);
  private http = inject(HttpClient);
  private router = inject(Router);
  private cdr = inject(ChangeDetectorRef);

  formData = {
    email: '',
    password: '',
    confirmPassword: '',
    organizationName: ''
  };

  showPassword = false;
  showConfirmPassword = false;
  error = '';
  submitting = false;
  showSuccessOverlay = false;
  showFailureOverlay = false;
  registeredEmail = '';

  onLogin(): void {
    this.authService.login();
  }

  togglePassword(): void {
    this.showPassword = !this.showPassword;
  }

  toggleConfirmPassword(): void {
    this.showConfirmPassword = !this.showConfirmPassword;
  }

  onSubmit(): void {
    if (!this.formData.email || !this.formData.password || !this.formData.organizationName) {
      this.error = 'All fields are required';
      return;
    }

    if (this.formData.password !== this.formData.confirmPassword) {
      this.error = 'Passwords do not match';
      return;
    }

    if (this.formData.password.length < 8) {
      this.error = 'Password must be at least 8 characters';
      return;
    }

    this.submitting = true;
    this.error = '';
    this.showSuccessOverlay = false;
    this.showFailureOverlay = false;

    const headers = new HttpHeaders({
      'Content-Type': 'application/json',
      'Accept': 'application/json'
    });

    this.http.post<any>(`${environment.apiUrl}/auth/register`, {
      Email: this.formData.email,
      Password: this.formData.password,
      OrganizationName: this.formData.organizationName
    }, { headers }).pipe(
      timeout(15000),
      catchError(err => {
        if (err.name === 'TimeoutError') {
          return throwError(() => ({ error: 'Server is not responding. Please try again later.' }));
        }
        return throwError(() => err);
      }),
      finalize(() => {
        this.submitting = false;
        this.cdr.detectChanges();
      })
    ).subscribe({
      next: (response) => {
        if (response.success) {
          this.registeredEmail = this.formData.email;
          this.showSuccessOverlay = true;
          this.cdr.detectChanges();
          setTimeout(() => this.router.navigate(['/']), 5000);
        } else {
          this.error = response.error || 'Registration failed';
          this.showFailureOverlay = true;
          this.cdr.detectChanges();
        }
      },
      error: (err) => {
        const body = err?.error;
        const message = typeof body === 'string' ? body
          : body?.error || body?.message || err?.message || err?.statusText;
        this.error = message || 'Could not connect to the server. Please try again later.';
        this.showFailureOverlay = true;
        this.cdr.detectChanges();
      }
    });
  }
}
