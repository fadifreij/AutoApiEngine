import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { CardModule } from 'primeng/card';
import { InputTextModule } from 'primeng/inputtext';
import { WorkspaceStore } from '../../store/workspace.store';
import { url_workspace } from '../../../auth/constants';

@Component({
  selector: 'app-create-workspace',
  imports: [
    ReactiveFormsModule,
    CardModule,
    InputTextModule,
    ButtonModule,
  ],
  templateUrl: './create-workspace.component.html',
  styleUrl: './create-workspace.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CreateWorkspaceComponent {
  private readonly store = inject(WorkspaceStore);
  private readonly router = inject(Router);
  private readonly fb = inject(FormBuilder);

  protected readonly isSubmitting = signal(false);

  protected readonly form = this.fb.group({
    name: ['', [Validators.required, Validators.minLength(1)]],
    apiKey: [{ value: this.generateApiKey(), disabled: true }, Validators.required],
    dbUserName: ['', [Validators.required, Validators.minLength(1)]],
    dbPassword: ['', [Validators.required, Validators.minLength(1)]],
    isActive: [true],
  });

  regenerateApiKey(): void {
    this.form.controls.apiKey.setValue(this.generateApiKey());
  }

  goBack(): void {
    this.router.navigate(['/workspaces']);
  }

  onSubmit(): void {
    if (this.form.invalid || this.isSubmitting()) return;

    this.isSubmitting.set(true);

    const rawForm = this.form.getRawValue();
    const id = this.store.workspaceCount() + 1;
    const workspaceName = (rawForm.name ?? '').trim();
    const apiKey = rawForm.apiKey ?? this.generateApiKey();
    const dbUserName = (rawForm.dbUserName ?? '').trim();
    const dbPassword = rawForm.dbPassword ?? '';

    const newWorkspace = {
      id,
      name: workspaceName,
      apiKey,
      dbUserName,
      dbPassword,
      isActive: !!rawForm.isActive,
      dbType: 'sqlserver' as const,
      status: 'empty' as const,
      children: [
        { label: 'Tables', icon: 'pi pi-table', type: 'default' as const, children: [] },
        { label: 'Backups', icon: 'pi pi-history', type: 'default' as const, children: [] },
        { label: 'Stored Procedures', icon: 'pi pi-cog', type: 'default' as const, children: [] },
        { label: 'Functions', icon: 'pi pi-code', type: 'default' as const, children: [] },
      ],
    };

    this.store.addWorkspace(newWorkspace);
    this.router.navigate([url_workspace ]);
  }

  protected isInvalid(fieldName: string): boolean {
    const control = this.form.get(fieldName);
    return !!(control?.invalid && control?.touched);
  }

  private generateApiKey(): string {
    const part = () => Math.random().toString(36).slice(2, 10).toUpperCase();
    return `AE-${part()}-${part()}-${Date.now().toString(36).toUpperCase()}`;
  }
}
