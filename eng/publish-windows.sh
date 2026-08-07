#!/usr/bin/env bash
#
# Publish every MCP server for win-x64. Convenience wrapper over eng/publish.sh.
# Run, do not source: this execs the main script.
#
#   ./eng/publish-windows.sh                    # Release
#   CONFIGURATION=Debug ./eng/publish-windows.sh
#
set -euo pipefail
if [ "$#" -gt 0 ]; then
  echo "$(basename "$0") builds a fixed platform and takes no arguments; use CONFIGURATION=Debug for a debug build." >&2
  exit 2
fi
script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
exec bash "${script_dir}/publish.sh" win-x64
