import {
  Component,
  signal,
  inject,
  OnInit,
  afterNextRender,
  ChangeDetectionStrategy,
} from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { ToastModule } from 'primeng/toast';
import { MessageService } from 'primeng/api';
import { ThemeService } from './core/theme/theme.service';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, ToastModule],
  providers: [MessageService], // Singleton at app level
  template: `
    <!-- Global toast outlet — positioned top-right -->
    <p-toast
      position="top-right"
      [breakpoints]="{ '960px': { width: '100%', right: '0', left: '0' } }"
    ></p-toast>

    <!-- All routes render here -->
    <router-outlet></router-outlet>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class App implements OnInit {
  protected readonly title = signal('autoApiEngine');
  private readonly themeService = inject(ThemeService);

  constructor() {
    // Apply persisted / OS-detected theme after first render (browser only)
    afterNextRender(() => {
      this.themeService.apply(this.themeService.active());
    });
  }

  ngOnInit(): void {}
}
