import { Component, signal } from '@angular/core';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'app-workspace-new',
  standalone: true,
  imports: [RouterLink],
  templateUrl: './workspace-new.html',
  styleUrl: './workspace-new.scss'
})
export class WorkspaceNew {
  dbType = signal<'hosted' | 'external'>('hosted');
  showEncryptionKey = signal(false);
}
