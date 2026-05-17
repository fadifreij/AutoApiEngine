import { Injectable, signal } from '@angular/core';

const STORAGE_KEY_ID = 'selectedWorkspaceId';
const STORAGE_KEY_NAME = 'selectedWorkspaceName';

@Injectable({ providedIn: 'root' })
export class WorkspaceStateService {
  selectedWorkspaceId = signal<string>(this.load(STORAGE_KEY_ID));
  selectedWorkspaceName = signal<string>(this.load(STORAGE_KEY_NAME));

  setSelectedWorkspace(id: string, name: string): void {
    this.selectedWorkspaceId.set(id);
    this.selectedWorkspaceName.set(name);
    this.save(STORAGE_KEY_ID, id);
    this.save(STORAGE_KEY_NAME, name);
  }

  private load(key: string): string {
    try {
      return localStorage.getItem(key) ?? '';
    } catch {
      return '';
    }
  }

  private save(key: string, value: string): void {
    try {
      localStorage.setItem(key, value);
    } catch { /* SSR or quota */ }
  }
}