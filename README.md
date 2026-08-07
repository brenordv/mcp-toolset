# RaccoonNinja MCP Toolset

A collection of simple, cross-platform [Model Context Protocol](https://modelcontextprotocol.io) (MCP) servers that run **locally** on your machine.

Each server is a small, self-contained .NET console app that speaks MCP over stdio. They're built to do one job well,
hand structured (typed JSON) results back to the assistant, and keep your data on your machine: no cloud calls, no
network round-trips beyond the local process the assistant already talks to.

## Goals

- **Local-first.** Servers run as a local subprocess of your MCP client. Nothing is sent anywhere by these tools.
- **Cross-platform.** Targets .NET 10, so the same server runs on Windows, macOS, and Linux.
- **Simple and focused.** Each server wraps a single domain in a small set of typed tools rather than one do-everything endpoint.
- **Safe by default.** Untrusted input is validated, scope is confined, and servers expose only what they advertise.

## Servers

- **[git-ops](src/RaccoonNinja.McpToolset.Server.GitOps/README.md)**: Local, read-only Git inspection; status, history, diffs, blame, and search exposed as typed tools that return JSON. The assistant never drives `git` through a shell, and no writing subcommands are wired up.
- **[file-vault](src/RaccoonNinja.McpToolset.Server.FileVault/README.md)**: A personal, cross-conversation file vault: versioned notes in a local SQLite + snapshot store, with optimistic concurrency, tags, full-text search, hierarchy, and structure-aware markdown/JSON/YAML edits. Drop-in port of the Rust `vault-mcp` server, same on-disk store.
- **[text-search](src/RaccoonNinja.McpToolset.Server.TextSearch/README.md)**: Local, read-only, root-confined text search and inspection; `describe_scope`, `find_files`, `inspect_files`, `search_text`, and `read_lines` as typed tools that replace the `find`/`grep`/`cat` habit. Every path is resolved through all symlinks and confined to its configured roots, a non-overridable denylist keeps secret files unread while content-based detection additionally withholds files whose contents match a known secret shape, and results stay root-relative so no absolute path from your machine reaches the model. Supports multiple named roots plus opt-in package roots for grepping cached dependency sources.
- **[text-edit](src/RaccoonNinja.McpToolset.Server.TextEdit/README.md)**: The mutating counterpart to text-search: root-confined text edits (`normalize_files`, `replace_text`) with hash-gated undo (`list_recent_batches`, `undo_batch`/`undo_last_batch`). It points at one repository, keeps its write tools on prompt, refuses secret files via the same non-overridable denylist, round-trips encodings and line endings faithfully, and journals every change to an append-only store sited outside the root so a batch can be rolled back even after a mid-batch crash.

## Repository layout

```
RaccoonNinja.McpToolset/
├─ src/
│  ├─ RaccoonNinja.McpToolset.Server.GitOps/       # MCP server
│  ├─ RaccoonNinja.McpToolset.Server.FileVault/    # MCP server
│  ├─ RaccoonNinja.McpToolset.Server.TextSearch/   # MCP server
│  ├─ RaccoonNinja.McpToolset.Server.TextEdit/     # MCP server
│  └─ RaccoonNinja.McpToolset.Files/               # shared library: confinement, denylist, selection, encoding
├─ tests/                     # matching test project per src project
│  ├─ RaccoonNinja.McpToolset.Server.GitOps.Tests/
│  ├─ RaccoonNinja.McpToolset.Server.FileVault.Tests/
│  ├─ RaccoonNinja.McpToolset.Server.TextSearch.Tests/
│  ├─ RaccoonNinja.McpToolset.Server.TextEdit.Tests/
│  └─ RaccoonNinja.McpToolset.Files.Tests/
├─ eng/                       # publish settings (ServerPublish.props) + local publish.ps1
├─ Directory.Build.props      # shared build settings (net10.0, analyzers, etc.)
├─ Directory.Packages.props   # central package version management
└─ RaccoonNinja.McpToolset.slnx
```

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download) to build, or the .NET 10 runtime to run a published server.
- Any individual server may have its own prerequisites (for example, **git-ops** needs `git` on `PATH`). See each
  server's README.

