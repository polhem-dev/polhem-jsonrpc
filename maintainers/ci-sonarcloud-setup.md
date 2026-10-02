# SonarCloud setup

This document records how `polhem-dev/polhem-jsonrpc` is connected to SonarCloud. The analysis steps live in the
`sonarcloud` job of [build-ci.yml](../.github/workflows/build-ci.yml) and the file-level settings in
[SonarQube.Analysis.xml](../SonarQube.Analysis.xml); this document does not copy them.

## Identifiers

| Item | Value |
|------|-------|
| Organization | `polhem-dev`, bound to the GitHub organization of the same name |
| Project key | `polhem-dev_polhem-jsonrpc` |
| Quality gate | The organization default (built-in **Sonar way**) |
| Analysis method | CI-based; Automatic Analysis is off |

The organization and project key are passed on the command line of the `SonarScanner begin` step (`/o:`, `/k:`). The
same key appears in the README badges (both languages).

## When the analysis runs

On every push to `main` and every pull request, in its own job, so that the `build` job stays the gate for
`TreatWarningsAsErrors` (the scanner turns warnings back into warnings). The job skips itself when the `SONAR_TOKEN`
secret is not available, as on pull requests from forks. Coverage comes from the collector of the test SDK
(`--collect "Code Coverage;Format=xml"`).

## One-time setup

1. **Project.** The SonarQube Cloud GitHub App is installed on the `polhem-dev` organization for all repositories, so
   the project appeared in SonarCloud when the GitHub repository was created.
2. **Automatic Analysis must be off** (project → Administration → Analysis Method). Otherwise the upload from CI is
   rejected with *"You are running CI analysis while Automatic Analysis is enabled"*. Check it with
   `curl -s "https://sonarcloud.io/api/settings/values?component=polhem-dev_polhem-jsonrpc&keys=sonar.autoscan.enabled"`.
   On 2026-10-02 it was found switched on again after the first merge to `main`, although it had been off the day
   before, and the first CI analysis failed until it was switched off. Check it again if the job fails that way.
3. **Token.** A maintainer generates a token in SonarCloud (avatar → My Account → Security) and stores it as the
   repository secret `SONAR_TOKEN`: `gh secret set SONAR_TOKEN --repo polhem-dev/polhem-jsonrpc`.

## Ignored rules

Rules ignored for part of the repository are declared, with the reason, in `SonarQube.Analysis.xml`
(`sonar.issue.ignore.multicriteria`), not in the SonarCloud UI, so that they are reviewed with the code.
