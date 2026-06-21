import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { AuthService } from '../../shared/auth/auth.service';

/** A single chat message exchanged with the AI assistant. */
export interface AiChatMessage {
    role: 'user' | 'assistant';
    content: string;
}

export interface AiAssistRequest {
    workspaceId: string;
    prompt: string;
    history: AiChatMessage[];
    currentSql?: string;
}

export interface AiAssistResponse {
    success: boolean;
    reply: string;
    error: string | null;
    model: string | null;
}

/** A single chunk emitted while streaming an assistant reply. */
export interface AiStreamChunk {
    delta?: string;
    done?: boolean;
    error?: string | null;
    model?: string | null;
    /** True when the AI executed a write operation on the database during this turn. */
    dbChanged?: boolean;
}

/** Callbacks invoked as a streamed reply is received. */
export interface AiStreamHandlers {
    onDelta: (text: string) => void;
    onDone?: (model: string | null, dbChanged: boolean) => void;
    onError?: (message: string) => void;
}

@Injectable({ providedIn: 'root' })
export class AiService {
    private readonly apiUrl = `${environment.apiUrl}/ai`;
    private readonly auth = inject(AuthService);

    constructor(private http: HttpClient) { }

    /**
     * Sends a prompt (plus optional conversation history and the editor's current SQL)
     * to the advisory AI database assistant and returns its reply.
     */
    assist(request: AiAssistRequest): Observable<AiAssistResponse> {
        return this.http.post<AiAssistResponse>(`${this.apiUrl}/assist`, request, {
            withCredentials: true,
        });
    }

    /**
     * Streams the assistant reply token-by-token via Server-Sent Events.
     * Uses fetch() (Angular's HttpClient cannot easily consume partial text streams),
     * so the Keycloak bearer token is attached manually.
     *
     * Returns an AbortController; call .abort() to cancel the stream.
     */
    assistStream(request: AiAssistRequest, handlers: AiStreamHandlers): AbortController {
        const controller = new AbortController();
        const token = this.auth.getAccessToken();

        const headers: Record<string, string> = { 'Content-Type': 'application/json' };
        if (token) {
            headers['Authorization'] = `Bearer ${token}`;
        }

        (async () => {
            try {
                const response = await fetch(`${this.apiUrl}/assist/stream`, {
                    method: 'POST',
                    headers,
                    body: JSON.stringify(request),
                    credentials: 'include',
                    signal: controller.signal,
                });

                if (!response.ok || !response.body) {
                    handlers.onError?.(`AI service error (${response.status}).`);
                    return;
                }

                const reader = response.body.getReader();
                const decoder = new TextDecoder();
                let buffer = '';

                while (true) {
                    const { value, done } = await reader.read();
                    if (done) break;

                    buffer += decoder.decode(value, { stream: true });

                    // SSE events are separated by a blank line.
                    let sepIndex: number;
                    while ((sepIndex = buffer.indexOf('\n\n')) !== -1) {
                        const rawEvent = buffer.slice(0, sepIndex);
                        buffer = buffer.slice(sepIndex + 2);

                        const dataLine = rawEvent
                            .split('\n')
                            .find((l) => l.startsWith('data:'));
                        if (!dataLine) continue;

                        const data = dataLine.slice('data:'.length).trim();
                        if (!data) continue;

                        let chunk: AiStreamChunk;
                        try {
                            chunk = JSON.parse(data);
                        } catch {
                            continue;
                        }

                        if (chunk.error) {
                            handlers.onError?.(chunk.error);
                            return;
                        }
                        if (chunk.delta) {
                            handlers.onDelta(chunk.delta);
                        }
                        if (chunk.done) {
                            handlers.onDone?.(chunk.model ?? null, chunk.dbChanged ?? false);
                            return;
                        }
                    }
                }

                // Stream closed without an explicit done signal.
                handlers.onDone?.(null, false);
            } catch (err: unknown) {
                if (controller.signal.aborted) {
                    return; // Cancelled by the caller; not an error.
                }
                const message = err instanceof Error ? err.message : 'The AI stream failed.';
                handlers.onError?.(message);
            }
        })();

        return controller;
    }
}
