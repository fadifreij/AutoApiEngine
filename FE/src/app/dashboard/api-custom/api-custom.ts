import { Component, signal } from '@angular/core';

@Component({
  selector: 'app-api-custom',
  standalone: true,
  templateUrl: './api-custom.html',
  styleUrl: './api-custom.scss'
})
export class ApiCustom {
  showModal = signal(false);
}
