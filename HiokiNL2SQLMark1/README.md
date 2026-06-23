# HiokiNL2SQLMark1

A Blazor web application that converts natural-language queries into SQL and provides a UI for searching and browsing Hioki device logs.

## Functionality

- **Query translation**: Accepts user input (searches, filters) and translates them into SQL/LINQ queries
- **UI pages**: Fct, Group, Step, and PowerSearch pages for browsing and filtering log data
- **Data layer**: Connects to Hioki log database via Entity Framework (`LogDbContext`)

## Architecture

- `Components/Pages`: Blazor pages (Fct, Group, Step, PowerSearch)
- `Components/CommonComponents`: Reusable UI components
- `Services/`: Application services including `SearchParserService`, `NavService`, `JSService`
- `Logic/`: Query translation and business logic (e.g., `PowerSearchLogic`, `LogTableLogic`)
- `LogDbContext.cs`: Data models and database context

## How it works

1. User searches via the UI (PowerSearch or table filters)
2. `SearchParserService` parses the input
3. Logic layer converts it to a SQL/LINQ query
4. Query executes against the database and results display in the UI

## Run

```bash
dotnet run --project HiokiNL2SQLMark1\HiokiNL2SQLMark1.csproj
```

## Configuration

- `appsettings.json`: Default settings
- `appsettings.Development.json`: Development overrides
- `Properties/launchSettings.json`: Launch configuration
