HiokiNL2SQLMark1
================

Overview
--------
`HiokiNL2SQLMark1` is a Blazor-based application and supporting logic that turns user-friendly, natural-language-like queries into SQL against the Hioki log dataset. It also provides a UI for browsing and searching logs (pages such as Fct, Group, Step, and PowerSearch).

Architecture
------------
- Components: Blazor UI components live under `Components/Pages` and `Components/CommonComponents`.
- Services: Application services are in `Services/` (for example, `SearchParserService.cs`, `NavService.cs`, `JSService.cs`).
- Logic: Business logic and query translation live in `Logic/` (for example, `PowerSearchLogic.cs`, `LogTableLogic.cs`, `FctTableLogic.cs`).
- Data: `LogDbContext.cs` and `LogTableBase.cs` show how data access and models are structured.

How it works (high level)
-------------------------
1. User enters a search in the UI (PowerSearch or table filters).
2. `SearchParserService` parses the input and uses the `Logic` layer to convert it into a SQL expression or LINQ query.
3. The query is executed against the application's data layer (`LogDbContext`) and results are displayed in the UI components.

Run locally
-----------
From the repo root run:

```bash
dotnet run --project HiokiNL2SQLMark1\HiokiNL2SQLMark1.csproj
```

Configuration
-------------
- `appsettings.json` and `appsettings.Development.json` in the project root contain runtime configuration (logging, connection strings, etc.).
- Use the `Properties/launchSettings.json` for development launch configurations.

Testing
-------
Unit and integration tests for the translation and logic live in the sibling project `HiokiNL2SQL.Tests`. Run `dotnet test` from the solution root to execute tests.

Development notes
-----------------
- To add new parsing rules, extend `SearchParserService` and add the corresponding logic in `Logic/`.
- Keep the UI components thin — put translation and data concerns in `Services`/`Logic`.
