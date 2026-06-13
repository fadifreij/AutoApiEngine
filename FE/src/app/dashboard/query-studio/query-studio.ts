import { isPlatformBrowser } from '@angular/common';
import { Component, computed, HostListener, inject, Inject, OnInit, PLATFORM_ID, signal, viewChild, WritableSignal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Subscription } from 'rxjs';
import { AuthService } from '../../shared/auth/auth.service';
import { MonacoEditorComponent } from '../../shared/monaco-editor/monaco-editor';
import { WorkspaceStateService } from '../../shared/workspace-state.service';
import { AiChatMessage, AiService } from './ai.service';
import { DdlFileService, DdlFileTreeNode } from './ddl-file.service';
import { QueryExecutionResponse, QueryService } from './query.service';
import { SchemaExplorerResponse, SchemaObject, SchemaService } from './schema.service';

/** A part of an assistant message — either plain text or a fenced code block. */
export interface AiMessagePart {
  type: 'text' | 'code';
  content: string;
}

/** A chat message shown in the AI panel. */
export interface AiPanelMessage {
  role: 'user' | 'assistant';
  content: string;
}

const EXPLORER_WIDTH_KEY = 'queryStudio_explorerWidth';
const RESULTS_HEIGHT_KEY = 'queryStudio_resultsHeight';
const RIGHT_PANEL_WIDTH_KEY = 'queryStudio_rightPanelWidth';
const RIGHT_PANEL_COLLAPSED_KEY = 'queryStudio_rightPanelCollapsed';
const RIGHT_PANE_TAB_KEY = 'queryStudio_rightPaneTab';

/** Represents a single query tab in the studio. */
export interface QueryTab {
  id: number;
  name: string;
  sql: string;
  result: QueryExecutionResponse | null;
  error: string | null;
  isRunning: boolean;
  /** Set when the tab's content was loaded from a saved file — used for prompt-less re-save. */
  sourceFolder?: string;
  sourceFileName?: string;
}

@Component({
  selector: 'app-query-studio',
  standalone: true,
  imports: [RouterLink, FormsModule, MonacoEditorComponent],
  templateUrl: './query-studio.html',
  styleUrl: './query-studio.scss'
})
export class QueryStudio implements OnInit {
  authService = inject(AuthService);
  private schemaService = inject(SchemaService);
  private workspaceState = inject(WorkspaceStateService);
  private queryService = inject(QueryService);
  private ddlFileService = inject(DdlFileService);
  private aiService = inject(AiService);

  /** Default SQL template shown on first load */
  defaultSql = '';

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

  // ── Right pane tab ('ddl' = Saved DDL, 'ai' = AI Assistant) ──
  rightPaneTab = signal<'ddl' | 'ai'>('ddl');

  /** Whether the right panel is collapsed to a thin icon rail. */
  rightPanelCollapsed = signal(false);

  /**
   * Selects a right-pane panel. If the panel is collapsed, it expands and shows
   * the chosen tab. If the chosen tab is already active and expanded, it collapses.
   */
  setRightPaneTab(tab: 'ddl' | 'ai'): void {
    if (this.rightPanelCollapsed()) {
      this.rightPanelCollapsed.set(false);
      this.rightPaneTab.set(tab);
      this.persistRightPanelState();
      return;
    }
    if (this.rightPaneTab() === tab) {
      this.rightPanelCollapsed.set(true);
    } else {
      this.rightPaneTab.set(tab);
    }
    this.persistRightPanelState();
  }

  /** Collapses the right panel to the icon rail. */
  collapseRightPanel(): void {
    this.rightPanelCollapsed.set(true);
    this.persistRightPanelState();
  }

  /** Expands the right panel, optionally selecting a specific tab. */
  expandRightPanel(tab?: 'ddl' | 'ai'): void {
    if (tab) this.rightPaneTab.set(tab);
    this.rightPanelCollapsed.set(false);
    this.persistRightPanelState();
  }

  private persistRightPanelState(): void {
    if (this.isBrowser) {
      localStorage.setItem(RIGHT_PANEL_COLLAPSED_KEY, this.rightPanelCollapsed() ? '1' : '0');
      localStorage.setItem(RIGHT_PANE_TAB_KEY, this.rightPaneTab());
    }
  }

