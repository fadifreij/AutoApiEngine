import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { CardModule } from 'primeng/card';
import { WorkspaceStore } from '../workspace/store/workspace.store';

@Component({
  selector: 'app-home',
  imports: [RouterLink, ButtonModule, CardModule],
  templateUrl: './home.component.html',
  styleUrl: './home.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class HomeComponent {
  protected readonly store = inject(WorkspaceStore);
  private readonly router = inject(Router);

  protected readonly activeWorkspaceCount = computed(
    () => this.store.workspaces().filter((w) => w.isActive).length
  );

  protected readonly totalEndpoints = computed(() =>
    this.store.workspaces().reduce((acc, ws) => {
      const tableCount = ws.children
        .find((c) => c.label === 'Tables')
        ?.children?.length ?? 0;
      return acc + tableCount * 4; // 4 endpoints per table (GET/POST/PUT/DELETE)
    }, 0)
  );

  navigateToCreate(): void {
    this.router.navigate(['/workspaces/create']);
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
}