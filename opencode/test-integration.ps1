param(
    [string]$OpenCodeUrl = "http://127.0.0.1:3000",
    [string]$Password = "local-dev-key"
)

$ErrorActionPreference = "Stop"
$InformationPreference = "Continue"

$cred = "opencode:$Password"
$encoded = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes($cred))
$headers = @{ Authorization = "Basic $encoded"; "Content-Type" = "application/json" }

function Send-Message($sessionId, $parts) {
    $body = @{ parts = $parts; noReply = $false } | ConvertTo-Json -Depth 10
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $response = Invoke-RestMethod -Uri "$OpenCodeUrl/session/$sessionId/message" -Method Post -Headers $headers -Body $body -ErrorAction Stop
    $sw.Stop()
    $text = ($response.parts | Where-Object { $_.type -eq "text" } | ForEach-Object { $_.text }) -join "`n"
    $model = $response.info.modelID
    $tokens = $response.info.tokens.total
    
    Write-Output "  Time: $($sw.Elapsed.TotalSeconds.ToString('F1'))s | Tokens: $tokens | Model: $model"
    return @{ Text = $text; Model = $model; Tokens = $tokens }
}

# Create session
$session = Invoke-RestMethod -Uri "$OpenCodeUrl/session" -Method Post -Headers $headers -Body '{}' -ErrorAction Stop
Write-Output "=== Session: $($session.id) ==="

# --- Test 1: Simple text response ---
Write-Output "`n--- TEST 1: Simple prompt (no tools) ---"
$r1 = Send-Message $session.id @(@{ type = "text"; text = "Say 'Hello from Big Pickle' and nothing else." })
Write-Output "Result: $($r1.Text)"
if ($r1.Text -match "Hello from Big Pickle") {
    Write-Output "PASS: Simple prompt works"
} else {
    Write-Output "UNEXPECTED: Response didn't match expected pattern"
}

# --- Test 2: Tool calling (list_tables) ---
Write-Output "`n--- TEST 2: Tool calling (list_tables) ---"
$sysPrompt = @'
You are DB Copilot. Available tools:
- list_tables: List all table names
- describe_table: Show columns (args: table_name)
- execute_query: Run SELECT (args: sql)

When you need a tool, output:
```json
{"tool": "tool_name", "arguments": {}}
```
'@

$r2 = Send-Message $session.id @(
    @{ type = "text"; text = "[System]`n$sysPrompt" }
    @{ type = "text"; text = "[User] What tables exist in ecommerce-db? Use list_tables." }
)
Write-Output "Result: $($r2.Text)"

if ($r2.Text -match '```json') {
    Write-Output "PASS: Tool call JSON block detected"
} else {
    Write-Output "No tool call JSON block found"
}

# --- Test 3: Database schema question ---
Write-Output "`n--- TEST 3: Schema description request ---"
$r3 = Send-Message $session.id @(
    @{ type = "text"; text = "[System]`n$sysPrompt" }
    @{ type = "text"; text = "[User] Show me the columns of the administration_organization table. Use describe_table." }
)
Write-Output "Result: $($r3.Text)"

if ($r3.Text -match '```json') {
    Write-Output "PASS: Tool call detected"
} else {
    Write-Output "No tool call - direct answer"
}

# --- Test 4: Non-DB question (graceful handling) ---
Write-Output "`n--- TEST 4: Non-DB question ---"
$r4 = Send-Message $session.id @(
    @{ type = "text"; text = "[System]`n$sysPrompt" }
    @{ type = "text"; text = "[User] What is the weather today?" }
)
Write-Output "Result: $($r4.Text)"

# --- Summary ---
Write-Output "`n========================================"
Write-Output "=== INTEGRATION TEST SUMMARY ==="
Write-Output "========================================"
Write-Output "OpenCode Server: $OpenCodeUrl"
Write-Output "Model: $($r1.Model)"
Write-Output "Tests: 4 completed"
Write-Output "========================================"
