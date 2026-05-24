import { Injectable, signal } from '@angular/core';
import * as signalR from '@microsoft/signalr';
import { environment } from '../../environments/environment';

export interface ProgressEvent {
    operation: string;
    percentage: number;
    message: string;
    fileName?: string;
}

@Injectable({ providedIn: 'root' })
export class DatabaseProgressService {
    private connection: signalR.HubConnection | null = null;

    latestProgress = signal<ProgressEvent | null>(null);
    connected = signal(false);

    async start(): Promise<void> {
        if (this.connection?.state === signalR.HubConnectionState.Connected) {
            this.connected.set(true);
            return;
        }

        // Build hub URL from the API base (e.g. https://localhost:7002/api -> https://localhost:7002/hubs/progress)
        const baseUrl = environment.apiUrl.replace(/\/api\/?$/, '');
        const hubUrl = `${baseUrl}/hubs/progress`;

        this.connection = new signalR.HubConnectionBuilder()
            .withUrl(hubUrl)
            .withAutomaticReconnect()
            .build();

        this.connection.on('ReceiveProgress', (data: ProgressEvent) => {
            this.latestProgress.set({ ...data });
        });

        try {
            await this.connection.start();
            this.connected.set(true);
        } catch (err) {
            console.warn('SignalR connection failed, progress will use fallback', err);
            this.connected.set(false);
        }
    }

    async stop(): Promise<void> {
        if (this.connection) {
            try { await this.connection.stop(); } catch { }
            this.connection = null;
            this.connected.set(false);
        }
    }
}
