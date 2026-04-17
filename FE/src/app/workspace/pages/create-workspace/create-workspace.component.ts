import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { CardModule } from 'primeng/card';
import { InputTextModule } from 'primeng/inputtext';
import { WorkspaceStore } from '../../store/workspace.store';

@Component({
  selector: 'app-create-workspace',
  imports: [
    ReactiveFormsModule,
    CardModule,
    InputTextModule,
    ButtonModule,
  ],
  template: `
    <div class="create-ws">
      <div class="create-ws__header">
        <p-button
          icon="pi pi-arrow-left"
          [text]="true"
          [rounded]="true"
          severity="secondary"
          (onClick)="goBack()"
          ariaLabel="Back to dashboard"
        />
        <h1>Create New Workspace</h1>
      </div>

      <p-card styleClass="create-ws__card">
        <form [formGroup]="form" (ngSubmit)="onSubmit()">
          <div class="create-ws__field">
            <label for="name">Workspace Name (Database Name)</label>
            <input
              pInputText
              id="name"
              formControlName="name"
              placeholder="e.g., SalesDB_2026"
              [class.ng-invalid]="form.get('name')?.invalid && form.get('name')?.touched"
            />
            @if (form.get('name')?.invalid && form.get('name')?.touched) {
              <small class="create-ws__error">Name is required</small>
            }
          </div>

          <div class="create-ws__field">
            <label for="apiKey">API Key</label>
            <div class="create-ws__api-key-wrap">
              <input
                pInputText
                id="apiKey"
                formControlName="apiKey"
                [disabled]="true"
                class="create-ws__disabled-input"
              />
              <p-button
                label="Generate New"
                icon="pi pi-refresh"
                size="small"
                severity="secondary"
                [outlined]="true"
                (onClick)="regenerateApiKey()"
              />
            </div>
          </div>

          <div class="create-ws__field">
            <label for="dbUserName">DbUser Name</label>
            <input
              pInputText
              id="dbUserName"
              formControlName="dbUserName"
              placeholder="e.g., sa"
              [class.ng-invalid]="form.get('dbUserName')?.invalid && form.get('dbUserName')?.touched"
            />
          </div>

          <div class="create-ws__field">
            <label for="dbPassword">DbPassword</label>
            <input
              pInputText
              id="dbPassword"
              type="password"
              formControlName="dbPassword"
              placeholder="Enter database password"
              [class.ng-invalid]="form.get('dbPassword')?.invalid && form.get('dbPassword')?.touched"
            />
          </div>

          <label class="create-ws__checkbox-row" for="isActive">
            <input id="isActive" type="checkbox" formControlName="isActive" />
            <span>isActive</span>
          </label>

          @if (form.get('dbUserName')?.invalid && form.get('dbUserName')?.touched) {
            <small class="create-ws__error">DbUser Name is required</small>
          }
          @if (form.get('dbPassword')?.invalid && form.get('dbPassword')?.touched) {
            <small class="create-ws__error">DbPassword is required</small>
          }

          <div class="create-ws__actions">
            <p-button
              label="Cancel"
              severity="secondary"
              [outlined]="true"
              (onClick)="goBack()"
            />
            <p-button
              label="Create Workspace"
              icon="pi pi-check"
              type="submit"
              [disabled]="form.invalid || isSubmitting()"
              (onClick)="onSubmit()"
            />
          </div>
        </form>
      </p-card>
    </div>
  `,
  styles: [`
    .create-ws {
      max-width: 560px;
      margin: 0 auto;
      padding: 2rem;
    }

    .create-ws__header {
      display: flex;
      align-items: center;
      gap: 0.75rem;
      margin-bottom: 2rem;

      h1 {
        margin: 0;
        font-size: 1.5rem;
      }
    }

    .create-ws__card {
      :host ::ng-deep .p-card-body {
        padding: 2rem;
      }
    }

    .create-ws__field {
      display: flex;
      flex-direction: column;
      gap: 0.5rem;
      margin-bottom: 1.5rem;

      label {
        font-weight: 500;
        font-size: 0.875rem;
      }

      input {
        width: 100%;
      }
    }

    .create-ws__api-key-wrap {
      display: grid;
      grid-template-columns: 1fr auto;
      gap: 0.65rem;
      align-items: center;
    }

    .create-ws__disabled-input {
      opacity: 1;
    }

    .create-ws__checkbox-row {
      display: flex;
      align-items: center;
      gap: 0.5rem;
      margin-top: -0.5rem;
      margin-bottom: 1rem;
      font-size: 0.95rem;
      cursor: pointer;
    }

    .create-ws__error {
      color: var(--aae-danger);
      font-size: 0.8rem;
    }

    .create-ws__actions {
      display: flex;
      gap: 0.75rem;
      justify-content: flex-end;
      margin-top: 2rem;
    }

  `],
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
    this.router.navigate(['/workspaces/dashboard']);
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
    this.router.navigate(['/workspaces/dashboard']);
  }

  private generateApiKey(): string {
    const part = () => Math.random().toString(36).slice(2, 10).toUpperCase();
    return `AE-${part()}-${part()}-${Date.now().toString(36).toUpperCase()}`;
  }
}
