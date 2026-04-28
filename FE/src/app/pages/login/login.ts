import { Component, inject } from '@angular/core';
import { AuthService } from '../../shared/auth/auth.service';

@Component({
  selector: 'app-login',
  standalone: true,
  template: ''
})
export class Login {
  constructor() {
    inject(AuthService).login();
  }
}
