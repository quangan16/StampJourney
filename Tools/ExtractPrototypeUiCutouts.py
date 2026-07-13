from __future__ import annotations

import json
import math
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageOps


OUTPUT = Path(__file__).resolve().parents[1] / "Assets" / "Arts" / "Prototype_UI_Demo_Cutouts"
CAPTURE_SOURCE = Path(r"C:\Users\Andy\AppData\Local\Temp\codex-clipboard-a662f79c-03a4-4c1c-87b2-e82d0bc03932.png")
SOURCE = OUTPUT / "_reference" / "source_ui_941x1672.png"
if not SOURCE.exists():
    SOURCE = CAPTURE_SOURCE


def rgba(image: Image.Image) -> Image.Image:
    return image.convert("RGBA")


def soft_ellipse_mask(size: tuple[int, int], inset: int = 1, feather: float = 1.1) -> Image.Image:
    w, h = size
    scale = 4
    mask = Image.new("L", (w * scale, h * scale), 0)
    ImageDraw.Draw(mask).ellipse(
        (inset * scale, inset * scale, (w - inset) * scale, (h - inset) * scale), fill=255
    )
    mask = mask.resize(size, Image.Resampling.LANCZOS)
    return mask.filter(ImageFilter.GaussianBlur(feather)) if feather else mask


def soft_round_mask(size: tuple[int, int], radius: int, feather: float = 1.2) -> Image.Image:
    w, h = size
    scale = 4
    mask = Image.new("L", (w * scale, h * scale), 0)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, w * scale - 1, h * scale - 1), radius * scale, fill=255)
    mask = mask.resize(size, Image.Resampling.LANCZOS)
    return mask.filter(ImageFilter.GaussianBlur(feather)) if feather else mask


def scalloped_stamp_mask(size: tuple[int, int]) -> Image.Image:
    """Retain the whole paper stamp, with transparent space outside its perforated edge."""
    w, h = size
    mask = Image.new("L", size, 0)
    d = ImageDraw.Draw(mask)
    left, top, right, bottom = 6, 6, w - 7, h - 7
    d.rectangle((left, top, right, bottom), fill=255)
    for x in range(left + 4, right - 3, 12):
        d.ellipse((x - 4, top - 3, x + 4, top + 5), fill=0)
        d.ellipse((x - 4, bottom - 5, x + 4, bottom + 3), fill=0)
    for y in range(top + 4, bottom - 3, 12):
        d.ellipse((left - 3, y - 4, left + 5, y + 4), fill=0)
        d.ellipse((right - 5, y - 4, right + 3, y + 4), fill=0)
    return mask.filter(ImageFilter.GaussianBlur(0.35))


def dark_icon(image: Image.Image, cutoff: int = 178) -> Image.Image:
    arr = np.asarray(image.convert("RGB"), dtype=np.float32)
    luma = arr[:, :, 0] * 0.299 + arr[:, :, 1] * 0.587 + arr[:, :, 2] * 0.114
    alpha = np.clip((cutoff - luma) * 3.2, 0, 255).astype(np.uint8)
    out = rgba(image)
    out.putalpha(Image.fromarray(alpha).filter(ImageFilter.GaussianBlur(0.25)))
    return out


def light_icon(image: Image.Image, cutoff: int = 198) -> Image.Image:
    arr = np.asarray(image.convert("RGB"), dtype=np.float32)
    luma = arr[:, :, 0] * 0.299 + arr[:, :, 1] * 0.587 + arr[:, :, 2] * 0.114
    chroma = arr.max(axis=2) - arr.min(axis=2)
    alpha = np.clip((luma - cutoff) * 4.0, 0, 255)
    alpha *= np.clip((60 - chroma) / 60, 0, 1)
    out = rgba(image)
    out.putalpha(Image.fromarray(alpha.astype(np.uint8)).filter(ImageFilter.GaussianBlur(0.2)))
    return out


