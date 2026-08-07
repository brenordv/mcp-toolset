#!/usr/bin/env bash
#
# Publish every MCP server for osx-x64 (Intel Macs). Convenience wrapper over eng/publish.sh.
# For Apple Silicon use publish-macos.sh. Run, do not source: this execs the main script.
#
#   ./eng/publish-macos-x64.sh                    # Release
#   CONFIGURATION=Debug ./eng/publish-macos-x64.sh
#
set -euo pipefail
if [ "$#" -gt 0 ]; then
  echo "$(basename "$0") builds a fixed platform and takes no arguments; use CONFIGURATION=Debug for a debug build." >&2
  exit 2
fi
script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
exec bash "${script_dir}/publish.sh" osx-x64
