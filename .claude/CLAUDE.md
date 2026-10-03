# Polhem.JsonRpc — guidance for coding agents

## Language

- Everything maintained together is written in **English**: source code, XML documentation, comments, test method
  names and their `DisplayName` text, commit messages, the maintainer documents under `maintainers/`, and the files under
  `.claude/`.
- This overrides any personal or user-level setting that asks for another language for prose. Replies in a
  conversation may still follow the user's language.
- User documents are bilingual (`name.md` and `name.zh-TW.md`); maintainer documents are English only.
  `CONTRIBUTING.md` says which is which.

## Project overview

JSON-RPC 2.0 for .NET on System.Text.Json. The core packages are `Polhem.JsonRpc` (shared message types and transport
abstraction), `Polhem.JsonRpc.Server` (dispatcher), `Polhem.JsonRpc.AspNetCore` (HTTP endpoint) and
`Polhem.JsonRpc.Client` (connector). The optional `Polhem.JsonRpc.Payload` packages add the payload envelope, its
encryption and replay protection; their bytes are a wire format other clients implement (ADR-002, decision 2). The
design and its reasons are in `maintainers/adr/`; read the relevant ADR before changing behavior it describes.

- **Version**: `src/Directory.Build.props`, the only place it is declared.
- **Dependencies**: the packages depend on nothing but .NET, plus ASP.NET Core for `Polhem.JsonRpc.AspNetCore`.
  Do not add a package reference without asking.
- **Downstream**: the Polhem framework (`polhem-dev/polhem`) builds its API on these packages, and the TypeScript client
  `polhem-dev/polhem-connector-js` speaks the same wire format. A change to the shape of a request, a response, an
  error or an error code changes what both of them see. Say so in the pull request, and arrange the follow-up there.

## Build and test

```bash
dotnet build Polhem.JsonRpc.slnx --configuration Release
dotnet test Polhem.JsonRpc.slnx --configuration Release --no-build
./check-md-links.sh
```

## Workflow

- How changes reach `main`, and who merges them, is the polhem-dev organization's contributing guide,
  <https://github.com/polhem-dev/.github/blob/main/CONTRIBUTING.md>: contributors work from a fork and open a pull
  request, and only the maintainer merges. `main` is protected, so nothing is pushed to it directly.
- Every change reaches `main` through a pull request. Agents name their branches `claude/<topic>`.
- Build and test locally before pushing when the environment allows it.
- **Never push a `v*` tag without the user's explicit consent.** It publishes the packages to nuget.org, and a
  published package cannot be withdrawn.
- Never change a test or the source code just to make a check pass.

## Local working documents

- `local/` at the repository root is ignored by git. Keep plans, drafts and personal notes there.
- Never commit anything under `local/`, never add it with `git add -f`, and never link to it or name a file in it from a
  committed file.
- Decisions of lasting value belong in `maintainers/adr/`; work other maintainers need to see belongs in a GitHub issue
  or pull request.