def make_puzzle_piece(source: Image.Image, bbox: tuple[int, int, int, int], piece: str) -> Image.Image:
    crop = rgba(source.crop(bbox))
    w, h = crop.size
    scale = 4
    mask = Image.new("L", (w * scale, h * scale), 0)
    d = ImageDraw.Draw(mask)
    # The artwork has a 380 px square body; its tabs extend into the neighbouring piece.
    if piece == "top_left":
        d.rectangle((0, 0, 382 * scale, 394 * scale), fill=255)
        d.ellipse((348 * scale, 170 * scale, 429 * scale, 251 * scale), fill=255)
        d.ellipse((154 * scale, 354 * scale, 234 * scale, 448 * scale), fill=255)
    elif piece == "top_right":
        d.rectangle((36 * scale, 0, w * scale, 394 * scale), fill=255)
        d.ellipse((0, 170 * scale, 82 * scale, 252 * scale), fill=0)
        d.ellipse((165 * scale, 354 * scale, 246 * scale, 448 * scale), fill=255)
    elif piece == "bottom_left":
        d.rectangle((0, 34 * scale, 382 * scale, h * scale), fill=255)
        d.ellipse((154 * scale, 0, 234 * scale, 81 * scale), fill=0)
        d.ellipse((348 * scale, 139 * scale, 429 * scale, 220 * scale), fill=255)
    elif piece == "bottom_right":
        d.rectangle((36 * scale, 34 * scale, w * scale, h * scale), fill=255)
        d.ellipse((165 * scale, 0, 246 * scale, 81 * scale), fill=0)
        d.ellipse((0, 139 * scale, 82 * scale, 220 * scale), fill=0)
    mask = mask.resize((w, h), Image.Resampling.LANCZOS).filter(ImageFilter.GaussianBlur(0.45))
    crop.putalpha(mask)
    return crop


def rebuilt_header(size: tuple[int, int]) -> Image.Image:
    w, h = size
    rng = np.random.default_rng(7)
    base = np.zeros((h, w, 4), dtype=np.uint8)
    noise = rng.normal(0, 4.5, (h, w))
    x = np.linspace(-1, 1, w)
    y = np.linspace(-1, 1, h)
    vignette = 13 * (x[None, :] ** 2) + 7 * (y[:, None] ** 2)
    for channel, value in enumerate((84, 86, 56)):
        base[:, :, channel] = np.clip(value + noise - vignette, 0, 255)
    base[:, :, 3] = 255
    image = Image.fromarray(base, "RGBA")
    image = image.filter(ImageFilter.GaussianBlur(0.35))
    edge = Image.new("RGBA", size, (0, 0, 0, 0))
    ImageDraw.Draw(edge).line((0, h - 2, w, h - 2), fill=(43, 38, 24, 180), width=3)
    return Image.alpha_composite(image, edge)


def rebuilt_tray(size: tuple[int, int]) -> Image.Image:
    w, h = size
    image = Image.new("RGBA", size, (0, 0, 0, 0))
    d = ImageDraw.Draw(image)
    d.rounded_rectangle((1, 3, w - 1, h - 1), radius=25, fill=(98, 69, 38, 90))
    d.rounded_rectangle((0, 0, w - 7, h - 8), radius=25, fill=(208, 168, 111, 255), outline=(136, 94, 50, 255), width=3)
    d.rounded_rectangle((10, 10, w - 17, h - 18), radius=17, fill=(241, 207, 153, 255), outline=(255, 230, 184, 255), width=3)
    texture = Image.effect_noise((w, h), 10).convert("L").point(lambda p: int((p - 128) * 0.14 + 128))
    warm = Image.new("RGBA", size, (164, 116, 63, 0))
    warm.putalpha(texture.point(lambda p: max(0, p - 128)))
    return Image.alpha_composite(image, warm.filter(ImageFilter.GaussianBlur(0.35)))


def rebuilt_toolbar(size: tuple[int, int]) -> Image.Image:
    w, h = size
    image = Image.new("RGBA", size, (0, 0, 0, 0))
    d = ImageDraw.Draw(image)
    d.rounded_rectangle((3, 5, w - 1, h - 1), radius=49, fill=(92, 65, 38, 90))
    d.rounded_rectangle((0, 0, w - 8, h - 9), radius=50, fill=(196, 156, 102, 255), outline=(111, 77, 42, 255), width=3)
    d.rounded_rectangle((9, 10, w - 18, h - 18), radius=40, fill=(236, 211, 166, 255), outline=(255, 233, 193, 255), width=3)
    texture = Image.effect_noise((w, h), 11).convert("L").point(lambda p: int((p - 128) * 0.13 + 128))
    warm = Image.new("RGBA", size, (146, 101, 55, 0))
    warm.putalpha(texture.point(lambda p: max(0, p - 128)))
    return Image.alpha_composite(image, warm.filter(ImageFilter.GaussianBlur(0.45)))


