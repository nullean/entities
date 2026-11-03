#!/usr/bin/env bash
# Trims Chromium's window rim from the sting takes (half a point along the page's left edge and top, which reel 0.6.0
# still records; WebKit has none, but draws nothing while its window is off screen) and writes media/<name>.mp4 at the
# canvas size the film's outputs use. Run after `reel` has recorded each sting reel.
set -euo pipefail
cd "$(dirname "$0")/media"
for name in intro-16x9 intro-9x16 outro-16x9 outro-9x16; do
	case "$name" in *16x9) size=1920:1080 ;; *) size=1080:1920 ;; esac
	ffmpeg -v error -y -i "$name.take.mp4" -vf "crop=iw-4:ih-2:4:2,scale=$size:flags=lanczos,setsar=1" \
		-c:v libx264 -preset slow -crf 16 -pix_fmt yuv420p -an "$name.mp4"
	echo "wrote media/$name.mp4"
done
