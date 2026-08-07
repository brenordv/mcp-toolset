#!/usr/bin/env bash
#
# Publish every MCP server for osx-arm64 (Apple Silicon). Convenience wrapper over eng/publish.sh.
# For Intel Macs use publish-macos-x64.sh. Run, do not source: this execs the main script.
#
#   ./eng/publish-macos.sh                    # Release
#   CONFIGURATION=Debug ./eng/publish-macos.sh
#
set -euo pipefail
if [ "$#" -gt 0 ]; then
  echo "$(basename "$0") builds a fixed platform and takes no arguments; use CONFIGURATION=Debug for a debug build." >&2
  exit 2
fi
script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
exec bash "${script_dir}/publish.sh" osx-arm64
