---
title: Architecture
description: SmartData at 30,000 feet — projects, layers, and how they fit together.
---

> TODO — adapt from `CLAUDE.md` "Architecture" section and `docs/SmartData.Guide.md`.

Project layering (all `net10.0`):

- **SmartData.Core** — binary RPC serialization, shared protocol models
- **SmartData.Contracts** — shared contracts, provider interfaces
- **SmartData.Client** — HTTP client for `POST /rpc`
- **SmartData.Server** — engine: AutoRepo ORM, stored procedure framework, scheduler, session, metrics, backups
- **SmartData.Server.Sqlite / .SqlServer** — database providers
- **SmartData.Console** — embedded admin UI
- **SmartData.Cli** — `sd` command-line tool
