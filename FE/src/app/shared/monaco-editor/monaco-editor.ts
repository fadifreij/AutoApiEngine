import {
  Component,
  ElementRef,
  OnInit,
  OnDestroy,
  AfterViewInit,
  output,
  input,
  PLATFORM_ID,
  Inject,
  NgZone,
} from '@angular/core';
import { isPlatformBrowser } from '@angular/common';

/**
 * Standalone component that wraps Monaco Editor for SQL editing.
 *
 * Loads Monaco from CDN (unpkg.com) — avoids all bundler conflicts with
 * Monaco's font files and web workers that plague esbuild-based Angular builds.
 *
 * Provides:
 *  - SQL syntax highlighting with custom theme
 *    (purple keywords, indigo functions, cyan identifiers,
 *     green strings, yellow numbers, gray comments)
 *  - Line numbers
 *  - Code folding, bracket matching, word wrap
 *
 * Only renders in browser — no-op during SSR.
 * Exposes `setValue()` / `getValue()` for programmatic access.
 */
@Component({
  selector: 'app-monaco-editor',
  standalone: true,
  template: `
    <div #editorContainer class="monaco-container">
      <div class="monaco-placeholder"
           [style.display]="placeholderVisible && placeholder() ? 'block' : 'none'"
           (click)="focusEditor()">{{ placeholder() }}</div>
    </div>
  `,
  styles: [
    `
      :host { display: flex; flex: 1; min-height: 0; }
      .monaco-container { flex: 1; min-height: 150px; position: relative; }
      .monaco-placeholder {
        position: absolute;
        top: 16px;
        left: 56px;
        z-index: 10;
        color: #6b7280;
        font-family: 'JetBrains Mono', 'Cascadia Code', 'Fira Code', monospace;
        font-size: 13px;
        cursor: text;
        user-select: none;
        white-space: nowrap;
        overflow: hidden;
        text-overflow: ellipsis;
        max-width: calc(100% - 72px);
        pointer-events: none;
      }
    `,
  ],
})
export class MonacoEditorComponent implements OnInit, AfterViewInit, OnDestroy {
  /** Guards theme definition against multiple registration */
  private static themeDefined = false;

  /** Initial SQL value to load into the editor */
  initialValue = input<string>('');

  /** Placeholder text shown when the editor is empty */
  placeholder = input<string>('');

  /** Emits the current editor content whenever it changes */
  valueChange = output<string>();

  private editor: any = null;
  private isBrowser: boolean;
  private initStarted = false;
  private containerEl!: HTMLDivElement;

  /** Controls visibility of the placeholder overlay */
  placeholderVisible = true;

  constructor(
    private el: ElementRef,
    private ngZone: NgZone,
    @Inject(PLATFORM_ID) platformId: Object
  ) {
    this.isBrowser = isPlatformBrowser(platformId);
  }

  ngOnInit(): void {
    if (!this.isBrowser) return;
  }

  ngAfterViewInit(): void {
    if (!this.isBrowser) return;

    // Find the container div — it's the first child of this host element
    const div = (this.el.nativeElement as HTMLElement).querySelector('.monaco-container') as HTMLDivElement;
    if (!div) {
      console.warn('[MonacoEditor] Container div not found');
      return;
    }
    this.containerEl = div;

    this.initEditor();
  }

  /**
   * Injects the Monaco Editor AMD loader into the page (once), then
   * creates the editor instance on the container element.
   */
  private async initEditor(): Promise<void> {
    if (this.initStarted) return;
    this.initStarted = true;

    // Wait for the Monaco loader to be available
    await this.ensureMonacoLoaded();

    // Define theme and create editor
    this.ngZone.runOutsideAngular(() => this.createEditor());
  }

  /**
   * Ensures the Monaco AMD loader script is loaded and the editor API is ready.
   * Uses a global promise so multiple editor instances share one loader.
   */
  private ensureMonacoLoaded(): Promise<void> {
    const g = globalThis as any;
    if (g.__monacoReady) return g.__monacoReady;

    g.__monacoReady = new Promise<void>((resolve) => {
      const MONACO_VERSION = '0.55.1';
      const CDN_BASE = `https://unpkg.com/monaco-editor@${MONACO_VERSION}/min/vs`;

      // If Monaco is already loaded, resolve immediately
      if (g.monaco?.editor) {
        resolve();
        return;
      }

      // Inject the loader script
      const script = document.createElement('script');
      script.src = `${CDN_BASE}/loader.js`;
      script.async = true;

      script.onload = () => {
        // Configure AMD paths and load editor.main
        g.require.config({
          paths: { vs: CDN_BASE },
        });

        g.require(['vs/editor/editor.main'], () => {
          resolve();
        });
      };

      script.onerror = () => {
        console.error('[MonacoEditor] Failed to load Monaco from CDN');
        g.__monacoReady = null; // Allow retry
        resolve(); // Resolve anyway so the error is handled downstream
      };

      document.head.appendChild(script);
    });

    return g.__monacoReady;
  }

