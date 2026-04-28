import { Component, inject } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../../shared/auth/auth.service';

@Component({
  selector: 'app-landing',
  standalone: true,
  imports: [RouterLink],
  templateUrl: './landing.html',
  styleUrl: './landing.scss'
})
export class Landing {
  private router = inject(Router);
  private authService = inject(AuthService);

  onLogin(): void {
    this.authService.login();
  }

  features = [
    {
      iconClass: 'purple',
      iconPath: 'M21 12c0 1.66-4.03 3-9 3s-9-1.34-9-3||M3 5v14c0 1.66 4.03 3 9 3s9-1.34 9-3V5',
      iconEllipse: true,
      title: 'Database Workspaces',
      desc: 'Each workspace is backed by its own database — hosted by us or connect your own.',
      items: ['Hosted PostgreSQL included', 'External DB support (Pg, MySQL, MSSQL)', 'Isolated environments per project']
    },
    {
      iconClass: 'blue',
      iconPath: 'M14 2H6a2 2 0 00-2 2v16a2 2 0 002 2h12a2 2 0 002-2V8z||M14 2v6h6||M8 13h8||M8 17h8',
      title: 'SQL Query Studio',
      desc: 'A built-in dark-themed IDE with schema browser, templates, and result preview.',
      items: ['Object explorer sidebar', 'SQL template library', 'Inline results & export']
    },
    {
      iconClass: 'green',
      iconPath: 'M13 2L3 14h9l-1 8 10-12h-9l1-8z',
      title: 'Auto CRUD APIs',
      desc: 'Instantly generate RESTful endpoints for every table. Toggle on/off per route.',
      items: ['GET, POST, PUT, DELETE per table', 'Enable/disable individual routes', 'Swagger-ready documentation']
    },
    {
      iconClass: 'orange',
      iconPath: 'M12 20h9||M16.5 3.5a2.12 2.12 0 013 3L7 19l-4 1 1-4L16.5 3.5z',
      title: 'Custom Endpoints',
      desc: 'Write your own SQL-backed API routes for complex business logic.',
      items: ['Custom SQL per endpoint', 'Parameter binding', 'Full HTTP method support']
    },
    {
      iconClass: 'pink',
      iconPath: 'M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z',
      title: 'API Protection',
      desc: 'Protect every endpoint with API keys, scopes, and rate limiting out of the box.',
      items: ['Auto-generated API keys', 'Scope-based permissions', 'Rate limiting & analytics']
    },
    {
      iconClass: 'cyan',
      iconPath: 'M17 21v-2a4 4 0 00-4-4H5a4 4 0 00-4 4v2||M23 21v-2a4 4 0 00-3-3.87||M16 3.13a4 4 0 010 7.75',
      iconCircle: true,
      title: 'Identity Provider',
      desc: 'Built-in user management with login/register flows, or integrate your own IdP.',
      items: ['Managed login & register', 'External IdP (OIDC / SAML)', 'User roles & permissions']
    }
  ];
}