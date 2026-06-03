import { Component, OnInit, signal, computed, inject, PLATFORM_ID, Inject, WritableSignal } from '@angular/core';
import { isPlatformBrowser } from '@angular/common';
import { RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { SchemaService, SchemaExplorerResponse } from './schema.service';
import { WorkspaceStateService } from '../../shared/workspace-state.service';

const EXPLORER_WIDTH_KEY = 'queryStudio_explorerWidth';

@Component({
  selector: 'app-query-studio',
  standalone: true,
  imports: [RouterLink, FormsModule],
  templateUrl: './query-studio.html',
  styleUrl: './query-studio.scss'
})
export class QueryStudio implements OnInit {
  private schemaService = inject(SchemaService);
  private workspaceState = inject(WorkspaceStateService);

  isRunning = signal(false);
  loading = signal(false);
  error = signal<string | null>(null);
  searchQuery = signal('');

  // Schema data
  schemaData = signal<SchemaExplorerResponse | null>(null);

  // ── Resizable explorer ──
  explorerWidth: WritableSignal<number>;
  private resizeStartX = 0;
  private resizeStartWidth = 0;
  private isBrowser: boolean;

  constructor(@Inject(PLATFORM_ID) platformId: Object) {
    this.isBrowser = isPlatformBrowser(platformId);
    const saved = this.isBrowser
      ? parseInt(localStorage.getItem(EXPLORER_WIDTH_KEY) ?? '', 10)
      : NaN;
    this.explorerWidth = signal(!isNaN(saved) && saved >= 200 ? saved : 340);
  }

  startResize(event: MouseEvent): void {
    event.preventDefault();
    this.resizeStartX = event.clientX;
    this.resizeStartWidth = this.explorerWidth();
    document.addEventListener('mousemove', this.onResize);
    document.addEventListener('mouseup', this.stopResize);
    document.body.style.cursor = 'col-resize';
    document.body.style.userSelect = 'none';
  }

  private onResize = (event: MouseEvent): void => {
    const delta = event.clientX - this.resizeStartX;
    const newWidth = Math.max(200, Math.min(600, this.resizeStartWidth + delta));
    this.explorerWidth.set(newWidth);
  };

  private stopResize = (): void => {
    document.removeEventListener('mousemove', this.onResize);
    document.removeEventListener('mouseup', this.stopResize);
    document.body.style.cursor = '';
    document.body.style.userSelect = '';
    if (this.isBrowser) {
      localStorage.setItem(EXPLORER_WIDTH_KEY, String(this.explorerWidth()));
    }
  };

  // Computed views
  tables = computed(() =>
    (this.schemaData()?.objects ?? []).filter(o => o.type === 'Table')
  );
  views = computed(() =>
    (this.schemaData()?.objects ?? []).filter(o => o.type === 'View')
  );
  functions = computed(() =>
    (this.schemaData()?.objects ?? []).filter(o => o.type === 'Function')
  );
  storedProcedures = computed(() =>
    (this.schemaData()?.objects ?? []).filter(o => o.type === 'StoredProcedure')
  );

  // ── Independent collapse/expand per section ──
  tablesExpanded = signal(false);
  viewsExpanded = signal(false);
  storedProceduresExpanded = signal(false);
  functionsExpanded = signal(false);

  toggleSection(section: 'tables' | 'views' | 'sp' | 'functions'): void {
    switch (section) {
      case 'tables':    this.tablesExpanded.update(v => !v); break;
      case 'views':     this.viewsExpanded.update(v => !v); break;
      case 'sp':        this.storedProceduresExpanded.update(v => !v); break;
      case 'functions': this.functionsExpanded.update(v => !v); break;
    }
  }

  // Formatted size
  databaseSizeFormatted = computed(() => {
    const bytes = this.schemaData()?.databaseSizeBytes ?? 0;
    if (bytes === 0) return '0 B';
    const units = ['B', 'KB', 'MB', 'GB', 'TB'];
    const i = Math.floor(Math.log(bytes) / Math.log(1024));
    return (bytes / Math.pow(1024, i)).toFixed(i === 0 ? 0 : 1) + ' ' + units[i];
  });

  ngOnInit(): void {
    this.loadSchema();
  }

  onSearchChange(value: string): void {
    this.searchQuery.set(value);
    this.loadSchema(value || undefined);
  }

  private loadSchema(search?: string): void {
    const workspaceId = this.workspaceState.selectedWorkspaceId();
    if (!workspaceId) {
      this.error.set('No workspace selected. Please select a workspace first.');
      return;
    }

    this.loading.set(true);
    this.error.set(null);

    this.schemaService.explore(workspaceId, search).subscribe({
      next: (data) => {
        this.schemaData.set(data);
        this.loading.set(false);
      },
      error: (err) => {
        // Try to extract a meaningful message
        let msg = 'Failed to load schema.';
        if (err.status === 404) {
          msg = err.error?.message
            ? err.error.message
            : 'Schema endpoint not found. Please ensure the backend is running the latest code (restart the server).';
        } else if (err.status === 401 || err.status === 403) {
          msg = 'Authentication required. Please log in again.';
        } else {
          msg = err.error?.message || err.message || msg;
        }
        this.error.set(msg);
        this.loading.set(false);
      }
    });
  }

  // ── Timer helpers (unchanged) ──
  private runTimer: ReturnType<typeof setTimeout> | null = null;

  run(): void {
    this.isRunning.set(true);
    this.runTimer = setTimeout(() => {
      this.isRunning.set(false);
      this.runTimer = null;
    }, 2000);
  }

  stop(): void {
    if (this.runTimer) {
      clearTimeout(this.runTimer);
      this.runTimer = null;
    }
    this.isRunning.set(false);
  }
}