  // ── AI Assistant state ──
  aiMessages = signal<AiPanelMessage[]>([]);
  aiInput = signal('');
  aiLoading = signal(false);
  aiError = signal<string | null>(null);
  private aiStreamController: AbortController | null = null;

  // ── Resizable results panel ──
  resultsHeight: WritableSignal<number>;
  private resultsResizeStartY = 0;
  private resultsResizeStartHeight = 0;

  // ── Query execution state ──
  isRunning = signal(false);
  queryResult = signal<QueryExecutionResponse | null>(null);
  queryError = signal<string | null>(null);
  querySuccess = signal<string | null>(null);
  private querySubscription: Subscription | null = null;
  private runningQueryTabId: number | null = null;

  /** True when there's a successful SELECT result with at least one row — enables Export CSV. */
  canExportCsv = computed(() => {
    const r = this.queryResult();
    return !!r && r.success && r.isSelectQuery && r.columns.length > 0 && r.rows.length > 0;
  });

  // ── Save Dialog state ──
  showSaveDialog = signal(false);
  saveDialogFolders = signal<string[]>([]);
  saveDialogFolderFilter = signal('');
  saveDialogSelectedFolder = signal<string>('');
  saveDialogCustomFolder = signal<string>('');
  saveDialogFileName = signal<string>('');
  saveDialogIsNewFolder = signal(false);
  private saveDialogResolve: ((result: { folderName: string; fileName: string } | null) => void) | null = null;

  /** Folders filtered by the search term (case-insensitive). */
  filteredFolders = computed(() => {
    const filter = this.saveDialogFolderFilter().toLowerCase().trim();
    if (!filter) return this.saveDialogFolders();
    return this.saveDialogFolders().filter(f => f.toLowerCase().includes(filter));
  });

  /** Returns whether the save-dialog inputs are valid. */
  isSaveValid(): boolean {
    if (this.saveDialogIsNewFolder()) {
      return !!this.saveDialogCustomFolder().trim() && !!this.saveDialogFileName().trim();
    }
    return !!this.saveDialogSelectedFolder() && !!this.saveDialogFileName().trim();
  }

  private openSaveDialog(): Promise<{ folderName: string; fileName: string } | null> {
    const tab = this.activeTab();
    if (!tab) return Promise.resolve(null);

    // Gather existing folders from the DDL tree
    const folders: string[] = [];
    const tree = this.ddlTree();
    if (tree?.children) {
      for (const org of tree.children) {
        for (const folder of org.children ?? []) {
          if (folder.type === 'Folder') {
            folders.push(folder.name);
          }
        }
      }
    }

    const defaultName = tab.name.endsWith('.sql') ? tab.name : `${tab.name}.sql`;

    this.saveDialogFolders.set(folders);
    this.saveDialogFolderFilter.set('');
    this.saveDialogSelectedFolder.set(folders[0] || '');
    this.saveDialogCustomFolder.set('');
    this.saveDialogFileName.set(defaultName);
    this.saveDialogIsNewFolder.set(false);
    this.showSaveDialog.set(true);

    return new Promise(resolve => {
      this.saveDialogResolve = resolve;
    });
  }

  confirmSaveDialog(): void {
    const folderName = this.saveDialogIsNewFolder()
      ? this.saveDialogCustomFolder().trim()
      : this.saveDialogSelectedFolder();

    const fileName = this.saveDialogFileName().trim();
    if (!folderName || !fileName) return;

    const finalFileName = fileName.endsWith('.sql') ? fileName : `${fileName}.sql`;

    this.showSaveDialog.set(false);
    this.saveDialogResolve?.({ folderName, fileName: finalFileName });
    this.saveDialogResolve = null;
  }

  cancelSaveDialog(): void {
    this.showSaveDialog.set(false);
    this.saveDialogResolve?.(null);
    this.saveDialogResolve = null;
  }

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

  // ── Resizable right panel ──
  rightPanelWidth: WritableSignal<number>;
  private rightResizeStartX = 0;
  private rightResizeStartWidth = 0;
  private layoutEl: HTMLElement | null = null;

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