## Building

```bash
dotnet build RaccoonNinja.McpToolset.slnx
dotnet test  RaccoonNinja.McpToolset.slnx
```

To build self-contained, single-file executables locally (the same shape the release workflow ships), run the publish script for your shell:

```bash
# PowerShell (Windows, or pwsh on any platform)
./eng/publish.ps1                 # win-x64, linux-x64, osx-arm64 -> dist/<rid>/
./eng/publish.ps1 -Rids win-x64   # a single platform

# Bash (Ubuntu / macOS)
./eng/publish.sh                  # win-x64, linux-x64, osx-arm64 -> dist/<rid>/
./eng/publish.sh win-x64          # a single platform
```

To publish a single platform without remembering its RID, use a per-platform convenience wrapper (one per shell). Each delegates to the main script above:

```bash
# PowerShell                     # Bash
./eng/publish-windows.ps1        ./eng/publish-windows.sh      # win-x64
./eng/publish-linux.ps1          ./eng/publish-linux.sh        # linux-x64
./eng/publish-macos.ps1          ./eng/publish-macos.sh        # osx-arm64 (Apple Silicon)
./eng/publish-macos-x64.ps1      ./eng/publish-macos-x64.sh    # osx-x64 (Intel)
```

The bash wrappers also work as `bash eng/publish-linux.sh` if the executable bit is not set. Configuration passes through the same way as the main scripts (`-Configuration Debug` for PowerShell, `CONFIGURATION=Debug` for bash).

Each server becomes one self-contained executable: the .NET runtime and the native SQLite/BLAKE3 libraries are embedded, so there is nothing else to install to run it. The single-file settings live in `eng/ServerPublish.props`.

## Continuous integration

Two GitHub Actions workflows live under [`.github/workflows`](.github/workflows):

- **QA** (`qa.yml`): runs on every pull request and on pushes to `master`. It
  verifies formatting (`dotnet format --verify-no-changes`), builds with
  warnings treated as errors, and runs the unit tests. The build fails if any
  of these fail. It is also a reusable workflow, so the release pipeline can
  reuse it as a gate.
- **Publish** (`publish.yml`): triggered by pushing a tag of the form
  `release/vX.Y.Z` (for example `release/v1.0.0`). It first re-runs QA as a
  hard gate, then cross-compiles each MCP server into a self-contained,
  single-file executable (the .NET runtime and native libraries are embedded)
  for `win-x64`, `linux-x64`, `osx-x64`, and `osx-arm64`, packages each as a
  named zip, and publishes them, together with a `SHA256SUMS.txt` manifest, in
  a single atomic GitHub release.

## Releases and verification

Each release attaches one zip per server and platform, named
`<ServerProject>-<version>-<rid>.zip`, plus a `SHA256SUMS.txt` manifest.

To check that a downloaded artifact is intact, verify its checksum against the
manifest:

```bash
sha256sum --check --ignore-missing SHA256SUMS.txt
```

> **Note on integrity vs. authenticity.** `SHA256SUMS.txt` is a *checksum*, not a cryptographic *signature*. 
> It lets you detect accidental corruption of a download, but because the manifest is published alongside the artifacts,
> it does not by itself prove the artifacts were produced by this project.
> We are using GitHub build-provenance attestation as a tamper-evident signing.

## Recommended Claude configuration
To make it easier, you can allow all agents to use the mcp servers here, by adding the following to your `~/.claude/settings.json`:

```json
{
  "permissions": {
    "allow": [
      "mcp__git-ops",
      "mcp__vault",
      "mcp__text-search"
    ]
  }
}
```
This will allow the agents to use the mcp servers autonomously, without having to ask you for permission every time.

The read-only servers above are safe to blanket-approve. **text-edit is deliberately left out**: it writes to
your files, so keep its tools on prompt rather than auto-approving them. If you want its read-only tools
approved while its write tools still prompt, allow only `mcp__text-edit__describe_scope` and
`mcp__text-edit__list_recent_batches`.
