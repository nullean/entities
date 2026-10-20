# Releasing

Packages are versioned from git tags by [MinVer](https://github.com/adamralph/minver), so a release is a tag. Everything else is a `./build.sh` target that CI runs too, so a release can be rehearsed on a laptop.

## What publishes when

| Event | What happens |
|---|---|
| Pull request | Build, test, pack and validate. Nothing is published. |
| Push to `main` | The same. Nothing is published: there is no canary feed (GitHub Packages was refused by the organisation's billing limit, and a tag is the only way a package reaches nuget.org). |
| Push of a tag `X.Y.Z` or `X.Y.Z-pre.N` | The same, then the public API changes are written, the ten packages are pushed to nuget.org, and the draft GitHub release is published for the tag with the API changes added to it. |

The release notes are drafted by [release-drafter](https://github.com/release-drafter/release-drafter) (`.github/release-drafter.yml`, `.github/workflows/release-drafter.yml`): every merge to `main` updates a draft release from the pull requests merged since the last release, grouped by label.

## Cutting a release

1. Give each pull request one category label so it lands in the notes: `breaking`, `feature` or `enhancement`, `bug` or `fix`, `documentation`, `chore` or `dependencies`, `automation` or `ci`. `changelog:skip` leaves a pull request out. The labels also pick the next version in the draft (`breaking` is major, `feature`/`enhancement` minor, anything else patch); the tag you push is what decides the version, so read the draft and tag accordingly.
2. Rehearse: `./build.sh release` on a clean `main`. It checks the working copy is clean, runs the tests on every target framework, packs, validates every package (version, strong-name key, all ten present), and writes `breaking-changes-<package>.md` (the public API changes since the previous nuget.org release) to `.artifacts/build-output`.
3. Tag and push: `git tag X.Y.Z && git push origin X.Y.Z`. For a first run of the pipeline, tag a prerelease (`0.1.0-preview.1`) before `0.1.0`: a version published to nuget.org can be unlisted but not deleted.

## Targets

| Target | Does |
|---|---|
| `test` | build, then every test project on every target framework |
| `pack` | test, then pack and validate the ten packages into `.artifacts/build-output` |
| `release` | clean-checkout check, `pack`, public API changes |

Add `-s true` to run one target without its dependencies (CI runs each step on its own), and `-c false` to skip the clean-checkout check.

## Things that are not obvious

- **Symbols are embedded** in each assembly (no `.snupkg`). SourceLink points at the commit on GitHub, and files generated at build time (the `[Constraints]` companions) are embedded because git does not have them.
- **The validator needs the newest runtime.** `nupkg-validator` loads each package's assembly, and a `net11.0` one references System.Runtime 11, so the build lets the tool roll forward to the newest installed runtime, previews included.
- **The API diff is skipped for a package that is not on nuget.org yet**, which is every package on the first release.
- **The strong-name key is committed** (`build/keys/keypair.snk`) so any build produces the same public key token. It identifies the assembly; it is not a secret.
- **Setup that lives outside the repository:** the `Nullean.Entities*` ID prefix reserved on nuget.org, the org-level NuGet key available to this repository, and the labels above (a label that does not exist yet is created by adding it to a pull request).