    const savedRightWidth = this.isBrowser
      ? parseInt(localStorage.getItem(RIGHT_PANEL_WIDTH_KEY) ?? '', 10)
      : NaN;
    const clampedRightWidth = !isNaN(savedRightWidth)
      ? Math.max(240, Math.min(640, savedRightWidth))
      : 320;
    this.rightPanelWidth = signal(clampedRightWidth);

    if (this.isBrowser) {
      this.rightPanelCollapsed.set(localStorage.getItem(RIGHT_PANEL_COLLAPSED_KEY) === '1');
      const savedTab = localStorage.getItem(RIGHT_PANE_TAB_KEY);
      if (savedTab === 'ddl' || savedTab === 'ai') {
        this.rightPaneTab.set(savedTab);
      }
    }
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

  // ── Resizable right panel ──

  startRightResize(event: MouseEvent): void {
    event.preventDefault();
    this.rightResizeStartX = event.clientX;
    this.rightResizeStartWidth = this.rightPanelWidth();
    // Cache the layout container so we can clamp against the available width.
    this.layoutEl = (event.target as HTMLElement).closest('.studio-layout') as HTMLElement | null;
    document.addEventListener('mousemove', this.onRightResize);
    document.addEventListener('mouseup', this.stopRightResize);
    document.body.style.cursor = 'col-resize';
    document.body.style.userSelect = 'none';
  }

  private onRightResize = (event: MouseEvent): void => {
    // Dragging left (negative delta) widens the right panel.
    const delta = event.clientX - this.rightResizeStartX;

    // Upper bound: never let the right panel push the editor below its minimum.
    // Available = total layout width − explorer − resize handles − minimum editor width.
    const RESIZE_HANDLES = 10; // two 5px handles
    const MIN_EDITOR = 320;
    const layoutWidth = this.layoutEl?.clientWidth ?? window.innerWidth;
    const maxByLayout = layoutWidth - this.explorerWidth() - RESIZE_HANDLES - MIN_EDITOR;
    const upperBound = Math.min(640, Math.max(240, maxByLayout));

    const newWidth = Math.max(240, Math.min(upperBound, this.rightResizeStartWidth - delta));
    this.rightPanelWidth.set(newWidth);
  };

