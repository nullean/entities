# The entities film

**Model your world. Get the tests for free.** A narrated explainer of Nullean.Entities, recorded with
[reel](https://mpdreamz.github.io/reel/) from the files in this folder, between an intro and an outro made by reel too.
`entities.reel` joins the scenes into one film.

| | Shows | reel |
|---|---|---|
| `sting/` | the intro and outro, carrying the title | web pages on the landing page's tokens, recorded full bleed at 16:9 and 9:16 |
| `stage/` | the full-bleed pages scenes 3, 4 and 5 record: `model.html`, `cscheck.html`, `axes.html` | one beat at a time; a click on `#next` (or →) takes the next step, `page.html#beat/step` opens on one |
| `scenes/01-types.reel` | the cold open: a type system can only encode so much; three questions an `Order`'s type can't answer | code screen: `Focus`, `Mark`, `Callout`, markers that land on a word |
| `scenes/02-metadata.reel` | rules buried as code lose their intent; `Morph` into named rules, then the same set read by a factory, an API and the tests | code screen: `Morph`, a `Code` transition |
| `scenes/03-model.reel` | what you can model: relations, time, state machines, operations, values and files, each broken on screen | `stage/model.html` (Chromium), clicks on the narration's markers |
| `scenes/04-one-line.reel` | built on CsCheck: what Entities adds on top (first-class properties, nine axes, AutoTest), and the one line | `stage/cscheck.html` |
| `scenes/05-axes.reel` | the nine axes, each playing its bug with the real failure's values | `stage/axes.html` |
| `scenes/06-live.reel` | a one-line bug planted and caught by the generated tests, then fixed | two tracks, code and a real shell, waiting on each other's named steps, directed: `Spotlight`, `Fullscreen`, `Pip`, `Morph`, terminal `Zoom` |
| `scenes/07-reports.reel` | a failure's labels, the seed, the typed result an agent reads, and the close | code screen, real failure text from `docs/_snippets` |

Two voices from [voicebox](https://github.com/jamiepine/voicebox) (`entities.reelpreset`): the narrator (its `Main`
profile) and a sceptical .NET developer (`Michael`) asking what a viewer would ask.

## Recording

You need:
- **reel 0.7.0 or later** (`brew install --cask mpdreamz/tap/reel`): 0.6.0 could stop scene 6 with "no track can go on", and voices lines with markers differently, so takes from it don't render with 0.7.0.
- **voicebox** running with those two profiles.
- **Google Chrome, ffmpeg**, and the **.NET SDK** from `global.json`.
- **An unlocked screen** for the stage scenes and the stings. Nothing needs Screen Recording or Accessibility: code screens and terminals are drawn, and web pages are driven over DevTools.

Scenes 3, 4 and 5 record the pages in `stage/` straight from disk; nothing needs serving.

```sh
reels/setup.sh                              # a worktree at HEAD, built, that scene 6 runs in; resets it too
yabai -m rule --add label=reel app='Chrom' manage=off   # only under yabai: Chrome must float
export REEL_COMPANION=0                     # keep reel's menu-bar companion out of the takes
for s in reels/sting/*.reel; do reel "$s"; done && reels/sting/finish.sh
for s in reels/scenes/*.reel; do reels/setup.sh >/dev/null; reel record "$s"; done
reel render reels/entities.reel --profile web   # .artifacts/reel/entities-web.mp4
yabai -m rule --remove reel
```

`reels/setup.sh` again before re-recording scene 6: its take plants a bug in the worktree, and starts from a clean one.
Changing a caption, a zoom, a director's move or a transition needs only a render; changing a narrated line, a step or
anything on screen needs that scene recorded again. Each scene also renders on its own, for every profile in the
preset (github, web, social at 9:16 and feed): `reel render reels/scenes/05-axes.reel` writes four clips.

## Workarounds that remain

- **The stings are recorded in Chromium, and `sting/finish.sh` trims its window rim.** reel's Chromium window still
  shows a half-point rim along the page's left edge and top. WebKit's window has none, but WebKit draws nothing while
  its window is off screen (behind another app's), so a take of it can come out blank.
- **`Speed` and `Hide` belong to the director in scene 6.** In a track they would put it out of step with the others,
  and reel refuses them there.