def aged_paper(size: tuple[int, int], colour: tuple[int, int, int], seed: int) -> Image.Image:
    """Create a quiet parchment patch for clearing dynamic copy from a baked mockup."""
    w, h = size
    rng = np.random.default_rng(seed)
    grain = rng.normal(0, 4.2, (h, w))
    fibres = Image.effect_noise((w, h), 18).filter(ImageFilter.GaussianBlur(1.5))
    fibres_array = (np.asarray(fibres, dtype=np.float32) - 128) * 0.065
    result = np.empty((h, w, 4), dtype=np.uint8)
    for channel, value in enumerate(colour):
        result[:, :, channel] = np.clip(value + grain + fibres_array, 0, 255)
    result[:, :, 3] = 255
    return Image.fromarray(result, "RGBA")


def clear_with_paper(
    image: Image.Image,
    box: tuple[int, int, int, int],
    colour: tuple[int, int, int] = (238, 208, 160),
    seed: int = 1,
) -> None:
    x1, y1, x2, y2 = box
    patch = aged_paper((x2 - x1, y2 - y1), colour, seed)
    mask = Image.new("L", patch.size, 255).filter(ImageFilter.GaussianBlur(1.2))
    image.paste(patch, (x1, y1), mask)


def blank_badge(source: Image.Image, box: tuple[int, int, int, int], seed: int) -> Image.Image:
    crop = rgba(source.crop(box))
    w, h = crop.size
    fill = aged_paper((w - 18, h - 18), (102, 70, 36), seed)
    fill_mask = Image.new("L", fill.size, 0)
    ImageDraw.Draw(fill_mask).ellipse((0, 0, fill.width - 1, fill.height - 1), fill=255)
    crop.paste(fill, (9, 9), fill_mask.filter(ImageFilter.GaussianBlur(0.55)))
    crop.putalpha(soft_ellipse_mask(crop.size, 1))
    return crop


def clear_badge_number(image: Image.Image, box: tuple[int, int, int, int], seed: int) -> None:
    x1, y1, x2, y2 = box
    fill = aged_paper((x2 - x1, y2 - y1), (102, 70, 36), seed)
    mask = Image.new("L", fill.size, 0)
    ImageDraw.Draw(mask).ellipse((0, 0, fill.width - 1, fill.height - 1), fill=255)
    image.paste(fill, (x1, y1), mask.filter(ImageFilter.GaussianBlur(0.5)))


def save(image: Image.Image, path: Path, entries: list[dict], category: str, source_box=None, note: str = "") -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    image.save(path, "PNG", optimize=True)
    entries.append(
        {
            "file": path.relative_to(OUTPUT).as_posix(),
            "category": category,
            "size": list(image.size),
            "source_box": list(source_box) if source_box else None,
            "note": note,
        }
    )


