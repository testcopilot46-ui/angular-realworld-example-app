---
name: pr-check
description: Pre-merge checklist — format, unit tests, git status, and recent commits summary
allowed-tools:
  - exec
  - read
permissions:
  allow:
    - Exec(bun run format:check)
    - Exec(bun run format)
    - Exec(bun run test)
    - Exec(git status)
    - Exec(git log)
    - Exec(git diff)
---

Run the pre-merge checklist for the current branch and report pass/fail for each step. Stop and explain if anything fails.

## Steps

1. **Formatting** — run `bun run format:check`.
   - If it fails, run `bun run format`, then re-check. Report whether formatting was applied.

2. **Unit tests** — run `bun run test` (Vitest).
   - All test files must pass. Report the pass count.

3. **Backend build** — if `backend/` has changes (`git status` / `git diff --name-only` shows `backend/**`), run `dotnet build` in `backend/ConduitApi` and confirm 0 errors.
   - Note: if the API is currently running, the build may fail to copy `ConduitApi.exe` due to the file lock — that is not a code error; compile errors (CS####) are what matter.

4. **Working tree** — run `git status` and `git log --oneline fork/main..HEAD`.
   - List uncommitted changes and summarize the commits that will be merged.

5. **Summary** — report a table: Formatting / Tests / Backend build / Uncommitted changes, each ✅ or ❌ with details.
