#!/usr/bin/env bash
#
# Publish every MCP server for linux-x64. Convenience wrapper over eng/publish.sh.
# Run, do not source: this execs the main script.
#
#   ./eng/publish-linux.sh                    # Release
#   CONFIGURATION=Debug ./eng/publish-linux.sh
#
set -euo pipefail
if [ "$#" -gt 0 ]; then
  echo "$(basename "$0") builds a fixed platform and takes no arguments; use CONFIGURATION=Debug for a debug build." >&2
  exit 2
fi
script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
exec bash "${script_dir}/publish.sh" linux-x64
