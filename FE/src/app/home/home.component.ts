import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { CommonModule } from '@angular/common';
import { WorkspaceStore } from '../workspace/store/workspace.store';
import { ThemeService } from '../core/theme/theme.service';
import { url_workspace } from '../auth/constants';

@Component({
  selector: 'app-home',
  standalone: true,
  imports: [RouterLink, CommonModule],
  templateUrl: './home.component.html',
  styleUrl: './home.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class HomeComponent {
  protected readonly store        = inject(WorkspaceStore);
  protected readonly themeService = inject(ThemeService);
  private   readonly router       = inject(Router);

  protected readonly activeWorkspaceCount = computed(
    () => this.store.workspaces().filter((w) => w.isActive).length
  );

  protected readonly totalEndpoints = computed(() =>
    this.store.workspaces().reduce((acc, ws) => {
      const tableCount =
        ws.children.find((c: any) => c.label === 'Tables')?.children?.length ?? 0;
      return acc + tableCount * 4;
    }, 0)
  );

  readonly steps = [
    { num: '01', title: 'Create a workspace',      desc: 'Connect SQL Server, PostgreSQL, or MySQL. Your data stays in your infrastructure.' },
    { num: '02', title: 'Run SQL & manage tables', desc: 'Use the built-in SQL runner to create tables, insert data, and run queries.' },
    { num: '03', title: 'Generate your API',       desc: 'Auto-generate full CRUD endpoints per table. Add custom routes as needed.' },
    { num: '04', title: 'Secure & ship',           desc: 'Apply auth rules, rate limits, and IP restrictions. Your API is production-ready.' },
  ];

  readonly apiFeatures = [
    { strong: 'Auto CRUD generation', rest: ' — GET, POST, PUT, DELETE endpoints created per table with zero configuration.' },
    { strong: 'Custom routes',        rest: ' — Add your own endpoint logic on top of the auto-generated base when you need it.' },
    { strong: 'Live API status',      rest: ' — Monitor which APIs are active, degraded, or offline from one dashboard.' },
    { strong: 'Auto documentation',   rest: ' — Every endpoint is self-documenting. Coming soon: OpenAPI export.' },
    { strong: 'Instant generation',   rest: ' — APIs appear the moment you create a table. No configuration needed.' },
  ];

  readonly sampleEndpoints = [
    { method: 'GET',    path: [{ text: '/api/orders', param: false }],                              dashed: false },
    { method: 'GET',    path: [{ text: '/api/orders/', param: false }, { text: '{id}', param: true }], dashed: false },
    { method: 'POST',   path: [{ text: '/api/orders', param: false }],                              dashed: false },
    { method: 'PUT',    path: [{ text: '/api/orders/', param: false }, { text: '{id}', param: true }], dashed: false },
    { method: 'DELETE', path: [{ text: '/api/orders/', param: false }, { text: '{id}', param: true }], dashed: false },
    { method: 'POST',   path: [{ text: '/api/orders/bulk-import', param: false }],                  dashed: true  },
  ];

  readonly securityCards = [
    { title: 'API key auth',      desc: 'Issue scoped API keys per workspace or per endpoint. Revoke anytime without touching your code.' },
    { title: 'Rate limiting',     desc: 'Set requests-per-minute limits globally or per key to protect your backend from abuse.' },
    { title: 'IP allowlist',      desc: 'Restrict API access to trusted IP ranges. Block everything else by default.' },
    { title: 'Audit logs',        desc: 'Every API call is logged with timestamp, key identity, and response code. Full traceability.' },
    { title: 'Threat monitoring', desc: 'Automatic detection of unusual request patterns. Get alerted before an attack causes damage.' },
    { title: 'Live status',       desc: 'Real-time health dashboard for all protected endpoints. Zero threats flagged = green.' },
  ];

  navigateToCreate(): void {
    this.router.navigate([url_workspace]);
  }

  methodClass(method: string): string {
    return `method-${method.toLowerCase()}`;
  }

  formatSize(bytes?: number): string {
    if (!bytes) return '—';
    const mb = bytes / (1024 * 1024);
    return mb >= 1024 ? `${(mb / 1024).toFixed(1)} GB` : `${mb.toFixed(0)} MB`;
  }

  formatDate(date?: Date): string {
    if (!date) return 'Never';
    return date.toLocaleDateString('en-US', { year: 'numeric', month: 'short', day: 'numeric' });
  }

  tableCount(ws: any): number {
    return ws.children.find((c: any) => c.label === 'Tables')?.children?.length ?? 0;
  }
}
