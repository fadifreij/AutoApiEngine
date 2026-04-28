import { CommonModule } from '@angular/common';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { ChangeDetectorRef, Component, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { environment } from '../../../environments/environment';
import { AuthService } from '../../shared/auth/auth.service';
import { LoadingService } from '../../shared/loading/loading.service';

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
  private loadingService = inject(LoadingService);
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
  success = false;

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

    this.loadingService.show();
    this.error = '';

    const headers = new HttpHeaders({
      'Content-Type': 'application/json',
      'Accept': 'application/json'
    });

    this.http.post<any>(`${environment.apiUrl}/register`, {
      Email: this.formData.email,
      Password: this.formData.password,
      OrganizationName: this.formData.organizationName
    }, { headers }).subscribe({
      next: (response) => {
        if (response.success) {
          this.loadingService.hide();
          this.success = true;
          this.cdr.detectChanges();
          setTimeout(() => this.router.navigate(['/']), 2500);
        } else {
          Promise.resolve().then(() => {
            this.error = response.error || 'Registration failed';
            this.loadingService.hide();
            this.cdr.detectChanges();
          });
        }
      },
      error: (err) => {
        Promise.resolve().then(() => {
          const body = err.error;
          this.error = (typeof body === 'string' ? body : body?.error || body?.message) || 'Registration failed';
          this.loadingService.hide();
          this.cdr.detectChanges();
        });
      }
    });
  }
}
