# Contributing to Polhem.JsonRpc

This guide describes how changes reach the repository and the conventions they follow.

## Before you start

- For a bug fix or a small improvement, open a pull request directly.
- For a larger change (a new feature, a change to public API, a new dependency), open an issue first so the approach
  can be agreed on before you spend time on it.
- Read the [architecture decision records](maintainers/adr/README.md) for why the design is the way it is.

## Workflow

1. Create a branch from the latest `main`, or fork the repository.
2. Make the change, with tests.
3. Build and test locally (see below).
4. Open a pull request against `main`. `main` only accepts changes through pull requests.

## Build and test

```bash
dotnet build Polhem.JsonRpc.slnx --configuration Release
dotnet test Polhem.JsonRpc.slnx --configuration Release --no-build
```

## Conventions

- Code style is enforced by `.editorconfig`, `src/Directory.Build.props` and `tests/Directory.Build.props`.
  `TreatWarningsAsErrors` turns every violation into a build error.
- Public API is tracked in each package's `PublicAPI.Shipped.txt` and `PublicAPI.Unshipped.txt`. A change to public API
  is declared in `PublicAPI.Unshipped.txt`; say in the pull request whether it is binary compatible.
- The packages depend on nothing but .NET (and ASP.NET Core for `Polhem.JsonRpc.AspNetCore`). A new package reference
  needs an issue first.

## Documents

Documents are split by reader:

- **User documents** (for developers who use the packages): `README.md`, `CHANGELOG.md` and the README of each sample.
  They are bilingual: `name.md` is English and `name.zh-TW.md` is Traditional Chinese, each with a language switch at
  the top. Change both files in the same pull request.
- **Maintainer documents** (this file, `maintainers/`, the ADRs in `maintainers/adr/`): English only.
- The README next to each package's `.csproj` is the nuget.org page of that package, in English only.

## Releases

Pushing a `v*` tag runs `.github/workflows/nuget-publish.yml`, which publishes the packages to nuget.org. A published
package cannot be withdrawn, so tags are pushed by a maintainer only.
