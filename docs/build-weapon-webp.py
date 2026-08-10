"""Build a compact animated WebP from numbered Unreal99 capture frames."""

from __future__ import annotations

import argparse
from pathlib import Path

from PIL import Image


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--input", required=True, type=Path, help="Directory containing numbered PNG frames")
    parser.add_argument("--output", required=True, type=Path, help="Animated WebP destination")
    parser.add_argument("--width", type=int, default=640)
    parser.add_argument("--height", type=int, default=360)
    parser.add_argument("--duration", type=int, default=67, help="Milliseconds per frame")
    parser.add_argument("--quality", type=int, default=72)
    parser.add_argument("--expected-frames", type=int, default=30)
    parser.add_argument(
        "--alpha",
        action="store_true",
        help="Keep the capture's alpha channel so the clip has a transparent background. "
        "Turntables are rendered as studio plates and use this; action footage is a real "
        "arena scene and must stay opaque.",
    )
    args = parser.parse_args()

    sources = sorted(args.input.glob("*.png"))
    if not sources:
        parser.error(f"no PNG frames found in {args.input}")
    if len(sources) != args.expected_frames:
        parser.error(f"expected {args.expected_frames} PNG frames in {args.input}, found {len(sources)}")

    mode = "RGBA" if args.alpha else "RGB"
    background = (0, 0, 0, 0) if args.alpha else "black"

    frames: list[Image.Image] = []
    try:
        for source in sources:
            with Image.open(source) as image:
                frame = image.convert(mode)
                # Downscaling a hard-edged studio plate is what feathers the silhouette: the
                # renderer writes coverage as a binary mask, and LANCZOS turns that into a
                # properly anti-aliased alpha edge at the published size.
                frame.thumbnail((args.width, args.height), Image.Resampling.LANCZOS)
                canvas = Image.new(mode, (args.width, args.height), background)
                canvas.paste(frame, ((args.width - frame.width) // 2, (args.height - frame.height) // 2))
                frame.close()
                frames.append(canvas)

        args.output.parent.mkdir(parents=True, exist_ok=True)
        frames[0].save(
            args.output,
            format="WEBP",
            save_all=True,
            append_images=frames[1:],
            duration=args.duration,
            loop=0,
            quality=args.quality,
            # Method 4 keeps generation practical for 22 clips while retaining nearly all of
            # method 6's visual quality at this small README resolution.
            method=4,
            minimize_size=True,
            # Lossless keeps the alpha edge crisp; lossy WebP quantises it into a halo.
            lossless=args.alpha,
            exact=args.alpha,
        )
    finally:
        for frame in frames:
            frame.close()

    with Image.open(args.output) as result:
        if getattr(result, "n_frames", 1) != len(sources):
            raise RuntimeError(
                f"animation verification failed: expected {len(sources)} frames, got {getattr(result, 'n_frames', 1)}"
            )
        if result.size != (args.width, args.height):
            raise RuntimeError(f"animation verification failed: unexpected size {result.size}")

    print(f"Built {args.output} ({len(sources)} frames, {args.width}x{args.height})")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
