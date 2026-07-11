# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What This Is

AmIRite is a two-player web game where players answer personal questions, then try to identify which answers belong to their opponent (disguised among LLM-generated decoys). The app tracks scores, achievements, and generates an AI-written narrative of how well two players know each other.

**Stack**: C# 12 / .NET 10 minimal APIs, SQLite + Dapper, HTMX + SSE, Anthropic Claude for decoy/analysis generation, Firebase FCM + SMTP for notifications. Deployed to Fly.io via Docker.

## Commands

```bash
# Run locally (uses amirite-dev.db per appsettings.Development.json)
dotnet run --project src/AmIRite.Web

# Build
dotnet build

# Deploy (CI via GitHub Actions on push to main)
flyctl deploy --remote-only
```

No test project exists yet.

## Architecture

Single ASP.NET Core project at `src/AmIRite.Web/`. Entry point `Program.cs` registers all services and maps all routes.

**Layers**:
- `Routes/` — 6 modules, each calling `MapGet`/`MapPost` inline with DI. No separate controllers.
- `Services/` — 12 stateless services injected into routes. All DB access is here via Dapper + raw SQL.
- `Workers/` — 4 `BackgroundService` subclasses: `AchievementWorker` (off-thread eval), `JoinExpiryWorker` (cancel timed-out games), `SseHeartbeatWorker` (30s keepalive), `LlmRetryWorker` (retry failed decoy generation every 5min).
- `Data/` — Migration runner (`Database.cs`) applies embedded `.sql` files at startup. `DbConnectionFactory` wraps `SqliteConnection`.
- `Models/` — `Domain.cs` for entities, `Config.cs` for options classes bound from `appsettings.json`.

**Real-time**: `SseService` holds a `ConcurrentDictionary<token, connections>`. When game state changes (opponent answered, LLM ready), the server pushes HTML via SSE. FCM or email are fallbacks when SSE is not live. SSE handlers require `X-Accel-Buffering: no` and `await Task.Delay(Timeout.Infinite, ct)` — never `.Result`.

**Game state machine**: Session status: `pending_join → active → paused → finished`. Round status: `answering → guessing → complete`. Endpoints assert legal status before acting.

**LLM flow**: After both players submit answers, `RoundService` calls `LlmService.GenerateDecoysAsync()` (Anthropic API). On failure, session moves to `paused`; `LlmRetryWorker` retries. On success, SSE notifies opponent.

**Achievements**: Strategy pattern — `IAchievementEvaluator` implementations registered in DI, evaluated off-thread by `AchievementWorker`.

## Coding Conventions

These are strict and applied to all new code:

| Element | Convention | Example |
|---|---|---|
| Parameters | `p` prefix | `pSessionId`, `pEmail` |
| Locals | `t` prefix | `tRound`, `tDecoys` |
| Private fields | `_camelCase` | `_logger`, `_client` |
| Async methods | `Async` suffix | `CreateRoundAsync` |
| Namespaces | File-scoped | `namespace AmIRite.Web;` |
| Indentation | Tabs |  |
| Braces | Always, even single-line bodies |  |
| One type per file | Yes |  |

Nullable reference types enabled. Use `record` for DTOs. Primary constructors OK for service classes.

## Dapper / Database

- No EF Core. All queries use `conn.QueryAsync<T>()` / `conn.ExecuteAsync()` with parameterized SQL.
- `Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true` maps `snake_case` columns to `PascalCase` properties automatically.
- Schema: 19 tables. Core: `players`, `sessions`, `rounds`, `round_questions`, `answers`, `decoys`, `guesses`. Auth: `otp_codes`, `player_sessions`. Social: `chat_messages`, `broadcasts`, `achievements`, `player_achievements`.
- New schema changes go in a new numbered `.sql` file under `Data/Migrations/` as an embedded resource.

## Config & Secrets

All settings bind to strongly-typed options classes (`GameOptions`, `LlmOptions`, `EmailOptions`, `FcmOptions`, `AdminOptions`). Never hardcode config values — always inject the options class.

In production, secrets are set via `flyctl secrets set`. Keys: `Llm:AnthropicApiKey`, `Email:FromAddress`, `Email:AppPassword`, `Fcm:ProjectId`, `Fcm:CredentialsJson`, `Admin:Username`, `Admin:Password`.

## Spec Document

`doc/AmIRite-Spec.md` is the authoritative 1,000+ line game design spec. Consult it for feature requirements, scoring rules, achievement definitions, and UI behavior.
