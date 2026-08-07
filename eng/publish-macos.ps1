#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Publish every MCP server for osx-arm64 (Apple Silicon). Convenience wrapper over eng/publish.ps1.
  For Intel Macs use publish-macos-x64.
#>
[CmdletBinding()]
param(
    [string] $Configuration = 'Release'
)
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'publish.ps1') -Rids 'osx-arm64' -Configuration $Configuration
