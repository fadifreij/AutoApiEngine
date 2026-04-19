import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';
import { Router, RouterLink } from '@angular/router';
import { ButtonModule } from 'primeng/button';

@Component({
  selector: 'app-landing',
  standalone: true,
  imports: [RouterLink, ButtonModule],
  templateUrl: './landing.component.html',
  styleUrl: './landing.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LandingComponent {
  private readonly sanitizer = inject(DomSanitizer);

  protected readonly plans = [
    {
      name: 'Identity Server',
      description: 'Provides a dedicated identity management solution for your application.',
      features: [
        'Identity server integration (Okta, custom)',
        'Authentication management',
        'Secure user authentication and authorization',
      ],
      price: 'Contact us',
      highlighted: false,
    },
    {
      name: 'API Without Hosting Database',
      description: 'Provides API functionality without hosting your database.',
      features: [
        'Custom API with CRUD operations',
        'API key management',
        'Provider support (Okta, IdentityServer)',
      ],
      price: 'Contact us',
      highlighted: false,
    },
    {
      name: 'API With Identity Server',
      description: 'Adds Identity Server functionality to Plan 2.',
      features: [
        'All features of Plan 2',
        'Integrated identity management with Okta or IdentityServer',
      ],
      price: 'Contact us',
      highlighted: true,
    },
    {
      name: 'Full Hosting (API + Database)',
      description: 'Complete solution, including database hosting, full API functionality, and security.',
      features: [
        'Database hosting with backup options',
        'API with full CRUD operations',
        'Full security setup with identity provider choice',
      ],
      price: 'Contact us',
      highlighted: false,
    },
  ];

  protected readonly features = [
    {
      title: 'API Generator',
      description: 'Automatically generate API endpoints with CRUD operations, or customize your own.',
      cta: 'Generate your custom API now',
      icon: this.trustSvg(`<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" width="28" height="28"><path d="M13 10V3L4 14h7v7l9-11h-7z"/></svg>`),
    },
    {
      title: 'Database Management',
      description: 'Upload, download, and backup databases with ease.',
      cta: 'Manage your database easily',
      icon: this.trustSvg(`<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" width="28" height="28"><ellipse cx="12" cy="5" rx="9" ry="3"/><path d="M21 12c0 1.66-4 3-9 3s-9-1.34-9-3"/><path d="M3 5v14c0 1.66 4 3 9 3s9-1.34 9-3V5"/></svg>`),
    },
    {
      title: 'Security',
      description: "Secure your API using your own identity provider, or select from options like Okta or our platform's built-in security services.",
      cta: 'Secure your API today',
      icon: this.trustSvg(`<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" width="28" height="28"><path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z"/></svg>`),
    },
    {
      title: 'Database Explorer',
      description: 'Explore and query your databases directly within the platform.',
      cta: 'Explore your database seamlessly',
      icon: this.trustSvg(`<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" width="28" height="28"><path d="M9 3H5a2 2 0 00-2 2v4m6-6h10a2 2 0 012 2v4M9 3v10m0 0h10M9 13v4m10-4v4M9 21H5a2 2 0 01-2-2v-4m0 6h18"/></svg>`),
    },
  ];

  constructor(private router: Router) {}

  private trustSvg(svg: string): SafeHtml {
    return this.sanitizer.bypassSecurityTrustHtml(svg);
  }

  navigateToSignup(): void {
    this.router.navigate(['/auth/signup']);
  }
}
