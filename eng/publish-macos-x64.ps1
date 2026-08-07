#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Publish every MCP server for osx-x64 (Intel Macs). Convenience wrapper over eng/publish.ps1.
  For Apple Silicon use publish-macos.
#>
[CmdletBinding()]
param(
    [string] $Configuration = 'Release'
)
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'publish.ps1') -Rids 'osx-x64' -Configuration $Configuration
