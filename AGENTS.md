# AGENTS.md

## Taskfile

Common dev commands are wrapped in `Taskfile.yml` (https://taskfile.dev). Run
`task --list` to see all of them; prefer these over raw `dotnet`/PowerShell
invocations:

- `task build` — build the app (Debug; pass `-- -c Release` for Release)
- `task run` — run the app (Debug)
- `task debug` — Debug build with full symbols, for attaching a debugger
- `task installer` — build the distributable installer via `Installer/build-installer.ps1`
- `task format` / `task lint` — auto-fix / check formatting with `dotnet format`
- `task test` — run unit tests (placeholder — no test project yet, see TODO.md)
- `task check` — the full CI gate locally: restore, lint, warnings-as-errors Release build
- `task clean` — remove `bin`, `obj`, `dist`
