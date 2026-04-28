import { Component, signal } from '@angular/core';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'app-query-studio',
  standalone: true,
  imports: [RouterLink],
  templateUrl: './query-studio.html',
  styleUrl: './query-studio.scss'
})
export class QueryStudio {
  isRunning = signal(false);

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
