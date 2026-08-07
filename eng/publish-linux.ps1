#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Publish every MCP server for linux-x64. Convenience wrapper over eng/publish.ps1.
#>
[CmdletBinding()]
param(
    [string] $Configuration = 'Release'
)
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'publish.ps1') -Rids 'linux-x64' -Configuration $Configuration
