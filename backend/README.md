# Conduit API (ASP.NET Core + SQL Server)

RealWorld backend for this Angular app. Users, articles, comments, tags, favorites and follows
are stored in SQL Server. Passwords are hashed with ASP.NET Core Identity's PBKDF2 `PasswordHasher`,
and the API issues HS256 JWTs (`Authorization: Token <jwt>`).

## Requirements

- .NET 8 SDK
- SQL Server (LocalDB, Express, Developer or Docker)

## Run

```bash
cd backend/ConduitApi
dotnet run
```

The API listens on http://localhost:3000. The `ConduitDb` database and its tables are created on first start.

The default connection string uses Windows authentication against the `localhost\SQLEXPRESS` instance.
Override it with an environment variable for another instance or SQL authentication, for example:

```bash
ConnectionStrings__Default="Server=localhost;Database=ConduitDb;User Id=sa;Password=<password>;TrustServerCertificate=True" dotnet run
```

`appsettings.Development.json` contains a development-only JWT key. Outside Development, set `Jwt__Key`
to a random secret of at least 32 bytes.

## Frontend

`bun run start` proxies `/api` to http://localhost:3000 (see `proxy.conf.json`), so start the API first.

## API tests

Requires Hurl 7+:

```bash
cd realworld/specs/api
HOST=http://localhost:3000 ./run-api-tests-hurl.sh
```