  private stopRightResize = (): void => {
    document.removeEventListener('mousemove', this.onRightResize);
    document.removeEventListener('mouseup', this.stopRightResize);
    this.layoutEl = null;
    document.body.style.cursor = '';
    document.body.style.userSelect = '';
    if (this.isBrowser) {
      localStorage.setItem(RIGHT_PANEL_WIDTH_KEY, String(this.rightPanelWidth()));
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
      case 'tables': this.tablesExpanded.update(v => !v); break;
      case 'views': this.viewsExpanded.update(v => !v); break;
      case 'sp': this.storedProceduresExpanded.update(v => !v); break;
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

  /** Saves the current editor content. Shows a modern dialog on first save, re-saves silently. */
  async saveCurrentSql(): Promise<void> {
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

    // ── Determine folder + file name ──
    let folderName: string;
    let fileName: string;

    if (tab.sourceFolder && tab.sourceFileName) {
      // Tab was loaded from a saved file → re-save silently (no dialog)
      folderName = tab.sourceFolder;
      fileName = tab.sourceFileName;
    } else {
      // First-time save → show modern dialog to pick folder and file name
      const result = await this.openSaveDialog();
      if (!result) return;
      folderName = result.folderName;
      fileName = result.fileName;
    }

    this.ddlFileService
      .save({
        workspaceId,
        folderName: folderName.trim(),
        fileName: fileName,
        content: sql,
      })
      .subscribe({
        next: () => {
          // Mark the tab with its source so subsequent saves skip prompts
          this.tabs.update(tabs =>
            tabs.map(t =>
              t.id === tab.id
                ? { ...t, name: fileName, sourceFolder: folderName.trim(), sourceFileName: fileName }
                : t,
            ),
          );
          this.queryResult.set(null);
          this.queryError.set(null);
          this.querySuccess.set('File saved successfully.');
          this.refreshDdlTree();
        },
        error: (err) => {
          this.querySuccess.set(null);
          this.queryError.set(err.error?.message || err.message || 'Failed to save file.');
        },
      });
  }

  /** Loads a saved DDL file's content into the editor. */
  loadSavedFile(folder: DdlFileTreeNode, file: DdlFileTreeNode): void {
    if (file.type !== 'File' || !file.path) return;

    this.ddlFileService.readFile(file.path).subscribe({
      next: (response) => {
        this.monacoEditor()?.setValue(response.content);
        // Update the active tab's name and store source info for prompt-less re-save
        const currentTabId = this.activeTabId();
        if (currentTabId != null) {
          this.tabs.update(tabs =>
            tabs.map(t =>
              t.id === currentTabId
                ? { ...t, name: file.name, sourceFolder: folder.name, sourceFileName: file.name }
                : t,
            ),
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

  // ── Context Menu for Schema Explorer (left pane) ──

  /** The schema object the context menu was opened for. */
  schemaContextMenuObj = signal<SchemaObject | null>(null);

  /** Pixel position of the schema context menu (null = hidden). */
  schemaContextMenuPos = signal<{ x: number; y: number } | null>(null);

  /** True while fetching DDL from the server (for Copy actions). */
  schemaContextMenuLoading = signal(false);

  /** Opens the schema context menu on right-click. */
  onSchemaContextMenu(obj: SchemaObject, event: MouseEvent): void {
    event.preventDefault();
    event.stopPropagation();
    this.closeSchemaContextMenu();
    this.schemaContextMenuObj.set(obj);
    this.schemaContextMenuPos.set({ x: event.clientX, y: event.clientY });
  }

  /** Closes the schema context menu. */
  closeSchemaContextMenu(): void {
    this.schemaContextMenuObj.set(null);
    this.schemaContextMenuPos.set(null);
  }

  /** Deletes the object via DROP and refreshes the schema tree. */
  deleteSchemaObject(): void {
    const obj = this.schemaContextMenuObj();
    this.closeSchemaContextMenu();
    if (!obj) return;

    const objectType = obj.type === 'StoredProcedure' ? 'PROCEDURE' : obj.type.toUpperCase();
    const dropSql = `DROP ${objectType} IF EXISTS \`${obj.name.replace(/`/g, '``')}\``;

    const workspaceId = this.workspaceState.selectedWorkspaceId();
    if (!workspaceId) return;

    const confirmed = window.confirm(
      `Are you sure you want to delete ${obj.type.toLowerCase()} "${obj.name}"?\n\n${dropSql};`
    );
    if (!confirmed) return;

    this.queryService.execute({ workspaceId, sql: dropSql }).subscribe({
      next: () => {
        this.loadSchema(this.searchQuery() || undefined);
        this.querySuccess.set(`"${obj.name}" deleted successfully.`);
      },
      error: (err) => {
        this.queryError.set(err.error?.error || err.error?.message || err.message || 'Failed to delete object.');
      },
    });
  }

  /** Fetches the object's CREATE DDL and copies it to the system clipboard. */
  copyObjectToClipboard(): void {
    const obj = this.schemaContextMenuObj();
    this.closeSchemaContextMenu();
    if (!obj) return;

    const workspaceId = this.workspaceState.selectedWorkspaceId();
    if (!workspaceId) return;

    this.schemaContextMenuLoading.set(true);
    this.schemaService.getObjectDdl(workspaceId, obj.name, obj.type).subscribe({
      next: (response) => {
        this.schemaContextMenuLoading.set(false);
        const ddl = response.ddl;
        if (!ddl) {
          this.queryError.set(`Could not retrieve DDL for "${obj.name}".`);
          return;
        }
        navigator.clipboard.writeText(ddl).then(() => {
          this.querySuccess.set(`DDL for "${obj.name}" copied to clipboard.`);
        }).catch(() => {
          this.queryError.set('Failed to copy to clipboard. Check permissions.');
        });
      },
      error: (err) => {
        this.schemaContextMenuLoading.set(false);
        this.queryError.set(err.error?.message || err.message || 'Failed to retrieve object DDL.');
      },
    });
  }

  /** Fetches the object's CREATE DDL and inserts it into the editor. */
  copyObjectToEditor(): void {
    const obj = this.schemaContextMenuObj();
    this.closeSchemaContextMenu();
    if (!obj) return;

    const workspaceId = this.workspaceState.selectedWorkspaceId();
    if (!workspaceId) return;

    this.schemaContextMenuLoading.set(true);
    this.schemaService.getObjectDdl(workspaceId, obj.name, obj.type).subscribe({
      next: (response) => {
        this.schemaContextMenuLoading.set(false);
        const ddl = response.ddl;
        if (!ddl) {
          this.queryError.set(`Could not retrieve DDL for "${obj.name}".`);
          return;
        }
        this.monacoEditor()?.setValue(ddl);
        this.querySuccess.set(`DDL for "${obj.name}" loaded into editor.`);
      },
      error: (err) => {
        this.schemaContextMenuLoading.set(false);
        this.queryError.set(err.error?.message || err.message || 'Failed to retrieve object DDL.');
      },
    });
  }

  /** Closes context menu on any click outside. */
  @HostListener('document:click')
  onDocumentClick(): void {
    this.closeContextMenu();
    this.closeSchemaContextMenu();
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
    this.querySuccess.set(null);
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

        // Auto-refresh schema explorer after DDL operations (CREATE, ALTER, DROP, TRUNCATE, RENAME).
        // This ensures the left pane tree shows newly created objects without manual refresh.
        if (result.success && this.isDdlStatement(sql)) {
          this.loadSchema(this.searchQuery() || undefined);
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

  /**
   * Detects whether the SQL contains a DDL statement (CREATE, ALTER, DROP, TRUNCATE, RENAME).
   * Strips comments and DELIMITER directives first so they don't mask real DDL keywords.
   * Used to auto-refresh the schema explorer after structural changes.
   */
  private isDdlStatement(sql: string): boolean {
    // Strip single-line comments
    const noSingleLine = sql.replace(/--[^\n]*/g, '');
    // Strip block comments
    const noComments = noSingleLine.replace(/\/\*[\s\S]*?\*\//g, '');
    // Strip DELIMITER directives (mysql CLI commands, not SQL — the server never sees them)
    const noDelimiter = noComments.replace(/^\s*DELIMITER\s+\S+\s*$/gim, '');
    // Extract the first non-whitespace keyword
    const firstWord = noDelimiter.trim().split(/\s+/)[0]?.toUpperCase() ?? '';
    return firstWord === 'CREATE' || firstWord === 'ALTER' ||
           firstWord === 'DROP'   || firstWord === 'TRUNCATE' ||
           firstWord === 'RENAME';
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

  /** Exports the current SELECT query result as a CSV file. */
  exportCsv(): void {
    const result = this.queryResult();
    if (!result || !result.success || !result.isSelectQuery || result.columns.length === 0) return;

    const escapeCsv = (val: string | null): string => {
      if (val === null) return '';
      const str = String(val);
      // If the value contains commas, quotes, or newlines, wrap in quotes and escape inner quotes
      if (str.includes(',') || str.includes('"') || str.includes('\n') || str.includes('\r')) {
        return `"${str.replace(/"/g, '""')}"`;
      }
      return str;
    };

    // Header row
    const header = result.columns.map(c => escapeCsv(c.name)).join(',');

    // Data rows
    const rows = result.rows.map(row =>
      row.values.map(v => escapeCsv(v)).join(',')
    );

    const csvContent = [header, ...rows].join('\r\n');
    const blob = new Blob([csvContent], { type: 'text/csv;charset=utf-8;' });
    const url = URL.createObjectURL(blob);

    const link = document.createElement('a');
    link.href = url;
    link.setAttribute('download', `query-result-${Date.now()}.csv`);
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
    URL.revokeObjectURL(url);
  }

  // ── AI Assistant ──

  /** Sends the current AI input to the assistant along with the editor's SQL + history. */
  sendAiMessage(): void {
    const prompt = this.aiInput().trim();
    if (!prompt || this.aiLoading()) return;

    const workspaceId = this.workspaceState.selectedWorkspaceId();
    if (!workspaceId) {
      this.aiError.set('No workspace selected.');
      return;
    }

    // Append the user's message and clear the input.
    this.aiMessages.update(msgs => [...msgs, { role: 'user', content: prompt }]);
    this.aiInput.set('');
    this.aiError.set(null);
    this.aiLoading.set(true);

    // Build history from the prior turns (exclude the message we just added).
    const history: AiChatMessage[] = this.aiMessages()
      .slice(0, -1)
      .map(m => ({ role: m.role, content: m.content }));

    const currentSql = this.monacoEditor()?.getValue()?.trim() || undefined;

    // Append an empty assistant message that we progressively fill as tokens arrive.
    this.aiMessages.update(msgs => [...msgs, { role: 'assistant', content: '' }]);

    const appendDelta = (text: string) => {
      this.aiMessages.update(msgs => {
        if (msgs.length === 0) return msgs;
        const updated = [...msgs];
        const last = updated[updated.length - 1];
        updated[updated.length - 1] = { ...last, content: last.content + text };
        return updated;
      });
    };

    const removeEmptyAssistant = () => {
      this.aiMessages.update(msgs => {
        if (msgs.length === 0) return msgs;
        const last = msgs[msgs.length - 1];
        if (last.role === 'assistant' && last.content.length === 0) {
          return msgs.slice(0, -1);
        }
        return msgs;
      });
    };

    this.aiStreamController = this.aiService.assistStream(
      { workspaceId, prompt, history, currentSql },
      {
        onDelta: (text) => appendDelta(text),
        onDone: () => {
          this.aiStreamController = null;
          this.aiLoading.set(false);
        },
        onError: (message) => {
          this.aiStreamController = null;
          this.aiLoading.set(false);
          removeEmptyAssistant();
          this.aiError.set(message || 'Failed to reach the AI assistant.');
        },
      },
    );
  }

  /** Clears the AI conversation. */
  clearAiChat(): void {
    this.aiStreamController?.abort();
    this.aiStreamController = null;
    this.aiMessages.set([]);
    this.aiError.set(null);
    this.aiLoading.set(false);
  }

  /** Handles Enter (send) / Shift+Enter (newline) in the AI input box. */
  onAiInputKeydown(event: KeyboardEvent): void {
    if (event.key === 'Enter' && !event.shiftKey) {
      event.preventDefault();
      this.sendAiMessage();
    }
  }

  /**
   * Splits an assistant message into plain-text and fenced code-block parts
   * so the template can render code blocks with an "Insert" action.
   */
  parseAiMessage(content: string): AiMessagePart[] {
    const parts: AiMessagePart[] = [];
    const fence = /```(?:[a-zA-Z]+)?\n?([\s\S]*?)```/g;
    let lastIndex = 0;
    let match: RegExpExecArray | null;

    while ((match = fence.exec(content)) !== null) {
      if (match.index > lastIndex) {
        const text = content.slice(lastIndex, match.index).trim();
        if (text) parts.push({ type: 'text', content: text });
      }
      parts.push({ type: 'code', content: match[1].trim() });
      lastIndex = fence.lastIndex;
    }

    if (lastIndex < content.length) {
      const text = content.slice(lastIndex).trim();
      if (text) parts.push({ type: 'text', content: text });
    }

    if (parts.length === 0) {
      parts.push({ type: 'text', content: content.trim() });
    }
    return parts;
  }

  /** Inserts an AI-suggested SQL snippet into the active editor (replacing content). */
  insertAiSql(sql: string): void {
    this.monacoEditor()?.setValue(sql);
    const id = this.activeTabId();
    if (id != null) {
      this.tabs.update(tabs => tabs.map(t => (t.id === id ? { ...t, sql } : t)));
    }
  }

  /** Copies an AI-suggested SQL snippet to the clipboard. */
  copyAiSql(sql: string): void {
    if (this.isBrowser && navigator.clipboard) {
      navigator.clipboard.writeText(sql).catch(() => { /* ignore */ });
    }
  }

}