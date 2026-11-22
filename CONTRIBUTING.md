# Contributing

## Build and test

Install the SDK selected by `global.json` and a .NET 10 targeting pack (the libraries multi-target `net10.0` and
`net11.0`), then run from the repository root:

```powershell
dotnet format .\Yadm.slnx --verify-no-changes --no-restore
dotnet build .\Yadm.slnx
dotnet test --solution .\Yadm.slnx
```

Tests run on Microsoft.Testing.Platform with xUnit.net v3, so `dotnet run --project .\tests\Yadm.Test`
also works. Add `--coverlet` to `dotnet test` to collect coverage into `TestResults\`.

`Directory.Build.props` holds the shared build policy (nullable, analyzers, warnings as errors) and `.editorconfig`
the code style; both are enforced by the build and CI.

## Benchmarks

`benchmarks\` compares Yadm with FluentValidation plus Mapperly on an order import and a flat registration request:

```powershell
dotnet run --project .\benchmarks\Yadm.OrderImport.Benchmarks -c Release -- --filter '*'
```

Use `--filter '*RegistrationBenchmarks*'` for the registration comparison only.

## Packaging

```powershell
dotnet pack .\src\Yadm\Yadm.csproj -c Release
dotnet pack .\src\Yadm.SystemTextJson\Yadm.SystemTextJson.csproj -c Release
```

## Continuous integration and releases

`ci.yml` runs on pull requests and `main`: format check, Release build, tests with coverage, a vulnerability audit
and package validation. Label a PR `ci:benchmark` for an advisory benchmark comment.

Releases are tag-driven and versioned by MinVer: `git tag vX.Y.Z && git push --tags` on `main`. The release
workflow tests, packs and publishes to GitHub Packages by default; set the `NUGET_SOURCE` variable and
`NUGET_API_KEY` secret to publish to nuget.org.
