"""Build the bundled DM Sans variant with -0.01em tracking for WPF.

WPF has no CharacterSpacing property. Reducing glyph advances preserves the
original outlines and native text editing/caret behavior without scaling glyphs.
The derivative is renamed, and the upstream font and OFL are retained.
"""
from pathlib import Path
import json
from fontTools.ttLib import TTFont

root = Path(__file__).resolve().parents[1] / "src/GlassShell/Assets/Fonts"
report = []
for source in sorted((root / "upstream").glob("DMSans-*.ttf")):
    font = TTFont(source)
    em = font["head"].unitsPerEm
    assert em % 100 == 0, "Exact 0.01em tracking requires integral font units"
    tracking = em // 100
    style = source.stem.removeprefix("DMSans-")
    for glyph, (advance, bearing) in list(font["hmtx"].metrics.items()):
        if advance > 0:
            font["hmtx"].metrics[glyph] = (max(1, advance - tracking), bearing)
    font["hhea"].advanceWidthMax = max(a for a, _ in font["hmtx"].metrics.values())
    font["OS/2"].recalcAvgCharWidth(font)
    names = {
        1: "GlassShell DM Sans", 2: style,
        3: f"GlassShell-DMSans-{style}-TrackingMinus001Em",
        4: f"GlassShell DM Sans {style}",
        6: f"GlassShellDMSans-{style}",
        16: "GlassShell DM Sans", 17: style,
    }
    for record in font["name"].names:
        if record.nameID in names:
            record.string = names[record.nameID].encode(record.getEncoding())
    for name_id, value in names.items():
        font["name"].setName(value, name_id, 3, 1, 0x409)
    dest = root / f"GlassShellDMSans-{style}.ttf"
    font.save(dest)
    check = TTFont(dest)
    original = TTFont(source)
    for glyph, (advance, _) in original["hmtx"].metrics.items():
        assert check["hmtx"].metrics[glyph][0] == (max(1, advance - tracking) if advance > 0 else advance)
    report.append({"style": style, "unitsPerEm": em, "trackingUnits": -tracking, "trackingEm": -0.01})
(root / "tracking.json").write_text(json.dumps(report, indent=2) + "\n")
print(json.dumps(report))
