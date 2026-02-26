HiokiParserMark1
================

Purpose
-------
`HiokiParserMark1` contains utilities and a small console entrypoint for parsing Hioki device logs and related data formats. It's intended as a lightweight toolset that can be used standalone or as part of the larger Hioki NL→SQL workflow.

How it works
------------
- The main application entry is in `Program.cs` and exposes parsing routines and helper utilities.
- Parsing code reads Hioki-formatted input and converts it to structured objects that other services (or tests) can consume.

Build & run
-----------
Build the project:

```bash
dotnet build HiokiParserMark1\HiokiParserMark1.csproj
```

Run from the project folder:

```bash
dotnet run --project HiokiParserMark1\HiokiParserMark1.csproj
```

Notes for developers
--------------------
- Target framework: .NET 8 (check `TargetFramework` in the project file).
- Keep parsing logic testable: prefer small, pure functions in `Services`/`Logic` so unit tests can cover behavior.

Where to look
-------------
- `Program.cs` — entrypoint and example usage.
- Any `Services` or `Logic` folders — core parsing routines.
