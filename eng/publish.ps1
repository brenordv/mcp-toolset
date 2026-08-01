#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Publish each MCP server as a self-contained, single-file executable, one build per RID.

.DESCRIPTION
  Output goes to dist/<rid>/<server>[.exe]. The single-file / self-contained settings live in
  eng/ServerPublish.props and engage because this script passes a RuntimeIdentifier; a normal
  `dotnet build` or `dotnet test` is unaffected. Cross-RID publishes build on any host but can only
  be run on their own OS.

.EXAMPLE
  ./eng/publish.ps1
  ./eng/publish.ps1 -Rids win-x64
#>
[CmdletBinding()]
param(
    [string[]] $Rids = @('win-x64', 'linux-x64', 'osx-arm64'),
    [string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$distRoot = Join-Path $repoRoot 'dist'

$servers = @(
    'src/RaccoonNinja.McpToolset.Server.GitOps/RaccoonNinja.McpToolset.Server.GitOps.csproj'
    'src/RaccoonNinja.McpToolset.Server.FileVault/RaccoonNinja.McpToolset.Server.FileVault.csproj'
    'src/RaccoonNinja.McpToolset.Server.TextSearch/RaccoonNinja.McpToolset.Server.TextSearch.csproj'
    'src/RaccoonNinja.McpToolset.Server.TextEdit/RaccoonNinja.McpToolset.Server.TextEdit.csproj'
)

foreach ($rid in $Rids) {
    $outDir = Join-Path $distRoot $rid
    foreach ($server in $servers) {
        $name = [IO.Path]::GetFileNameWithoutExtension($server)
        Write-Host "publish $name -> $rid" -ForegroundColor Cyan
        dotnet publish (Join-Path $repoRoot $server) -c $Configuration -r $rid -o $outDir --nologo
        if ($LASTEXITCODE -ne 0) { throw "publish failed: $server ($rid)" }
    }
}

Write-Host "`nPublished artifacts:" -ForegroundColor Green
Get-ChildItem $distRoot -Recurse -File | Sort-Object FullName | ForEach-Object {
    $rel = $_.FullName.Substring($repoRoot.Length).TrimStart('\', '/')
    "{0,8:N1} MB  {1}" -f ($_.Length / 1MB), $rel
}
