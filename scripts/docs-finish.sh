#!/usr/bin/env bash
# What the docs build does after docs-builder: the entities theme appended to the design system's stylesheet, root
# links and the header lockup (scripts/docs-pages.py), the landing page directory over the generated index.html, and
# the fonts docs-builder doesn't ship. Needs a build with FEATURE_NAVIGATION_PREVIEW=true: without it the pages load
# no ds.css and this fails.
#
#   scripts/docs-finish.sh .artifacts/docs/html [path prefix]
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
out="${1:?usage: docs-finish.sh <docs-builder output dir> [path prefix]}"
prefix="${2:-}"

# The theme goes into the ds.css the pages actually load. Its name comes from docs-builder (plain ds.css with ?v=, or a
# hashed file), so read it from a built docs page instead of guessing; not index.html, which the landing page replaces.
probe="$out/getting-started/index.html"
[[ -f "$probe" ]] || { echo "docs: $probe is missing, cannot find the design-system stylesheet" >&2; exit 1; }
href="$(grep -o 'href="[^"]*/_static/ds[^"/]*\.css[^"]*"' "$probe" | grep -v 'ds-layers' | head -n1 | sed 's/^href="//; s/"$//; s/?.*$//')" || true
if [[ -z "$href" ]]; then
  echo "docs: $probe loads no design-system ds.css; build with FEATURE_NAVIGATION_PREVIEW=true" >&2
  exit 1
fi
css="$out/_static/${href##*/_static/}"
[[ -f "$css" ]] || { echo "docs: $css (from $href) does not exist" >&2; exit 1; }
# An incremental docs-builder build keeps the previous ds.css, theme and all: cut it at the marker before appending.
marker="/* entities-docs.css: appended by scripts/docs-finish.sh */"
awk -v m="$marker" '$0 == m { exit } { print }' "$css" > "$css.tmp"
{ printf '%s\n' "$marker"; cat "$root/docs/entities-docs.css"; } >> "$css.tmp"
mv "$css.tmp" "$css"

# Root links that skip htmx, and the header logo: see scripts/docs-pages.py. The logo is the landing page's animated
# nav lockup, not the docset's branding.icon (the static mark, which docs-builder uses elsewhere).
python3 -I "$root/scripts/docs-pages.py" "$out" "$root/docs" "$prefix" "images/entities-lockup-header.svg"

# The landing page is a standalone directory (index.html plus its css, js and assets), excluded from the docset. Only
# its index.html may replace a docs-builder file: anything else of the same name would break the docs pages. A copy
# left by an earlier run of this script is identical, so it passes.
for entry in "$root"/docs/landing/*; do
  name="${entry##*/}"
  if [[ "$name" != "index.html" && -e "$out/$name" ]] && ! diff -rq "$entry" "$out/$name" > /dev/null 2>&1; then
    echo "docs: docs/landing/$name would overwrite docs-builder's $name" >&2
    exit 1
  fi
done
cp -R "$root/docs/landing/." "$out/"
cp "$root/docs/robots.txt" "$out/robots.txt"
# docs-builder ships JetBrains Mono and Inter in ds.css, not Space Grotesk; the theme's headings need it.
mkdir -p "$out/fonts" && cp "$root"/docs/fonts/*.woff2 "$out/fonts/"
echo "docs: finished $out (theme appended to ${css#"$out"/}, landing page applied)"
