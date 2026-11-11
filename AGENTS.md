# Agent Instructions

- Keep changes focused and follow existing API and error-handling patterns.
- For C# changes, run `dotnet format PdfDotNet.slnx` before finishing.
- Run `dotnet test --project tests/PdfDotNet.Tests/PdfDotNet.Tests.csproj` after code or test changes.
- Keep `testassets/TestFont.ttf` optional and local. Do not add third-party font files to source control.
- The pre-commit hook formats staged C# files and stages its formatting changes. It stops if those files also have unstaged edits.
- Enable the tracked hook in each clone with `git config core.hooksPath .githooks`.
