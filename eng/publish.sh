#!/usr/bin/env bash
#
# Publish each MCP server as a self-contained, single-file executable, one build per RID.
# The bash counterpart of eng/publish.ps1, for Ubuntu / macOS hosts without PowerShell.
#
# Output goes to dist/<rid>/<server>. The single-file / self-contained settings live in
# eng/ServerPublish.props and engage because this script passes a runtime identifier; a plain
# `dotnet build` / `dotnet test` is unaffected. Cross-RID publishes build on any host but can
# only be run on their own OS.
#
# Usage:
#   ./eng/publish.sh                       # win-x64, linux-x64, osx-arm64
#   ./eng/publish.sh linux-x64 osx-arm64   # explicit RIDs
#   CONFIGURATION=Debug ./eng/publish.sh linux-x64
#
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
dist_root="${repo_root}/dist"
configuration="${CONFIGURATION:-Release}"

if [ "$#" -gt 0 ]; then
  rids=("$@")
else
  rids=(win-x64 linux-x64 osx-arm64)
fi

servers=(
  src/RaccoonNinja.McpToolset.Server.GitOps/RaccoonNinja.McpToolset.Server.GitOps.csproj
  src/RaccoonNinja.McpToolset.Server.FileVault/RaccoonNinja.McpToolset.Server.FileVault.csproj
  src/RaccoonNinja.McpToolset.Server.TextSearch/RaccoonNinja.McpToolset.Server.TextSearch.csproj
  src/RaccoonNinja.McpToolset.Server.TextEdit/RaccoonNinja.McpToolset.Server.TextEdit.csproj
)

for rid in "${rids[@]}"; do
  out_dir="${dist_root}/${rid}"
  for server in "${servers[@]}"; do
    name="$(basename "$(dirname "$server")")"
    echo "publish ${name} -> ${rid}"
    dotnet publish "${repo_root}/${server}" \
      --configuration "$configuration" \
      --runtime "$rid" \
      --output "$out_dir" \
      --nologo
  done
done

echo ""
echo "Published artifacts:"
# wc -c is portable across GNU (Linux) and BSD (macOS) coreutils.
find "$dist_root" -type f | sort | while IFS= read -r file; do
  mb="$(awk "BEGIN { printf \"%.1f\", $(wc -c < "$file") / 1048576 }")"
  printf '%8s MB  %s\n' "$mb" "${file#"${repo_root}/"}"
done
