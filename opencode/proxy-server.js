/**
 * OpenCode API Proxy
 * ===================
 * Bridges OpenAI-compatible Chat Completions API (used by the .NET backend)
 * with the OpenCode server's session-based API.
 *
 * This allows the existing OpencodeAiAssistantService to talk to the local
 * OpenCode server (with Big Pickle LLM) without changing its request format.
 *
 * Environment variables (project-specific naming):
 *   OPENCODE_SERVER_URL    - OpenCode server base URL (default: http://127.0.0.1:3000)
 *   OPENCODE_PROXY_PORT    - Port for this proxy (default: 3088)
 *   OPENCODE_SERVER_PASS   - OpenCode server password (default: local-dev-key)
 *   OPENCODE_BIG_PICKLE_MODEL - Model ID (default: opencode/big-pickle)
 */

const http = require("http");
const { v4: uuidv4 } = require("uuid");

// ── Configuration (project-specific env vars) ──
const OPENCODE_SERVER_URL = process.env.OPENCODE_SERVER_URL || "http://127.0.0.1:3000";
const PROXY_PORT = parseInt(process.env.OPENCODE_PROXY_PORT || "3088", 10);
const SERVER_PASS = process.env.OPENCODE_SERVER_PASS || "local-dev-key";
const DEFAULT_MODEL = process.env.OPENCODE_BIG_PICKLE_MODEL || "opencode/big-pickle";

// ── Session cache: map sessionId → last used timestamp ──
const sessionCache = new Map();
const SESSION_TTL_MS = 10 * 60 * 1000; // 10 minutes

const BASIC_AUTH = `opencode:${SERVER_PASS}`;
const AUTH_HEADER = `Basic ${Buffer.from(BASIC_AUTH).toString("base64")}`;

// ── Helpers ──

function opencodeFetch(path, options = {}) {
  const url = `${OPENCODE_SERVER_URL}${path}`;
  const headers = {
    Authorization: AUTH_HEADER,
    "Content-Type": "application/json",
    ...options.headers,
  };
  return fetch(url, { ...options, headers });
}

/**
 * Gets or creates an OpenCode session.
 * We maintain a singleton session for the proxy to keep context across calls.
 */
let currentSessionId = null;

async function getOrCreateSession() {
  if (currentSessionId) {
    // Check if session still exists
    try {
      const res = await opencodeFetch(`/session/${currentSessionId}`);
      if (res.ok) return currentSessionId;
    } catch {
      // Session expired, create new one
    }
  }

  const res = await opencodeFetch("/session", {
    method: "POST",
    body: JSON.stringify({}),
  });

  if (!res.ok) {
    const err = await res.text();
    throw new Error(`Failed to create OpenCode session: ${res.status} ${err}`);
  }

  const session = await res.json();
  currentSessionId = session.id;
  sessionCache.set(currentSessionId, Date.now());
  return currentSessionId;
}

/**
 * Sends a message to an OpenCode session and returns the response.
 */
async function sendMessage(sessionId, parts) {
  const res = await opencodeFetch(`/session/${sessionId}/message`, {
    method: "POST",
    body: JSON.stringify({ parts, noReply: false }),
  });

  if (!res.ok) {
    const err = await res.text();
    throw new Error(`OpenCode message failed: ${res.status} ${err}`);
  }

  const data = await res.json();

  // Extract text from response parts
  const textParts = data.parts.filter((p) => p.type === "text");
  const reasoningParts = data.parts.filter((p) => p.type === "reasoning");
  const text = textParts.map((p) => p.text).join("\n");
  const reasoning = reasoningParts.map((p) => p.text).join("\n");

  return {
    text,
    reasoning,
    modelID: data.info?.modelID || "big-pickle",
    providerID: data.info?.providerID || "opencode",
    finishReason: data.info?.finish || "stop",
    tokenUsage: data.info?.tokens || {},
    raw: data,
  };
}

