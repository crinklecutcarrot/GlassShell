# DM Sans

The upstream static fonts come from `googlefonts/dm-fonts`, at the commit in
`UPSTREAM-COMMIT.txt`. Their license is in `OFL.txt`.

`GlassShellDMSans-*.ttf` retain the DM Sans outlines and apply -0.01em tracking to
positive glyph advance widths. This lets WPF use the requested spacing in labels,
buttons, menus, and the native note editor, including caret/selection layout.
Combining glyphs with zero advance remain unchanged. Original kerning is retained.
The modified internal family name is **GlassShell DM Sans**.

Rebuild with `python tools/prepare_fonts.py` (fontTools required). `tracking.json`
records the exact font-unit adjustment; the script verifies every glyph advance.
