import { Component, OnInit, signal, computed, inject, PLATFORM_ID, Inject, WritableSignal, viewChild, HostListener } from '@angular/core';
import { isPlatformBrowser } from '@angular/common';
import { RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { Subscription } from 'rxjs';
import { SchemaService, SchemaExplorerResponse } from './schema.service';
import { WorkspaceStateService } from '../../shared/workspace-state.service';
import { QueryService, QueryExecutionResponse } from './query.service';
import { DdlFileService, DdlFileTreeNode } from './ddl-file.service';
import { MonacoEditorComponent } from '../../shared/monaco-editor/monaco-editor';

const EXPLORER_WIDTH_KEY = 'queryStudio_explorerWidth';
const RESULTS_HEIGHT_KEY = 'queryStudio_resultsHeight';

/** Represents a single query tab in the studio. */
export interface QueryTab {
  id: number;
  name: string;
  sql: string;
  result: QueryExecutionResponse | null;
  error: string | null;
  isRunning: boolean;
}

@Component({
  selector: 'app-query-studio',
  standalone: true,
  imports: [RouterLink, FormsModule, MonacoEditorComponent],
  templateUrl: './query-studio.html',
  styleUrl: './query-studio.scss'
})
export class QueryStudio implements OnInit {
  private schemaService = inject(SchemaService);
  private workspaceState = inject(WorkspaceStateService);
  private queryService = inject(QueryService);
  private ddlFileService = inject(DdlFileService);

  /** Default SQL template shown on first load */
  defaultSql = '-- Type your SQL query here …';

  // Reference to the Monaco editor component
  monacoEditor = viewChild<MonacoEditorComponent>('monacoEditor');

  // ── Tab state ──
  tabs = signal<QueryTab[]>([]);
  activeTabId = signal<number | null>(null);
  private tabIdCounter = 0;

  /** Computed reference to the currently active tab. */
  activeTab = computed(() => this.tabs().find(t => t.id === this.activeTabId()) ?? null);

  /** Tracks which tab is currently being renamed (null = not editing). */
  editingTabId = signal<number | null>(null);

  // ── Saved DDL files tree ──
  ddlTree = signal<DdlFileTreeNode | null>(null);
  ddlTreeLoading = signal(false);
  ddlTreeError = signal<string | null>(null);
  expandedFolders = signal<Set<string>>(new Set());

  // ── Resizable results panel ──
  resultsHeight: WritableSignal<number>;
  private resultsResizeStartY = 0;
  private resultsResizeStartHeight = 0;

  // ── Query execution state ──
  isRunning = signal(false);
  queryResult = signal<QueryExecutionResponse | null>(null);
  queryError = signal<string | null>(null);
  private querySubscription: Subscription | null = null;
  private runningQueryTabId: number | null = null;

  // ── Schema explorer state ──
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

    const savedResultsHeight = this.isBrowser
      ? parseInt(localStorage.getItem(RESULTS_HEIGHT_KEY) ?? '', 10)
      : NaN;
    this.resultsHeight = signal(!isNaN(savedResultsHeight) && savedResultsHeight >= 80 ? savedResultsHeight : 200);
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

  // ── Resizable results panel ──

  startResultsResize(event: MouseEvent): void {
    event.preventDefault();
    this.resultsResizeStartY = event.clientY;
    this.resultsResizeStartHeight = this.resultsHeight();
    document.addEventListener('mousemove', this.onResultsResize);
    document.addEventListener('mouseup', this.stopResultsResize);
    document.body.style.cursor = 'row-resize';
    document.body.style.userSelect = 'none';
  }

  private onResultsResize = (event: MouseEvent): void => {
    // Calculate height relative to the editor-area bottom
    const editorArea = (event.target as HTMLElement).closest('.editor-area');
    if (!editorArea) return;

    const editorAreaRect = editorArea.getBoundingClientRect();
    const editorAreaBottom = editorAreaRect.bottom;
    const distanceFromBottom = editorAreaBottom - event.clientY;
    const newHeight = Math.max(80, Math.min(500, distanceFromBottom));

    this.resultsHeight.set(newHeight);
  };

  private stopResultsResize = (): void => {
    document.removeEventListener('mousemove', this.onResultsResize);
    document.removeEventListener('mouseup', this.stopResultsResize);
    document.body.style.cursor = '';
    document.body.style.userSelect = '';
    if (this.isBrowser) {
      localStorage.setItem(RESULTS_HEIGHT_KEY, String(this.resultsHeight()));
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
    this.addTab();
    this.loadSchema();
    this.refreshDdlTree();
  }

  // ── Tab Management ──

  /** Creates a new query tab and switches to it. */
  addTab(): void {
    const id = ++this.tabIdCounter;
    const tab: QueryTab = {
      id,
      name: `Query ${id}`,
      sql: this.defaultSql,
      result: null,
      error: null,
      isRunning: false,
    };
    this.tabs.update(tabs => [...tabs, tab]);
    this.switchTab(id);
  }

  /** Switches to the tab with the given id, saving & restoring editor + results. */
  switchTab(id: number): void {
    const editor = this.monacoEditor();
    const currentId = this.activeTabId();

    // ── Save current state to the tab we're leaving ──
    if (currentId != null) {
      const currentSql = editor ? editor.getValue() : '';
      this.tabs.update(tabs =>
        tabs.map(t =>
          t.id === currentId
            ? {
                ...t,
                sql: currentSql,
                result: this.queryResult(),
                error: this.queryError(),
                isRunning: this.isRunning(),
              }
            : t,
        ),
      );
    }

    this.activeTabId.set(id);

    // ── Restore state from the tab we're switching to ──
    const tab = this.tabs().find(t => t.id === id);
    if (tab) {
      editor?.setValue(tab.sql);
      this.queryResult.set(tab.result);
      this.queryError.set(tab.error);
      this.isRunning.set(tab.isRunning);
    }
  }

  /** Closes the given tab. At least one tab is always kept open. */
  closeTab(id: number): void {
    const currentTabs = this.tabs();
    if (currentTabs.length <= 1) return;

    // Clear editing state if closing the tab being renamed
    if (this.editingTabId() === id) {
      this.editingTabId.set(null);
    }

    const idx = currentTabs.findIndex(t => t.id === id);
    this.tabs.update(tabs => tabs.filter(t => t.id !== id));

    // If we closed the active tab, switch to the nearest remaining tab
    if (this.activeTabId() === id) {
      const remaining = this.tabs();
      const newIdx = Math.min(idx, remaining.length - 1);
      this.switchTab(remaining[newIdx].id);
    }
  }

  // ── Tab Renaming ──

  /** Double-click on a tab name starts inline editing. */
  startEditName(tabId: number, event: MouseEvent): void {
    event.stopPropagation();
    this.editingTabId.set(tabId);
    // Focus and select the input after Angular renders it
    setTimeout(() => {
      const input = document.querySelector('.tab-name-input') as HTMLInputElement;
      if (input) {
        input.focus();
        input.select();
      }
    });
  }

  /** Saves the new name when editing is confirmed (Enter / blur). */
  finishEditName(tabId: number, input: HTMLInputElement): void {
    const raw = input.value.trim();
    if (raw.length > 0) {
      this.tabs.update(tabs =>
        tabs.map(t => (t.id === tabId ? { ...t, name: raw } : t)),
      );
    }
    this.editingTabId.set(null);
  }

  /** Cancels renaming (Escape) without saving. */
  cancelEditName(): void {
    this.editingTabId.set(null);
  }

  // ── Saved DDL File Management ──

  /** Refreshes the saved-DDL file tree from the backend. */
  refreshDdlTree(): void {
    const workspaceId = this.workspaceState.selectedWorkspaceId();
    if (!workspaceId) return;

    this.ddlTreeLoading.set(true);
    this.ddlTreeError.set(null);

    this.ddlFileService.getTree(workspaceId).subscribe({
      next: (tree) => {
        this.ddlTree.set(tree);
        this.ddlTreeLoading.set(false);
      },
      error: (err) => {
        this.ddlTreeError.set(err.error?.message || err.message || 'Failed to load saved files.');
        this.ddlTreeLoading.set(false);
      },
    });
  }

  /** Prompts the user for folder name and saves the current editor content. */
  saveCurrentSql(): void {
    const tab = this.activeTab();
    if (!tab) return;

    const workspaceId = this.workspaceState.selectedWorkspaceId();
    if (!workspaceId) {
      this.queryError.set('No workspace selected.');
      return;
    }

    const sql = this.monacoEditor()?.getValue()?.trim();
    if (!sql || sql === this.defaultSql) {
      this.queryError.set('Please enter a SQL query before saving.');
      return;
    }

    // Prompt for folder name
    const folderName = window.prompt('Enter folder name to save in:', 'My Queries');
    if (!folderName || !folderName.trim()) return;

    // Prompt for file name (default: tab name + .sql)
    const defaultName = tab.name.endsWith('.sql') ? tab.name : `${tab.name}.sql`;
    const fileName = window.prompt('Enter file name:', defaultName);
    if (!fileName || !fileName.trim()) return;

    const finalName = fileName.trim().endsWith('.sql') ? fileName.trim() : `${fileName.trim()}.sql`;

    this.ddlFileService
      .save({
        workspaceId,
        folderName: folderName.trim(),
        fileName: finalName,
        content: sql,
      })
      .subscribe({
        next: () => {
          // Update the active tab's name to the saved file name (strip .sql extension)
          const tabName = finalName.endsWith('.sql') ? finalName.slice(0, -4) : finalName;
          this.tabs.update(tabs =>
            tabs.map(t => (t.id === tab.id ? { ...t, name: tabName } : t)),
          );
          this.queryResult.set(null);
          this.queryError.set('File saved successfully.');
          this.refreshDdlTree();
        },
        error: (err) => {
          this.queryError.set(err.error?.message || err.message || 'Failed to save file.');
        },
      });
  }

  /** Loads a saved DDL file's content into the editor. */
  loadSavedFile(node: DdlFileTreeNode): void {
    if (node.type !== 'File' || !node.path) return;

    this.ddlFileService.readFile(node.path).subscribe({
      next: (response) => {
        this.monacoEditor()?.setValue(response.content);
        // Update the active tab's name to match the loaded file name (strip .sql extension)
        const tabName = node.name.endsWith('.sql') ? node.name.slice(0, -4) : node.name;
        const currentTabId = this.activeTabId();
        if (currentTabId != null) {
          this.tabs.update(tabs =>
            tabs.map(t => (t.id === currentTabId ? { ...t, name: tabName } : t)),
          );
        }
      },
      error: (err) => {
        this.queryError.set(err.error?.message || err.message || 'Failed to load file.');
      },
    });
  }

  /** Tracks which tree node (by its Path) is currently being renamed. */
  editingNodePath = signal<string | null>(null);

  /** Starts inline rename on double-click of a tree node (folder or file). */
  startEditNode(node: DdlFileTreeNode, event: MouseEvent): void {
    event.stopPropagation();
    if (!node.path) return;
    this.editingNodePath.set(node.path);
    setTimeout(() => {
      const input = document.querySelector('.tree-rename-input') as HTMLInputElement;
      if (input) {
        input.focus();
        input.select();
      }
    });
  }

  /** Saves the rename when confirmed (Enter / blur). */
  finishEditNode(node: DdlFileTreeNode, input: HTMLInputElement): void {
    const raw = input.value.trim();
    if (raw.length > 0 && node.path && raw !== node.name) {
      this.ddlFileService.rename({ currentPath: node.path, newName: raw }).subscribe({
        next: () => this.refreshDdlTree(),
        error: (err) => {
          this.ddlTreeError.set(err.error?.message || err.message || 'Failed to rename item.');
        },
      });
    }
    this.editingNodePath.set(null);
  }

  /** Cancels rename on Escape. */
  cancelEditNode(): void {
    this.editingNodePath.set(null);
  }

  // ── Context Menu for Saved DDL tree ──

  /** The node (folder or file) the context menu was opened for. */
  contextMenuNode = signal<DdlFileTreeNode | null>(null);

  /** Pixel position of the context menu (null = hidden). */
  contextMenuPos = signal<{ x: number; y: number } | null>(null);

  /** Opens the context menu on right-click. */
  onContextMenu(node: DdlFileTreeNode, event: MouseEvent): void {
    event.preventDefault();
    event.stopPropagation();
    this.closeContextMenu();
    this.contextMenuNode.set(node);
    this.contextMenuPos.set({ x: event.clientX, y: event.clientY });
  }

  /** Closes the context menu. */
  closeContextMenu(): void {
    this.contextMenuNode.set(null);
    this.contextMenuPos.set(null);
  }

  /** Handles "Rename" from the context menu (reuses the inline rename flow). */
  contextMenuRename(): void {
    const node = this.contextMenuNode();
    if (node?.path) {
      this.editingNodePath.set(node.path);
      // Need to close context menu first so the input can be found
      this.closeContextMenu();
      setTimeout(() => {
        const input = document.querySelector('.tree-rename-input') as HTMLInputElement;
        if (input) {
          input.focus();
          input.select();
        }
      });
    } else {
      this.closeContextMenu();
    }
  }

  /** Handles "Delete" from the context menu — prompts for confirmation. */
  contextMenuDelete(): void {
    const node = this.contextMenuNode();
    this.closeContextMenu();
    if (!node?.path) return;

    const type = node.type === 'Folder' ? 'folder' : 'file';
    const confirmed = window.confirm(
      `Are you sure you want to delete this ${type} "${node.name}"?`,
    );
    if (!confirmed) return;

    this.ddlFileService.delete({ filePath: node.path }).subscribe({
      next: () => {
        this.queryError.set(null);
        this.refreshDdlTree();
      },
      error: (err) => {
        this.ddlTreeError.set(err.error?.message || err.message || 'Failed to delete item.');
      },
    });
  }

  /** Closes context menu on any click outside. */
  @HostListener('document:click')
  onDocumentClick(): void {
    this.closeContextMenu();
  }

  /** Toggles expansion of a folder node in the tree. */
  toggleFolderExpanded(folder: DdlFileTreeNode): void {
    this.expandedFolders.update(set => {
      const next = new Set(set);
      if (next.has(folder.name)) {
        next.delete(folder.name);
      } else {
        next.add(folder.name);
      }
      return next;
    });
  }

  /** Derives a unique key for a tree node (its full path). */
  nodeKey(node: DdlFileTreeNode, parentPath?: string): string {
    return parentPath ? `${parentPath}/${node.name}` : node.name;
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

  // ── Query Execution ──

  /** Fires when the Monaco editor content changes — saves to the active tab. */
  onSqlChange(sql: string): void {
    const id = this.activeTabId();
    if (id != null) {
      this.tabs.update(tabs =>
        tabs.map(t => (t.id === id ? { ...t, sql } : t)),
      );
    }
  }

  /** Load a SQL template into the editor */
  loadTemplate(sql: string): void {
    this.monacoEditor()?.setValue(sql);
  }

  /** Executes the current SQL query */
  run(): void {
    // Block if another query is already running (in any tab)
    if (this.runningQueryTabId != null) {
      this.queryError.set('Another query is already running. Please wait or stop it.');
      return;
    }

    const workspaceId = this.workspaceState.selectedWorkspaceId();
    if (!workspaceId) {
      this.queryError.set('No workspace selected.');
      return;
    }

    const sql = this.monacoEditor()?.getValue()?.trim();
    if (!sql) {
      this.queryError.set('Please enter a SQL query.');
      return;
    }

    const tabId = this.activeTabId();
    if (tabId == null) return;

    // Mark this tab as running in the tab model
    this.tabs.update(tabs =>
      tabs.map(t =>
        t.id === tabId ? { ...t, result: null, error: null, isRunning: true } : t,
      ),
    );

    // Sync display signals
    this.queryResult.set(null);
    this.queryError.set(null);
    this.isRunning.set(true);
    this.runningQueryTabId = tabId;

    this.querySubscription = this.queryService.execute({ workspaceId, sql }).subscribe({
      next: (result) => {
        this.querySubscription = null;
        const rtId = this.runningQueryTabId;
        this.runningQueryTabId = null;

        // Store the result in the tab that initiated the query
        this.tabs.update(tabs =>
          tabs.map(t =>
            t.id === rtId ? { ...t, result, error: null, isRunning: false } : t,
          ),
        );

        // Only update display signals if this tab is still the active one
        if (this.activeTabId() === rtId) {
          this.queryResult.set(result);
          this.isRunning.set(false);
        }
      },
      error: (err) => {
        this.querySubscription = null;
        const rtId = this.runningQueryTabId;
        this.runningQueryTabId = null;

        const errMsg = err.error?.error || err.error?.message || err.message || 'Query execution failed.';

        // Store the error in the tab that initiated the query
        this.tabs.update(tabs =>
          tabs.map(t =>
            t.id === rtId ? { ...t, result: null, error: errMsg, isRunning: false } : t,
          ),
        );

        // Only update display signals if this tab is still the active one
        if (this.activeTabId() === rtId) {
          this.queryError.set(errMsg);
          this.isRunning.set(false);
        }
      },
    });
  }

  /** Cancels the running query */
  stop(): void {
    if (this.querySubscription) {
      this.querySubscription.unsubscribe();
      this.querySubscription = null;
    }

    // Clear the running state on the tab that was executing
    if (this.runningQueryTabId != null) {
      this.tabs.update(tabs =>
        tabs.map(t =>
          t.id === this.runningQueryTabId ? { ...t, isRunning: false } : t,
        ),
      );
      this.runningQueryTabId = null;
    }

    this.isRunning.set(false);
    this.queryError.set('Query execution was cancelled.');
  }

  /** Formats the timestamp of the last execution */
  formattedTimestamp(): string {
    return new Date().toLocaleString('en-US', {
      month: 'short',
      day: 'numeric',
      year: 'numeric',
      hour: 'numeric',
      minute: '2-digit',
      hour12: true
    });
  }

  /** Helper to show duration with unit */
  formatDuration(ms: number): string {
    if (ms < 1000) return `${ms}ms`;
    return `${(ms / 1000).toFixed(2)}s`;
  }
}