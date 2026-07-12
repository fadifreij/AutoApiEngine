# OpenCode — Big Pickle LLM Integration

This directory contains the configuration and tooling for the OpenCode server integration
with the Big Pickle LLM (`opencode/big-pickle`).

## Architecture

```
┌──────────────┐    Session API (direct)    ┌──────────────────┐
│  .NET Backend │ ─── POST /session ───────▶ │  OpenCode Server  │
│  (C#)         │ ─── POST /session/   ───▶ │  (port 3000)      │
│               │ ◀──── /message ──────────  │  Big Pickle LLM   │
└──────────────┘                             └──────────────────┘
                                                    │
                                          ┌─────────┴─────────┐
                                          │   MCP Filesystem   │
                                          │  (workspace access) │
                                          └───────────────────┘
```

The backend communicates directly with the OpenCode server via its native session/message
API — no proxy layer. Tool definitions are embedded in the system prompt as JSON text,
and the AI responds with JSON code blocks for tool calls.

## Prerequisites

- Node.js v18+ (v22.18.0 installed)
- OpenCode CLI v1.17.11+ (`npm install opencode-ai`) in opencode directory
- .NET 10 SDK (for the backend)

## Environment Variables (Project-Specific)

| Variable | Default | Description |
|----------|---------|-------------|
| `OPENCODE_SERVER_PASS` | `local-dev-key` | OpenCode server password for API auth |

## How to Start

> **Note:** The OpenCode server is **auto-started** by the backend as a child process.
> Just run the backend — no separate command needed.

### Step 1: Start the backend (auto-starts OpenCode)

```powershell
dotnet run --project BE/AutoApiEngine.ApiServices/AutoApiEngine.ApiServices.csproj --launch-profile https
```

### Step 2: Start the frontend (optional)

```powershell
cd FE; npm start
```

### Manual start (if needed)

If you need to run the OpenCode server independently:

```powershell
$env:OPENCODE_SERVER_PASSWORD = "local-dev-key"
opencode serve --port 3000 --hostname 127.0.0.1
```

### Verify

```powershell
# Check OpenCode server health
curl http://127.0.0.1:3000/global/health

# Test API directly
$cred = "opencode:local-dev-key"
$encoded = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes($cred))
$headers = @{ Authorization = "Basic $encoded"; "Content-Type" = "application/json" }

$session = Invoke-RestMethod -Uri "http://127.0.0.1:3000/session" -Method Post -Headers $headers -Body '{}'
$body = @{ parts = @(@{ type = "text"; text = "Hello!" }); noReply = $false } | ConvertTo-Json -Depth 10
$response = Invoke-RestMethod -Uri "http://127.0.0.1:3000/session/$($session.id)/message" -Method Post -Headers $headers -Body $body
$response.parts | Where-Object { $_.type -eq "text" } | ForEach-Object { $_.text }
```

## MCP Configuration

The `opencode.json` at the project root configures a filesystem MCP server via
`@modelcontextprotocol/server-filesystem`, giving Big Pickle access to the full
`D:\AutoCrud_Full` workspace.

## Backend Configuration

The `OpencodeAiAssistantService` in `BE/AutoApiEngine.Services/DatabaseManagementServices/`
calls the OpenCode server directly. Configuration is in `appsettings.json`:

```json
"OpencodeAi": {
    "BaseUrl": "http://127.0.0.1:3000",
    "ApiKey": "local-dev-key",
    "Model": "opencode/big-pickle",
    "Temperature": 0.1,
    "MaxTokens": 1024
}
```