/**
 * Converts OpenAI-format messages to OpenCode text parts.
 * Combines system messages, user messages, and assistant messages into a coherent prompt.
 */
function convertMessagesToParts(messages) {
  const parts = [];

  for (const msg of messages) {
    if (msg.role === "system") {
      // System message becomes a text part with context marker
      parts.push({
        type: "text",
        text: `[System Instruction]\n${msg.content || ""}`,
      });
    } else if (msg.role === "user") {
      // Handle multi-part or simple user messages
      if (Array.isArray(msg.content)) {
        for (const item of msg.content) {
          if (item.type === "text") {
            parts.push({ type: "text", text: item.text });
          } else if (item.type === "image_url") {
            parts.push({
              type: "text",
              text: `[Image: ${item.image_url?.url || "unknown"}]`,
            });
          }
        }
      } else {
        parts.push({ type: "text", text: msg.content || "" });
      }
    } else if (msg.role === "assistant") {
      if (msg.content) {
        parts.push({
          type: "text",
          text: `[Assistant]\n${msg.content}`,
        });
      }
      if (msg.tool_calls) {
        for (const tc of msg.tool_calls) {
          parts.push({
            type: "text",
            text: `[Tool Call: ${tc.function?.name}] Arguments: ${tc.function?.arguments || "{}"}`,
          });
        }
      }
    } else if (msg.role === "tool") {
      parts.push({
        type: "text",
        text: `[Tool Result for ${msg.tool_call_id || "unknown"}]\n${msg.content || ""}`,
      });
    }
  }

  return parts;
}

/**
 * Generates an OpenAI-compatible chat completion response from OpenCode output.
 */
function buildOpenAIResponse(ocResponse, requestModel, stream = false) {
  const model = requestModel || DEFAULT_MODEL;

  const choice = {
    index: 0,
    message: {
      role: "assistant",
      content: ocResponse.text || null,
    },
    finish_reason: ocResponse.finishReason || "stop",
  };

  // Include reasoning if present (as a custom field)
  if (ocResponse.reasoning) {
    choice.message.reasoning = ocResponse.reasoning;
  }

  return {
    id: `chatcmpl-${uuidv4().replace(/-/g, "")}`,
    object: stream ? "chat.completion.chunk" : "chat.completion",
    created: Math.floor(Date.now() / 1000),
    model: model,
    choices: [choice],
    usage: {
      prompt_tokens: ocResponse.tokenUsage?.input || 0,
      completion_tokens: ocResponse.tokenUsage?.output || 0,
      total_tokens: ocResponse.tokenUsage?.total || 0,
    },
  };
}

// ── HTTP Server ──