  private createEditor(): void {
    const monaco = (globalThis as any).monaco;
    if (!monaco?.editor) {
      console.error('[MonacoEditor] Monaco API not available');
      return;
    }

    // ── Define custom theme (once, tracked by module var) ──
    if (!MonacoEditorComponent.themeDefined) {
      MonacoEditorComponent.themeDefined = true;
      monaco.editor.defineTheme('api-engine-sql', {
        base: 'vs-dark',
        inherit: true,
        rules: [
          { token: 'keyword', foreground: 'c084fc' },
          { token: 'keyword.sql', foreground: 'c084fc' },
          { token: 'predefined', foreground: '818cf8', fontStyle: 'bold' },
          { token: 'identifier', foreground: '67e8f9' },
          { token: 'string', foreground: '34d399' },
          { token: 'string.sql', foreground: '34d399' },
          { token: 'number', foreground: 'fbbf24' },
          { token: 'comment', foreground: '475569' },
          { token: 'comment.line', foreground: '475569' },
          { token: 'comment.block', foreground: '475569' },
          { token: 'type', foreground: '94a3b8' },
          { token: 'delimiter', foreground: '64748b' },
        ],
        colors: {
          'editor.background': '#1e1e2e',
          'editor.foreground': '#e2e8f0',
          'editor.lineHighlightBackground': '#262640',
          'editor.selectionBackground': '#3d3d5c',
          'editor.selectionHighlightBackground': '#2d2d44',
          'editorCursor.foreground': '#818cf8',
          'editorLineNumber.foreground': '#475569',
          'editorLineNumber.activeForeground': '#64748b',
          'editorIndentGuide.background': '#2d2d44',
          'editorIndentGuide.activeBackground': '#3d3d5c',
          'editorBracketMatch.background': '#2d2d44',
          'editorBracketMatch.border': '#6366f1',
          'editorWidget.background': '#1e1e2e',
          'editorWidget.border': '#2d2d44',
          'input.background': '#2d2d44',
          'input.border': '#3d3d5c',
          'focusBorder': '#6366f1',
          'list.activeSelectionBackground': '#2d2d44',
          'list.hoverBackground': '#262640',
        },
      });
    }

    // ── Create editor ──
    this.editor = monaco.editor.create(this.containerEl, {
      value: this.initialValue(),
      language: 'sql',
      theme: 'api-engine-sql',
      fontSize: 13,
      fontFamily: "'JetBrains Mono', 'Cascadia Code', 'Fira Code', monospace",
      lineNumbers: 'on',
      lineNumbersMinChars: 3,
      glyphMargin: false,
      folding: true,
      foldingHighlight: false,
      minimap: { enabled: false },
      scrollBeyondLastLine: false,
      automaticLayout: true,
      tabSize: 2,
      wordWrap: 'on',
      renderWhitespace: 'selection',
      bracketPairColorization: { enabled: true },
      padding: { top: 16, bottom: 16 },
      cursorStyle: 'line',
      cursorWidth: 2,
      smoothScrolling: true,
      overviewRulerLanes: 0,
      overviewRulerBorder: false,
      hideCursorInOverviewRuler: true,
      scrollbar: {
        vertical: 'visible',
        horizontal: 'visible',
        verticalScrollbarSize: 10,
        horizontalScrollbarSize: 10,
        useShadows: false,
      },
    });

    // ── Placeholder: update visibility based on editor state ──
    const updatePlaceholder = (): void => {
      const value = this.editor?.getValue() ?? '';
      this.placeholderVisible = !value || value.trim() === '';
    };

    // Set initial placeholder state
    updatePlaceholder();

    this.editor.onDidFocusEditorText(() => {
      this.ngZone.run(() => { this.placeholderVisible = false; });
    });

    this.editor.onDidBlurEditorText(() => {
      this.ngZone.run(() => { updatePlaceholder(); });
    });

    // ── Emit content changes + update placeholder ──
    this.editor.onDidChangeModelContent(() => {
      const value = this.editor.getValue();
      this.ngZone.run(() => {
        updatePlaceholder();
        this.valueChange.emit(value);
      });
    });
  }

  /** Programmatically set the editor content */
  setValue(value: string): void {
    if (this.editor) {
      this.editor.setValue(value);
    }
  }

  /** Get the current editor content */
  getValue(): string {
    return this.editor?.getValue() ?? '';
  }

  /** Programmatically focus the editor */
  focusEditor(): void {
    if (this.editor) {
      this.editor.focus();
    }
  }

  ngOnDestroy(): void {
    if (this.editor) {
      this.editor.dispose();
      this.editor = null;
    }
  }
}
