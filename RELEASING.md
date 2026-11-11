# Releasing

Packages are versioned from git tags by [MinVer](https://github.com/adamralph/minver). The tag comes from publishing the draft release that [release-drafter](https://github.com/release-drafter/release-drafter) keeps; nobody pushes a tag by hand. Everything the release job builds is a `./build.sh` target that CI runs too, so a release can be rehearsed on a laptop.

## What publishes when

| Event | What happens |
|---|---|
| Pull request | Build, test, pack and validate. Nothing is published. |
| Push to `main` | The same. Nothing is published: there is no canary feed (GitHub Packages was refused by the organisation's billing limit, and a published release is the only way a package reaches nuget.org). release-drafter updates the draft release. |
| Publishing the draft release | GitHub creates its tag, which fires `release: published` on `.github/workflows/ci.yml`. Every job runs again on the tagged commit; then the `release` job (in the `nuget` environment) writes the public API changes, pushes every package to nuget.org and appends the API changes to the release notes. |
| Push of a tag on its own | Nothing. |

The release notes are drafted by release-drafter (`.github/release-drafter.yml`, `.github/workflows/release-drafter.yml`): every merge to `main` updates a draft release from the pull requests merged since the last release, grouped by label. The release runs from `ci.yml` because the trusted publishing policy on nuget.org names that file and the `nuget` environment.

## Cutting a release

1. Give each pull request exactly one release label (the `pr-labels` check fails a pull request without one): `breaking`, `feature` or `enhancement`, `bug` or `fix`, `documentation`, `chore` or `dependencies`, `ci`. `changelog:skip` leaves a pull request out of the notes. The labels pick the next version in the draft: while the version is 0.x, `breaking`, `feature` and `enhancement` bump the minor version and anything else the patch. Going to 1.0 is a maintainer's decision, made by editing the draft's tag when publishing (see the [breaking-changes](.claude/skills/breaking-changes/SKILL.md) skill).
2. Rehearse: `./build.sh release` on a clean `main`. It checks the working copy is clean, runs the tests on every target framework, packs, validates every package (version, strong-name key, every package in `build/scripts/Paths.fs` present), and writes `breaking-changes-<package>.md` (the public API changes since the previous nuget.org release) to `.artifacts/build-output`.
3. Read the draft: `mise run release`. Publish it with `mise run release publish`, or from the releases page on github.com. To release a prerelease, edit the draft's tag to `X.Y.Z-pre.N` and tick "Set as a pre-release" before publishing: a version published to nuget.org can be unlisted but not deleted.
4. Follow the run: `gh run list --workflow ci.yml --event release --limit 1`. A failed run can be re-run: the push uses `--skip-duplicate` and the API changes are appended once. See the [releasing](.claude/skills/releasing/SKILL.md) skill.

## Targets

| Target | Does |
|---|---|
| `test` | build, then every test project on every target framework |
| `pack` | test, then pack and validate every package into `.artifacts/build-output` |
| `release` | clean-checkout check, `pack`, public API changes |

Add `-s true` to run one target without its dependencies (CI runs each step on its own), and `-c false` to skip the clean-checkout check.

## Things that are not obvious

- **Symbols are embedded** in each assembly (no `.snupkg`). SourceLink points at the commit on GitHub, and files generated at build time (the `[Constraints]` companions) are embedded because git does not have them.
- **The validator needs the newest runtime.** `nupkg-validator` loads each package's assembly, and a `net11.0` one references System.Runtime 11, so the build lets the tool roll forward to the newest installed runtime, previews included.
- **The API diff is skipped for a package that is not on nuget.org yet**, which is every package on the first release.
- **The strong-name key is committed** (`build/keys/keypair.snk`) so any build produces the same public key token. It identifies the assembly; it is not a secret.
- **Setup that lives outside the repository:** the `Nullean.Entities*` ID prefix reserved on nuget.org, the trusted publishing policy that names `ci.yml` and the `nuget` environment, the org-level `NUGET_USER` secret, and the labels above.
