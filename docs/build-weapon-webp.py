"""Build a compact animated WebP from numbered Unreal99 capture frames."""

from __future__ import annotations

import argparse
import os
import time
from pathlib import Path

from PIL import Image, ImageChops


def prepare(image: Image.Image, args, keep_alpha: bool) -> Image.Image:
    """Bring one capture frame down to the published size.

    A studio plate is rendered on black with the subject's coverage in alpha, so the order of
    operations matters: resizing RGBA directly averages transparent black into the edge pixels
    and leaves a grey halo around everything. Flattening onto the white card at full resolution
    first — or premultiplying, when the alpha is being kept — is what avoids that.
    """
    if not args.alpha:
        frame = image.convert("RGB")
        frame.thumbnail((args.width, args.height), Image.Resampling.LANCZOS)
        return frame

    plate = image.convert("RGBA")
    if not keep_alpha:
        card = Image.new("RGBA", plate.size, (255, 255, 255, 255))
        card.alpha_composite(plate)
        plate.close()
        frame = card.convert("RGB")
        card.close()
        frame.thumbnail((args.width, args.height), Image.Resampling.LANCZOS)
        return frame

    # Premultiply, resize, then unpremultiply so the retained alpha edge stays colour-correct.
    r, g, b, a = plate.split()
    premultiplied = Image.merge("RGBA", (
        ImageChops.multiply(r, a), ImageChops.multiply(g, a), ImageChops.multiply(b, a), a))
    plate.close()
    premultiplied.thumbnail((args.width, args.height), Image.Resampling.LANCZOS)
    pr, pg, pb, pa = premultiplied.split()
    premultiplied.close()
    inverse = pa.point(lambda v: 0 if v == 0 else min(255, round(65025 / v)))
    return Image.merge("RGBA", (
        ImageChops.multiply(pr, inverse), ImageChops.multiply(pg, inverse),
        ImageChops.multiply(pb, inverse), pa))


def publish(temporary: Path, destination: Path, attempts: int = 40) -> None:
    """Move the freshly encoded clip onto its published path.

    Encoding straight to the destination fails intermittently on Windows: an antivirus scanner
    or the search indexer opens the previous WebP the moment it is touched, and Pillow's write
    comes back as OSError 22. Encoding to a sibling temp file and then replacing it keeps a
    complete clip on disk either way, and the replace itself is retried for a few seconds.
    """
    for attempt in range(attempts):
        try:
            os.replace(temporary, destination)
            return
        except OSError:
            if attempt == attempts - 1:
                raise
            time.sleep(0.25)


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
        help="Treat the capture as a studio plate whose background is transparent. Turntables "
        "use this; action footage is a real arena scene and is always left alone.",
    )
    parser.add_argument(
        "--background",
        choices=("white", "transparent"),
        default="white",
        help="What to put behind a studio plate. 'white' flattens it onto a white card, which is "
        "what the README shows and what any viewer that ignores alpha will display. "
        "'transparent' keeps the alpha channel instead. Only meaningful with --alpha.",
    )
    args = parser.parse_args()

    sources = sorted(args.input.glob("*.png"))
    if not sources:
        parser.error(f"no PNG frames found in {args.input}")
    if len(sources) != args.expected_frames:
        parser.error(f"expected {args.expected_frames} PNG frames in {args.input}, found {len(sources)}")

    keep_alpha = args.alpha and args.background == "transparent"
    mode = "RGBA" if keep_alpha else "RGB"
    background: object = (0, 0, 0, 0) if keep_alpha else ("white" if args.alpha else "black")

    frames: list[Image.Image] = []
    staged = args.output.with_name(args.output.name + ".partial")
    try:
        for source in sources:
            with Image.open(source) as image:
                frame = prepare(image, args, keep_alpha)
                canvas = Image.new(mode, (args.width, args.height), background)
                canvas.paste(frame, ((args.width - frame.width) // 2, (args.height - frame.height) // 2))
                frame.close()
                frames.append(canvas)

        args.output.parent.mkdir(parents=True, exist_ok=True)
        frames[0].save(
            staged,
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
            # Lossless keeps a retained alpha edge crisp; lossy WebP quantises it into a halo.
            lossless=keep_alpha,
            exact=keep_alpha,
        )
        publish(staged, args.output)
    finally:
        for frame in frames:
            frame.close()
        staged.unlink(missing_ok=True)

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
