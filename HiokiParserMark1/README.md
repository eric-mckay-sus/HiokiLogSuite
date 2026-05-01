# HiokiParserMark1

Utilities for parsing Hioki device logs and converting them into structured objects.

## Functionality

- Reads Hioki-formatted input files and data streams
- Converts raw log data into typed objects for consumption by services and tests
- Provides a console entrypoint in `LogParserCore.cs` for standalone usage
- Provides reusable functions used in parsing, but not part of the parsing itself, in `LogParserUtilities.cs`

## Run

```bash
dotnet run --project HiokiParserMark1\HiokiParserMark1.csproj
```