const server = http.createServer(async (req, res) => {
  // CORS headers
  res.setHeader("Access-Control-Allow-Origin", "*");
  res.setHeader("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
  res.setHeader("Access-Control-Allow-Headers", "Content-Type, Authorization");

  if (req.method === "OPTIONS") {
    res.writeHead(204);
    res.end();
    return;
  }

  // ── Health endpoint ──
  if (req.method === "GET" && (req.url === "/health" || req.url === "/")) {
    res.writeHead(200, { "Content-Type": "application/json" });
    res.end(
      JSON.stringify({
        status: "ok",
        proxyVersion: "1.0.0",
        opencodeServer: OPENCODE_SERVER_URL,
        model: DEFAULT_MODEL,
      })
    );
    return;
  }

  // ── Model listing (OpenAI-compatible) ──
  if (req.method === "GET" && req.url === "/v1/models") {
    res.writeHead(200, { "Content-Type": "application/json" });
    res.end(
      JSON.stringify({
        object: "list",
        data: [
          {
            id: DEFAULT_MODEL,
            object: "model",
            created: Math.floor(Date.now() / 1000),
            owned_by: "opencode",
          },
          {
            id: "big-pickle",
            object: "model",
            created: Math.floor(Date.now() / 1000),
            owned_by: "opencode",
          },
        ],
      })
    );
    return;
  }

  // ── Chat Completions ──
  if (req.method === "POST" && (req.url === "/v1/chat/completions" || req.url === "/chat/completions")) {
    let body = "";
    req.on("data", (chunk) => (body += chunk));
    req.on("end", async () => {
      try {
        const request = JSON.parse(body);
        const messages = request.messages || [];
        const model = request.model || DEFAULT_MODEL;
        const stream = request.stream === true;

        // Clean expired sessions
        const now = Date.now();
        for (const [sid, ts] of sessionCache) {
          if (now - ts > SESSION_TTL_MS) {
            sessionCache.delete(sid);
          }
        }

        // Get or create a session
        const sessionId = await getOrCreateSession();

        // Convert messages to OpenCode parts
        const parts = convertMessagesToParts(messages);

        // If there are tool definitions, append them as context
        if (request.tools && request.tools.length > 0) {
          parts.push({
            type: "text",
            text: `[Available Tools]\n${JSON.stringify(request.tools, null, 2)}`,
          });
        }

        // Send to OpenCode server
        const ocResponse = await sendMessage(sessionId, parts);

        // Build OpenAI-compatible response
        const openaiResponse = buildOpenAIResponse(ocResponse, model, stream);

        if (stream) {
          // Streaming SSE response
          res.writeHead(200, {
            "Content-Type": "text/event-stream",
            "Cache-Control": "no-cache",
            Connection: "keep-alive",
          });

          // Send a single chunk (OpenCode returns complete responses)
          const chunk = {
            id: openaiResponse.id,
            object: "chat.completion.chunk",
            created: openaiResponse.created,
            model: model,
            choices: [
              {
                index: 0,
                delta: { role: "assistant", content: ocResponse.text || "" },
                finish_reason: null,
              },
            ],
          };
          res.write(`data: ${JSON.stringify(chunk)}\n\n`);

          // Final chunk with finish_reason
          const finalChunk = {
            id: openaiResponse.id,
            object: "chat.completion.chunk",
            created: openaiResponse.created,
            model: model,
            choices: [
              {
                index: 0,
                delta: {},
                finish_reason: ocResponse.finishReason || "stop",
              },
            ],
            usage: openaiResponse.usage,
          };
          res.write(`data: ${JSON.stringify(finalChunk)}\n\n`);
          res.write("data: [DONE]\n\n");
          res.end();
        } else {
          // Non-streaming response
          res.writeHead(200, { "Content-Type": "application/json" });
          res.end(JSON.stringify(openaiResponse));
        }
      } catch (err) {
        console.error("Proxy error:", err);
        res.writeHead(500, { "Content-Type": "application/json" });
        res.end(
          JSON.stringify({
            error: {
              message: `OpenCode proxy error: ${err.message}`,
              type: "proxy_error",
            },
          })
        );
      }
    });
    return;
  }

  // ── 404 ──
  res.writeHead(404, { "Content-Type": "application/json" });
  res.end(JSON.stringify({ error: "Not found" }));
});

// ── Start Server ──
server.listen(PROXY_PORT, "127.0.0.1", () => {
  console.log(`[OpenCode Proxy] Running on http://127.0.0.1:${PROXY_PORT}`);
  console.log(`[OpenCode Proxy] Forwarding to OpenCode server at ${OPENCODE_SERVER_URL}`);
  console.log(`[OpenCode Proxy] Model: ${DEFAULT_MODEL}`);
  console.log(`[OpenCode Proxy] OpenAI-compatible endpoint: http://127.0.0.1:${PROXY_PORT}/v1/chat/completions`);
});

// Graceful shutdown
process.on("SIGINT", () => {
  console.log("\n[OpenCode Proxy] Shutting down...");
  server.close(() => process.exit(0));
});

process.on("SIGTERM", () => {
  server.close(() => process.exit(0));
});