def contact_sheet(items: list[tuple[str, Path]], destination: Path) -> None:
    cell_w, cell_h = 300, 240
    cols = 4
    rows = math.ceil(len(items) / cols)
    sheet = Image.new("RGB", (cols * cell_w, rows * cell_h), (38, 37, 30))
    draw = ImageDraw.Draw(sheet)
    for index, (label, path) in enumerate(items):
        image = Image.open(path).convert("RGBA")
        image.thumbnail((cell_w - 34, cell_h - 54), Image.Resampling.LANCZOS)
        x = (index % cols) * cell_w + (cell_w - image.width) // 2
        y = (index // cols) * cell_h + 32 + (cell_h - 58 - image.height) // 2
        sheet.alpha_composite(image, (x, y)) if sheet.mode == "RGBA" else sheet.paste(image, (x, y), image)
        draw.text(((index % cols) * cell_w + 12, (index // cols) * cell_h + 10), label, fill=(240, 227, 194))
    sheet.save(destination, "PNG", optimize=True)


def main() -> None:
    source = Image.open(SOURCE).convert("RGB")
    if source.size != (941, 1672):
        raise ValueError(f"Unexpected source size: {source.size}")
    OUTPUT.mkdir(parents=True, exist_ok=True)
    for relative_path in (
        "header/level_word.png",
        "header/level_value_1.png",
        "header/timer_value_03_38.png",
        "bottom_bar/label_hint.png",
        "bottom_bar/label_shuffle.png",
        "bottom_bar/label_preview.png",
        "bottom_bar/label_restart.png",
        "bottom_bar/badge_hint_5.png",
        "bottom_bar/badge_preview_3.png",
    ):
        (OUTPUT / relative_path).unlink(missing_ok=True)
    entries: list[dict] = []
    preview_items: list[tuple[str, Path]] = []

    def export(name: str, image: Image.Image, category: str, source_box=None, note: str = "") -> None:
        path = OUTPUT / name
        save(image, path, entries, category, source_box, note)
        if not name.startswith("_reference") and not name.endswith("manifest.json"):
            preview_items.append((Path(name).stem, path))

    # Reference copies
    export("_reference/source_ui_941x1672.png", source, "reference", (0, 0, 941, 1672))
    export("_reference/layout_1080x1920.png", source.resize((1080, 1920), Image.Resampling.LANCZOS), "reference", (0, 0, 941, 1672), "Scaled screen reference for Canvas layout.")

    # Header and HUD, with all baked text removed for dynamic Unity labels.
    header_full = rgba(source.crop((0, 0, 941, 205)))
    clear_with_paper(header_full, (179, 53, 302, 143), seed=11)
    clear_with_paper(header_full, (458, 66, 616, 130), seed=12)
    export("header/header_full.png", header_full, "header", (0, 0, 941, 205), "Text-free full header.")
    export("header/header_background_blank_rebuilt.png", rebuilt_header((941, 205)), "header", None, "Clean reconstructed background from the source palette.")
    header_items = {
        "button_back.png": ((35, 47, 136, 147), "round button"),
        "level_badge.png": ((151, 31, 331, 160), "level label and value together"),
        "timer_panel.png": ((365, 48, 665, 147), "timer icon and value together"),
        "button_settings.png": ((803, 48, 904, 147), "round button"),
    }
    for name, (box, note) in header_items.items():
        crop = rgba(source.crop(box))
        if name == "level_badge.png":
            clear_with_paper(crop, (24, 23, 157, 113), seed=13)
            note = "Blank level badge for a dynamic Unity label."
        elif name == "timer_panel.png":
            clear_with_paper(crop, (92, 16, 270, 84), seed=14)
            note = "Blank timer panel; clock icon remains."
        if name.startswith("button_"):
            crop.putalpha(soft_ellipse_mask(crop.size, 2))
        export(f"header/{name}", crop, "header", box, note)
    export("icons/icon_back.png", dark_icon(source.crop((60, 77, 113, 122))), "icons", (60, 77, 113, 122))
    export("icons/icon_gear.png", dark_icon(source.crop((824, 70, 882, 123))), "icons", (824, 70, 882, 123))
    export("icons/icon_clock.png", dark_icon(source.crop((399, 67, 454, 129))), "icons", (399, 67, 454, 129))

    # Stamp tray and the five stamp slots.
    tray_box = (27, 189, 916, 452)
    export("stamp_tray/stamp_tray_full.png", rgba(source.crop(tray_box)), "stamp tray", tray_box)
    export("stamp_tray/stamp_tray_blank_rebuilt.png", rebuilt_tray((889, 263)), "stamp tray", None, "Blank tray for dynamic stamp slots.")
    export("stamp_tray/stamp_tray_frame.png", rgba(source.crop(tray_box)).copy(), "stamp tray", tray_box, "Full source tray, useful as a fixed backdrop.")
    slot_boxes = [
        (62, 222, 222, 411),
        (225, 222, 385, 411),
        (389, 222, 548, 411),
        (552, 222, 711, 411),
        (715, 222, 875, 411),
    ]
    for index, box in enumerate(slot_boxes, 1):
        crop = rgba(source.crop(box))
        crop.putalpha(scalloped_stamp_mask(crop.size))
        export(f"stamp_tray/stamp_slot_{index:02d}.png", crop, "stamp slots", box, "Individual stamped placeholder slot.")

    # Puzzle frame and all four playable pieces.
    board_box = (27, 462, 915, 1320)
    board = rgba(source.crop(board_box))
    board_alpha = soft_round_mask(board.size, 42)
    board.putalpha(board_alpha)
    export("puzzle/puzzle_board_full.png", board, "puzzle", board_box, "Complete puzzle board as shown in the reference.")
    frame = rgba(source.crop(board_box))
    frame_alpha = np.asarray(soft_round_mask(frame.size, 42)).copy()
    frame_alpha[50:807, 55:834] = 0
    frame.putalpha(Image.fromarray(frame_alpha))
    export("puzzle/puzzle_board_frame.png", frame, "puzzle", board_box, "Outer board/frame with a transparent puzzle area.")
    pieces = {
        "puzzle_piece_top_left.png": ((87, 512, 519, 960), "top_left"),
        "puzzle_piece_top_right.png": ((430, 512, 858, 960), "top_right"),
        "puzzle_piece_bottom_left.png": ((87, 875, 519, 1266), "bottom_left"),
        "puzzle_piece_bottom_right.png": ((430, 875, 858, 1266), "bottom_right"),
    }
    extracted_pieces: dict[str, Image.Image] = {}
    for name, (box, piece) in pieces.items():
        extracted = make_puzzle_piece(source, box, piece)
        extracted_pieces[piece] = extracted
        export(f"puzzle/{name}", extracted, "puzzle pieces", box, "Transparent jigsaw piece, including its white stamp edge.")
    assembled = Image.new("RGBA", (771, 754), (0, 0, 0, 0))
    for piece, position in {
        "top_left": (0, 0),
        "top_right": (343, 0),
        "bottom_left": (0, 363),
        "bottom_right": (343, 363),
    }.items():
        assembled.alpha_composite(extracted_pieces[piece], position)
    export("_reference/puzzle_piece_assembly_preview.png", assembled, "reference", None, "Assembly check for the four individual transparent pieces.")
    export("puzzle/puzzle_complete_stamp.png", rgba(source.crop((87, 512, 858, 1266))), "puzzle", (87, 512, 858, 1266), "Complete four-piece stamp artwork.")

    # Instruction note and its reusable decorative parts.
    note_box = (266, 1327, 681, 1458)
    instruction_note = rgba(source.crop(note_box))
    clear_with_paper(instruction_note, (105, 31, 351, 110), colour=(242, 221, 184), seed=15)
    export("hint/instruction_note_full.png", instruction_note, "hint", note_box, "Text-free instruction note; lightbulb and tape remain.")
    export("icons/icon_lightbulb_note.png", dark_icon(source.crop((315, 1368, 361, 1432))), "icons", (315, 1368, 361, 1432))
    export("hint/tape_left.png", rgba(source.crop((265, 1330, 331, 1374))), "hint", (265, 1330, 331, 1374))
    export("hint/tape_right.png", rgba(source.crop((618, 1417, 683, 1463))), "hint", (618, 1417, 683, 1463))

    # Bottom controls: individual buttons, item badges, and icon layers.
    toolbar_box = (35, 1483, 902, 1660)
    toolbar_full = rgba(source.crop(toolbar_box))
    for box, seed in (
        ((57, 124, 127, 160), 16),
        ((199, 124, 314, 160), 17),
        ((538, 124, 682, 160), 18),
        ((719, 124, 828, 160), 19),
    ):
        clear_with_paper(toolbar_full, box, colour=(232, 207, 163), seed=seed)
    clear_badge_number(toolbar_full, (118, 17, 144, 47), seed=20)
    clear_badge_number(toolbar_full, (642, 18, 668, 48), seed=21)
    export("bottom_bar/bottom_toolbar_full.png", toolbar_full, "bottom bar", toolbar_box, "Text-free full toolbar.")
    export("bottom_bar/bottom_toolbar_blank_rebuilt.png", rebuilt_toolbar((867, 177)), "bottom bar", None, "Blank toolbar for interactive controls.")
    badges = {
        "hint": (142, 1490, 187, 1539),
        "preview": (666, 1491, 710, 1539),
    }
    blank_badges: dict[str, Image.Image] = {}
    for index, (name, box) in enumerate(badges.items(), 22):
        badge = blank_badge(source, box, index)
        blank_badges[name] = badge
        export(f"bottom_bar/badge_{name}_blank.png", badge, "badges", box, "Empty item-count badge for a dynamic number.")
    controls = {
        "button_hint.png": ((73, 1504, 183, 1612), "round button"),
        "button_shuffle.png": ((235, 1504, 345, 1612), "round button"),
        "button_pause.png": ((384, 1485, 545, 1629), "primary round button"),
        "button_preview.png": ((587, 1504, 707, 1612), "round button"),
        "button_restart.png": ((747, 1504, 859, 1612), "round button"),
    }
    for name, (box, note) in controls.items():
        crop = rgba(source.crop(box))
        if name in {"button_hint.png", "button_preview.png"}:
            mirrored = ImageOps.mirror(crop)
            repair = Image.new("L", crop.size, 0)
            start = 60 if name == "button_hint.png" else 68
            ImageDraw.Draw(repair).rectangle((start, 0, crop.width, 43), fill=255)
            crop.paste(mirrored, (0, 0), repair.filter(ImageFilter.GaussianBlur(1.1)))
            full_name = name.replace("button_", "control_").replace(".png", "_with_badge.png")
            full = crop.copy()
            full.putalpha(soft_ellipse_mask(full.size, 2))
            badge_name = "hint" if name == "button_hint.png" else "preview"
            badge_position = (69, -14) if badge_name == "hint" else (79, -13)
            full.alpha_composite(blank_badges[badge_name], badge_position)
            export(f"bottom_bar/{full_name}", full, "bottom controls", box, "Control with an empty item-count badge.")
        crop.putalpha(soft_ellipse_mask(crop.size, 2))
        export(f"bottom_bar/{name}", crop, "bottom controls", box, note)
    export("icons/icon_hint.png", dark_icon(source.crop((99, 1521, 154, 1581))), "icons", (99, 1521, 154, 1581))
    export("icons/icon_shuffle.png", dark_icon(source.crop((257, 1532, 325, 1575))), "icons", (257, 1532, 325, 1575))
    export("icons/icon_preview.png", dark_icon(source.crop((612, 1530, 674, 1577))), "icons", (612, 1530, 674, 1577))
    export("icons/icon_restart.png", dark_icon(source.crop((767, 1529, 838, 1580))), "icons", (767, 1529, 838, 1580))
    export("icons/icon_pause.png", light_icon(source.crop((428, 1524, 505, 1594))), "icons", (428, 1524, 505, 1594))

    # Main preview deliberately shows the most useful individual text-free pieces.
    key_order = [
        "header/header_background_blank_rebuilt.png",
        "header/button_back.png",
        "header/level_badge.png",
        "header/timer_panel.png",
        "header/button_settings.png",
        "stamp_tray/stamp_tray_blank_rebuilt.png",
        "stamp_tray/stamp_slot_01.png",
        "stamp_tray/stamp_slot_02.png",
        "stamp_tray/stamp_slot_03.png",
        "stamp_tray/stamp_slot_04.png",
        "stamp_tray/stamp_slot_05.png",
        "puzzle/puzzle_board_frame.png",
        "puzzle/puzzle_piece_top_left.png",
        "puzzle/puzzle_piece_top_right.png",
        "puzzle/puzzle_piece_bottom_left.png",
        "puzzle/puzzle_piece_bottom_right.png",
        "hint/instruction_note_full.png",
        "bottom_bar/bottom_toolbar_blank_rebuilt.png",
        "bottom_bar/button_hint.png",
        "bottom_bar/button_shuffle.png",
        "bottom_bar/button_pause.png",
        "bottom_bar/button_preview.png",
        "bottom_bar/button_restart.png",
    ]
    contact_sheet([(Path(item).stem, OUTPUT / item) for item in key_order], OUTPUT / "_preview_contact_sheet.png")

    manifest = {
        "source": str(SOURCE),
        "source_size": [941, 1672],
        "screen_reference_size": [1080, 1920],
        "unity_notes": {
            "texture_type": "Sprite (2D and UI)",
            "mesh_type": "Full Rect",
            "filter_mode": "Bilinear",
            "compression": "None for UI, or High Quality after visual review",
            "canvas_reference_resolution": [1080, 1920],
        },
        "assets": entries,
    }
    (OUTPUT / "assets_manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"Exported {len(entries)} PNG assets to {OUTPUT}")


if __name__ == "__main__":
    main()
