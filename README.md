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

| Server      | Description                                                                                                                                                                                                    | Docs                                                          |
|-------------|----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|---------------------------------------------------------------|
| **git-ops** | Local, read-only Git inspection; status, history, diffs, blame, and search exposed as typed tools that return JSON. The assistant never drives `git` through a shell, and no writing subcommands are wired up. | [README](src/RaccoonNinja.McpToolset.Server.GitOps/README.md) |

> **Note:** The **git-ops** server is the first and currently the reference server in this toolset. See its
> [README](src/RaccoonNinja.McpToolset.Server.GitOps/README.md) for the full tool catalog, security model, and
> instructions for adding it to Claude Code (or any MCP client).

## Repository layout

```
RaccoonNinja.McpToolset/
├─ src/                       # one project per MCP server
│  └─ RaccoonNinja.McpToolset.Server.GitOps/
├─ tests/                     # matching test project per server
│  └─ RaccoonNinja.McpToolset.Server.GitOps.Tests/
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

## Continuous integration

Two GitHub Actions workflows live under [`.github/workflows`](.github/workflows):

- **QA** (`qa.yml`) — runs on every pull request and on pushes to `master`. It
  verifies formatting (`dotnet format --verify-no-changes`), builds with
  warnings treated as errors, and runs the unit tests. The build fails if any
  of these fail. It is also a reusable workflow, so the release pipeline can
  reuse it as a gate.
- **Publish** (`publish.yml`) — triggered by pushing a tag of the form
  `release/vX.Y.Z` (for example `release/v1.0.0`). It first re-runs QA as a
  hard gate, then cross-compiles a self-contained, single-file binary of each
  MCP server for `win-x64`, `linux-x64`, `osx-x64`, and `osx-arm64`, packages
  each as a named zip, and publishes them — together with a `SHA256SUMS.txt`
  manifest — in a single atomic GitHub release.

## Releases and verification

Each release attaches one zip per server and platform, named
`<ServerProject>-<version>-<rid>.zip`, plus a `SHA256SUMS.txt` manifest.

To check that a downloaded artifact is intact, verify its checksum against the
manifest:

```bash
sha256sum --check --ignore-missing SHA256SUMS.txt
```

> **Note on integrity vs. authenticity.** `SHA256SUMS.txt` is a *checksum*, not
> a cryptographic *signature*. It lets you detect accidental corruption of a
> download, but because the manifest is published alongside the artifacts,
> it does not by itself prove the artifacts were produced by this project.
> Tamper-evident signing (for example, Sigstore `cosign` keyless signing or
> GitHub build-provenance attestations) is a planned enhancement.
