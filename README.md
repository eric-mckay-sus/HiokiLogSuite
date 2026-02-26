
Hioki Parser Mark1 Solution
============================

Overview
--------
This repository contains the Hioki Parser solution and related projects used for parsing Hioki logs and converting user search queries into SQL for the Hioki dataset.

Top-level projects
------------------
- HiokiParserMark1 — A utility project providing parsing tools and helpers. See [HiokiParserMark1/README.md](HiokiParserMark1/README.md).
- HiokiNL2SQLMark1 — A Blazor-based UI and logic layer that converts natural-language-style queries into SQL and provides search UI components. See [HiokiNL2SQLMark1/README.md](HiokiNL2SQLMark1/README.md).
- HiokiNL2SQL.Tests — Unit and integration tests for the logic and services.

Quick start
-----------
Build the solution:

```bash
dotnet build
```

Run the web UI (HiokiNL2SQLMark1):

```bash
dotnet run --project HiokiNL2SQLMark1\HiokiNL2SQLMark1.csproj
```

Run tests:

```bash
dotnet test
```

Structure notes
---------------
- The UI project is under HiokiNL2SQLMark1 (Blazor components, `Services`, and `Logic`).
- The parser utilities are under HiokiParserMark1.
- Tests live under HiokiNL2SQL.Tests.

See the project-level READMEs linked above for details about each project and development tips.
