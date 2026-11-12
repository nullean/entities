#!/usr/bin/env bash
# Prepares what the terminal scenes (06-live, 07-everywhere) run in: a git worktree of this repository at HEAD,
# built once, so a take shows an incremental build rather than a cold one. Run it before recording; run it again to
# reset the worktree, so every take starts from the same clean tree.
set -euo pipefail

root="$(git -C "$(dirname "$0")" rev-parse --show-toplevel)"
live="${REEL_LIVE:-$HOME/code/wt/entities/reel-live}"

if [ -d "$live/.git" ] || [ -f "$live/.git" ]; then
	git -C "$live" checkout --detach --quiet "$(git -C "$root" rev-parse HEAD)"
	git -C "$live" checkout --quiet -- .
	git -C "$live" clean -fdq -e .artifacts
else
	mkdir -p "$(dirname "$live")"
	git -C "$root" worktree add --detach "$live" HEAD
fi

# The bug scene 06 plants, where its terminal can copy it from without a long path: .artifacts is ignored by git, so
# a reset leaves it alone.
mkdir -p "$live/.artifacts/planted"
cp "$root/reels/code/Order.cancel-bug.cs" "$live/.artifacts/planted/Order.cs"

cd "$live"
dotnet build tests/Nullean.Entities.Quickstart.Tests -f net10.0 -v quiet -nologo
dotnet build examples/Apps/OrderFulfilment -f net11.0 -v quiet -nologo
echo "ready: $live"
