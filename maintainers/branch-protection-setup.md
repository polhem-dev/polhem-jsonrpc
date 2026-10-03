# Branch protection and repository settings

This document records how `main` of `polhem-dev/polhem-jsonrpc` is protected and which repository settings go with it.
Every change, including the maintainers', reaches `main` through a pull request.

## Branch protection on `main`

Set with the classic branch protection API on 2026-10-01:

| Setting | Value | Why |
|---------|-------|-----|
| `required_status_checks.contexts` | `["build", "docs"]` | The `build` job of `build-ci.yml` and the `docs` job of `docs-check.yml` must pass |
| `required_status_checks.strict` | `true` | The branch must be up to date with `main` before it merges |
| `enforce_admins` | `true` | The rules apply to administrators too |
| `required_pull_request_reviews.required_approving_review_count` | `0` | A pull request is required, but no approval: with a single maintainer, GitHub does not let authors approve their own |
| `allow_force_pushes` / `allow_deletions` | `false` | `main` cannot be rewritten or deleted |

Show the current rules with `gh api repos/polhem-dev/polhem-jsonrpc/branches/main/protection`.

The `aot` and `sonarcloud` jobs are not required checks. Making one required means adding its job name to
`contexts`; renaming a required job means changing the protection too, otherwise every pull request waits for a check
that no longer exists.

A required check must start on every pull request, so the `pull_request` trigger of `build-ci.yml` has no
`paths-ignore`; only its `push` trigger ignores `.md` files. A pull request that changes only `.md` files is recognized
inside each job instead: every job of `build-ci.yml` starts, skips its build steps and reports success. The detection
is a step of each job, not a job of its own: GitHub reports a job skipped by `if:` as passing, so a failed detection
job would let the pull request merge. The reasons are in the header of `.github/scripts/detect-docs-only.sh`.

## Repository settings

| Setting | Value |
|---------|-------|
| `allow_squash_merge` | `true`, the only merge method |
| `allow_merge_commit` / `allow_rebase_merge` | `false` |
| `delete_branch_on_merge` | `true` |
| `allow_auto_merge` | `true` |
