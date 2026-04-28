import { Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AuthService } from '../../shared/auth/auth.service';

@Component({
  selector: 'app-pricing',
  standalone: true,
  imports: [RouterLink],
  templateUrl: './pricing.html',
  styleUrl: './pricing.scss'
})
export class Pricing {
  private authService = inject(AuthService);
  yearly = signal(false);

  faqItems = [
    { q: 'What happens after the free trial?', a: 'After 14 days, you can upgrade to Pro or Enterprise. Your data stays intact. If you don\'t upgrade, your account becomes read-only until you choose a plan.', open: true },
    { q: 'Can I connect my own database?', a: 'Yes! Pro and Enterprise plans support external databases. Connect any PostgreSQL, MySQL, or SQL Server by providing credentials.', open: false },
    { q: 'Can I change plans later?', a: 'Absolutely. You can upgrade or downgrade at any time. Changes take effect immediately and billing is prorated.', open: false },
    { q: 'Do you offer a money-back guarantee?', a: 'Yes, we offer a 30-day money-back guarantee on all paid plans. No questions asked.', open: false },
  ];

  toggleFaq(item: typeof this.faqItems[0]) {
    item.open = !item.open;
  }

  onLogin(): void {
    this.authService.login();
  }
}
