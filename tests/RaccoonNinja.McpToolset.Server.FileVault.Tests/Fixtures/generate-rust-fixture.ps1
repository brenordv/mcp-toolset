# Store-compat fixture generator.
#
# Purpose: produce a small vault store written by the REAL Rust vault-mcp binary, so the C#
# server's tests can prove it opens a Rust-written store with no data loss. The fixture is
# synthetic content only and is meant to be committed to the repo.
#
# What it does:
#   1. Recreates the 'rust-store' folder next to this script (deleting a previous fixture).
#   2. Starts the Rust binary (c:\path\to\the\rust\mcp-vault\vault-mcp.exe)
#      with VAULT_MCP_HOME pointed at that folder.
#   3. Sends JSON-RPC tool calls over stdin, PACED (one every 300 ms) so the server never
#      handles two writes concurrently — piping them all at once made racing tool calls fight
#      for the store's write lock ("database is locked").
#   4. Closes stdin so the server checkpoints WAL and shuts down cleanly, then prints the
#      responses and the fixture tree.
#
# It touches nothing outside the 'rust-store' folder. The real ~/.vault-mcp is not involved.
#
# NOTE: re-running rewrites vault.db even when the logical content is identical (row timestamps
# differ), so expect a dirty git diff on the fixture db after any regeneration. Snapshot files
# are hash-named and byte-stable. The Rust binary path below is machine-specific by design.

$ErrorActionPreference = 'Stop'

$fixtureDir = Join-Path $PSScriptRoot 'rust-store'
if (Test-Path $fixtureDir) { Remove-Item -Recurse -Force $fixtureDir -Confirm:$false }
New-Item -ItemType Directory -Force $fixtureDir | Out-Null

$exe = 'c:\path\to\the\rust\mcp-vault\vault-mcp.exe'

function ToolCall([int]$id, [string]$name, [hashtable]$arguments) {
    @{ jsonrpc = '2.0'; id = $id; method = 'tools/call'
       params = @{ name = $name; arguments = $arguments } } | ConvertTo-Json -Compress -Depth 8
}

$messages = @(
    '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"fixture-gen","version":"0"}}}'
    '{"jsonrpc":"2.0","method":"notifications/initialized"}'
    # A three-version text note with tags (save, save, append).
    (ToolCall 2 'vault_save'   @{ name = 'multi-version'; project = 'fixture'; content = "alpha`n"; summary = 'first version'; tags = @('kind-a', 'shared') })
    (ToolCall 3 'vault_save'   @{ name = 'multi-version'; project = 'fixture'; content = "alpha`nbeta`n"; summary = 'second version'; base_version = 1 })
    (ToolCall 4 'vault_append' @{ name = 'multi-version'; project = 'fixture'; content = "gamma`n"; base_version = 2 })
    # Markdown, JSON, and YAML notes (exercise formats + editors' history ops).
    (ToolCall 5 'vault_save'     @{ name = 'md-note'; project = 'fixture'; content = "# Title`n`n## Section`nbody`n"; summary = 'markdown'; format = 'markdown' })
    (ToolCall 6 'vault_edit_section' @{ name = 'md-note'; project = 'fixture'; heading = 'Section'; content = 'edited body'; base_version = 1 })
    (ToolCall 7 'vault_save'     @{ name = 'json-note'; project = 'fixture'; content = '{"a": {"b": 1}}'; summary = 'json'; format = 'json' })
    (ToolCall 8 'vault_edit_key' @{ name = 'json-note'; project = 'fixture'; key_path = 'a.b'; value = 42; base_version = 1 })
    (ToolCall 9 'vault_save'     @{ name = 'yaml-note'; project = 'fixture'; content = "a:`n  b: 1`n"; summary = 'yaml'; format = 'yaml' })
    # Hierarchy: child under parent; monorepo-style project name.
    (ToolCall 10 'vault_save' @{ name = 'parent-note'; project = 'fixture'; content = 'root'; summary = 'the parent' })
    (ToolCall 11 'vault_save' @{ name = 'child-note'; project = 'fixture'; content = 'leaf'; summary = 'the child'; parent = 'parent-note' })
    (ToolCall 12 'vault_save' @{ name = 'mono-note'; project = 'mono/app'; content = 'monorepo'; summary = 'slash project' })
    # Metadata-only update and an archived file.
    (ToolCall 13 'vault_set_meta' @{ name = 'multi-version'; project = 'fixture'; summary = 'live summary after set_meta'; tags = @('kind-b') })
    (ToolCall 14 'vault_save'    @{ name = 'archived-note'; project = 'fixture'; content = 'sleeping'; summary = 'archived' })
    (ToolCall 15 'vault_archive' @{ name = 'archived-note'; project = 'fixture' })
    # Multi-byte content (emoji + CJK) for encoding fidelity. Built from code points because
    # Windows PowerShell 5.1 reads BOM-less .ps1 files as ANSI and would mangle literals.
    (ToolCall 16 'vault_save' @{
        name = 'unicode-note'; project = 'fixture'
        content = 'emoji ' + [char]::ConvertFromUtf32(0x1F99D) + ' and ' `
            + [string][char]0x4E2D + [char]0x6587 + [char]0x30C6 + [char]0x30AD + [char]0x30B9 + [char]0x30C8 + "`n"
        summary = 'multi-byte content ' + [char]::ConvertFromUtf32(0x1F99D)
    })
)

# Run the server as a child process with redirected pipes so messages can be paced and
# stderr never trips PowerShell's NativeCommandError handling.
$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = $exe
$psi.UseShellExecute = $false
$psi.RedirectStandardInput = $true
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
$psi.StandardOutputEncoding = [System.Text.Encoding]::UTF8
$psi.EnvironmentVariables['VAULT_MCP_HOME'] = $fixtureDir

$process = [System.Diagnostics.Process]::Start($psi)
$stdout = $process.StandardOutput.ReadToEndAsync()
$stderr = $process.StandardError.ReadToEndAsync()

$writer = New-Object System.IO.StreamWriter($process.StandardInput.BaseStream, (New-Object System.Text.UTF8Encoding($false)))
foreach ($message in $messages) {
    $writer.WriteLine($message)
    $writer.Flush()
    Start-Sleep -Milliseconds 300
}
$writer.Close()

if (-not $process.WaitForExit(30000)) {
    $process.Kill()
    throw 'rust vault-mcp did not exit after stdin closed'
}

Write-Host "--- rust server responses ---"
Write-Host $stdout.Result
Write-Host "--- rust server stderr (log) tail ---"
Write-Host (($stderr.Result -split "`n" | Select-Object -Last 5) -join "`n")
Write-Host "--- fixture contents ---"
Get-ChildItem -Recurse $fixtureDir | Select-Object -ExpandProperty FullName
