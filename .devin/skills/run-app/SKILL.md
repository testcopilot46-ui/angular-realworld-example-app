---
name: run-app
description: Start the ConduitApi backend and Angular frontend dev servers, then verify both are healthy
allowed-tools:
  - exec
permissions:
  allow:
    - Exec(dotnet run)
    - Exec(bun run start)
    - Exec(curl)
---

Start both servers for local development and verify they are healthy.

## Steps

1. **Backend** — in `backend/ConduitApi`, run `dotnet run` as a background process.
   - Wait for "Now listening on: http://localhost:3000" in the output.
   - Verify with `curl -s http://localhost:3000/api/articles` — expect JSON with `articles` and `articlesCount`.

2. **Frontend** — in the repo root, run `bun run start` as a background process.
   - Wait for "Application bundle generation complete" and the `http://localhost:4200/` URL in the output.
   - Verify with `curl -s -o /dev/null -w "%{http_code}" http://localhost:4200` — expect `200`.

3. **Report** — tell the user both URLs and that the API proxy (`/api` → `localhost:3000`) is active.

## Notes

- If port 3000 or 4200 is already in use, check whether the server is already running before starting a duplicate — `curl` the health endpoints first.
- If the backend fails with a SQL Server connection error, the `ConduitDb` database on `localhost\SQLEXPRESS` (see `backend/ConduitApi/appsettings.json`) must be reachable; `EnsureCreated()` in `Program.cs` creates the schema on first start.
- Do NOT stop the servers when done — leave them running for the user.
