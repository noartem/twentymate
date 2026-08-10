## TODO

- [ ] Taskfile - for build, installer build, run, debug, tests/checks/linters
- [x] CI&CD in Github Actions
  - Testing — build + `dotnet format --verify-no-changes` (no unit tests yet)
  - Installer build
- [ ] Unit tests for Core/* (e.g. BreakScheduler) — will require extracting
      state out of DispatcherTimer into a testable class
- [ ] i18n, support for top-5 languages and Russian
- [x] add mouse/keyboard tracker for auto stop/start - extra setting
