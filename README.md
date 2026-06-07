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
