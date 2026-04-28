import { Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AuthService } from '../../shared/auth/auth.service';

@Component({
  selector: 'app-docs',
  standalone: true,
  imports: [RouterLink],
  templateUrl: './docs.html',
  styleUrl: './docs.scss'
})
export class Docs {
  private authService = inject(AuthService);

  onLogin(): void {
    this.authService.login();
  }
}
