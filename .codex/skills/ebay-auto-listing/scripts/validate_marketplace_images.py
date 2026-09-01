#!/usr/bin/env python3
"""Minimum mechanical validation for processed eBay real-product images."""

from __future__ import annotations

import argparse
from pathlib import Path

import numpy as np
from PIL import Image


def validate(path: Path, min_white_ratio: float, allow_subject_at_border: bool) -> list[str]:
    with Image.open(path) as source:
        image = source.convert("RGB")
        pixels = np.asarray(image)

    errors: list[str] = []
    if image.width != image.height:
        errors.append(f"not square ({image.width}x{image.height})")
    if min(image.size) < 1000:
        errors.append(f"resolution below 1000px ({image.width}x{image.height})")

    near_white = pixels.min(axis=2) >= 248
    white_ratio = float(near_white.mean())
    border = np.concatenate((near_white[0], near_white[-1], near_white[:, 0], near_white[:, -1]))
    border_ratio = float(border.mean())
    if white_ratio < min_white_ratio:
        errors.append(f"near-white coverage {white_ratio:.1%} < {min_white_ratio:.1%}")
    if not allow_subject_at_border and border_ratio < 0.95:
        errors.append(f"near-white outer border {border_ratio:.1%} < 95.0%")
    return errors


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("images", nargs="+", type=Path)
    parser.add_argument("--min-white-ratio", type=float, default=0.45)
    parser.add_argument(
        "--allow-subject-at-border",
        action="store_true",
        help="Skip the outer-border threshold only for an intentional hand/scale subject that touches the frame.",
    )
    args = parser.parse_args()
    failed = False
    for path in args.images:
        errors = validate(path, args.min_white_ratio, args.allow_subject_at_border)
        if errors:
            failed = True
            print(f"FAIL {path}: {'; '.join(errors)}")
        else:
            print(f"PASS {path}")
    return 1 if failed else 0


if __name__ == "__main__":
    raise SystemExit(main())
