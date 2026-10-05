# Contributing to Polhem.JsonRpc

How changes reach the repository, and who merges them, is in the
[contributing guide of the polhem-dev organization](https://github.com/polhem-dev/.github/blob/main/CONTRIBUTING.md).
This page adds what is specific to this repository.

Read the [architecture decision records](maintainers/adr/README.md) for why the design is the way it is.

## Build and test

```bash
dotnet build Polhem.JsonRpc.slnx --configuration Release
dotnet test Polhem.JsonRpc.slnx --configuration Release --no-build
./check-md-links.sh
```

## Conventions

- Code style is enforced by `.editorconfig`, `src/Directory.Build.props` and `tests/Directory.Build.props`.
  `TreatWarningsAsErrors` turns every violation into a build error.
- Public API is tracked in each package's `PublicAPI.Shipped.txt` and `PublicAPI.Unshipped.txt`. A change to public API
  is declared in `PublicAPI.Unshipped.txt`; say in the pull request whether it is binary compatible.
- The packages depend on nothing but .NET (and ASP.NET Core for `Polhem.JsonRpc.AspNetCore`). A new package reference
  needs an issue first.
- The C# snippets of the READMEs are compiled in `tests/Polhem.JsonRpc.ReadmeSnippets`, one region per snippet.
  Change a snippet there and in the README together: the build compiles the region, and `ReadmeSnippetTests` fails when
  a README no longer shows its region's code, or a translated README shows other code than the English one. CI runs
  both on a pull request that changes only a README, when that README has a C# snippet
  (`.github/scripts/detect-docs-only.sh`).
- A test method is named `<Method>_<Scenario>_<Expected>` and says what it checks in the `DisplayName` of its `[Fact]`
  or `[Theory]`, which is what xUnit reports. (`System.ComponentModel.DisplayNameAttribute` is not read by xUnit.)
  `TestConventionTests` fails the build's test run when a test breaks either rule.

## Documents

Documents are split by reader:

- **User documents** (for developers who use the packages): `README.md`, `CHANGELOG.md`, the documents under `docs/` and the README of each sample.
  They are bilingual: `name.md` is English and `name.zh-TW.md` is Traditional Chinese, each with a language switch at
  the top. Change both files in the same pull request.
- **Maintainer documents** (this file, `maintainers/`, the ADRs in `maintainers/adr/`): English only.
- The README next to each package's `.csproj` is the nuget.org page of that package, in English only.

## Releases

Pushing a `v*` tag runs `.github/workflows/nuget-publish.yml`, which publishes the packages to nuget.org. A published
package cannot be withdrawn, so tags are pushed by a maintainer only.
