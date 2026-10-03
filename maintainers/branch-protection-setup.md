# Branch protection and repository settings

The repository settings, the protection of `main` and its required checks are those of the polhem-dev organization's
baseline, [`repo-baseline.json`](https://github.com/polhem-dev/.github/blob/main/repo-baseline.json), audited and
applied with the `org-baseline` skill of the dev-workflow plugin. The
[organization's contributing guide](https://github.com/polhem-dev/.github/blob/main/CONTRIBUTING.md) explains why
they are what they are. This page records only what is specific to Polhem.JsonRpc.

Show the current rules with `gh api repos/polhem-dev/polhem-jsonrpc/branches/main/protection`.

## Required checks

- `build` and `aot` are jobs of `build-ci.yml`; `docs` is the job of `docs-check.yml`.
- `aot` has been required since 2026-10-03. With auto-merge on, a check that is not required cannot stop a pull
  request that fails it, and only running the published binary shows that the packages work under Native AOT.
- `sonarcloud` is not required.

## Documentation-only pull requests

The `pull_request` trigger of `build-ci.yml` has no `paths-ignore`; only its `push` trigger ignores `.md` files. A pull
request that changes only `.md` files is recognized inside each job: every job of `build-ci.yml` starts, skips its build
steps and reports success. The detection is a step of each job, not a job of its own: GitHub reports a job skipped by
`if:` as passing, so a failed detection job would let the pull request merge. The reasons are in the header of
`.github/scripts/detect-docs-only.sh`.
