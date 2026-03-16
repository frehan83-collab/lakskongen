# Steam Capsule Images - Lakskongen

**Generated:** March 15, 2026  
**Status:** ✅ All 6 variants complete and validated

## Overview

This directory contains all 6 required Steam capsule image variants for the Lakskongen store page. These images are currently template placeholders with Norwegian fjord aesthetic branding and will be replaced with actual game screenshots once available.

## Generated Files

| File | Dimensions | Size | Purpose |
|------|------------|------|---------|
| `header.png` | 460×215 | 12 KB | Store page header |
| `small.png` | 231×87 | 4 KB | Library grid view |
| `main.png` | 616×353 | 17 KB | Featured/search results |
| `library_header.png` | 460×215 | 12 KB | Library detail view |
| `library_hero.png` | 3840×1240 | 68 KB | Library background |
| `page_background.png` | 1438×810 | 33 KB | Store page background |
| `preview_all_capsules.png` | 2000×2400 | 130 KB | Preview composite (all variants) |

## Design Specification

### Branding
- **Title:** LAKSKONGEN
- **Tagline:** Build Your Salmon Empire
- **Font:** Arial (system fallback)
- **Color Palette:**
  - Background: `#0F1720` (deep fjord water)
  - Accent: `#2AB7B5` (turquoise)
  - Text: `#E6EEF3` (light)
  - Dim Text: `#8B9DAB`

### Visual Style
- Norwegian fjord-inspired gradient backgrounds
- Subtle gradient from dark blue to teal
- Bottom overlay gradient for text readability
- Text with outline for maximum contrast
- Accent line dividers
- Professional, clean composition

## Steam Compliance

All capsules meet Steam's technical requirements:

✅ **Format:** PNG (24-bit RGB)  
✅ **Color Profile:** sRGB  
✅ **File Size:** All < 5MB (largest is 68 KB)  
✅ **DPI:** 72 (web-optimized)  
✅ **Dimensions:** Exact match to Steam specifications  

## Regeneration

To regenerate capsules (for example, with updated branding):

```powershell
python generate_capsules.py
```

The script will:
1. Create all 6 capsule variants
2. Generate a preview composite
3. Validate against Steam requirements
4. Output to `capsules/` directory

## Customization

Edit `generate_capsules.py` to customize:

- **Colors:** Modify `COLORS` dictionary
- **Text:** Change `title` and `tagline` variables in `create_capsule()`
- **Fonts:** Update font paths in the try/except blocks
- **Layout:** Adjust positioning logic for each capsule type

### Adding Real Screenshots

To replace template backgrounds with actual gameplay screenshots:

1. **Capture high-quality screenshots** (see `../README.md`)
2. **Edit `generate_capsules.py`:**
   - In `create_capsule()`, replace `create_gradient_background()` with:
     ```python
     # Load screenshot
     bg = Image.open("path/to/screenshot.png")
     bg = bg.resize((width, height), Image.Resampling.LANCZOS)
     ```
3. **Regenerate capsules** with `python generate_capsules.py`

### Screenshot Recommendations

For best results when adding real screenshots:

- **Main Capsule:** Dashboard overview or dramatic fjord scene
- **Header/Library Header:** Zoomed gameplay with UI visible
- **Small Capsule:** Simple, iconic image (logo or key visual)
- **Library Hero:** Wide cinematic shot of Norwegian coastline
- **Page Background:** Atmospheric background (blurred/darkened)

## Upload to Steam

1. Log in to [Steamworks Partner](https://partner.steamgames.com/)
2. Navigate to your app's **Store Page** section
3. Upload each capsule to the corresponding slot:
   - Header Capsule → `header.png`
   - Small Capsule → `small.png`
   - Main Capsule → `main.png`
   - Library Header → `library_header.png`
   - Library Hero → `library_hero.png`
   - Page Background → `page_background.png`
4. **Save changes** and **preview** your store page
5. Submit for review once satisfied

## Quality Checklist

Before uploading to Steam:

- [ ] All 6 capsules present
- [ ] Dimensions verified (see table above)
- [ ] File sizes < 5MB each
- [ ] Professional appearance (no placeholders if using real screenshots)
- [ ] Consistent branding across all variants
- [ ] Text is readable on all sizes
- [ ] No visual bugs or artifacts
- [ ] Preview composite reviewed
- [ ] Colors look good on Steam's dark background

## Technical Details

### Color Space
- **sRGB** color profile for web compatibility
- No embedded color profiles in PNG to avoid compatibility issues

### Compression
- **PNG optimization** enabled for smaller file sizes
- **Lossless compression** (no quality loss)

### Fallback Fonts
Script uses system fonts with fallback to PIL default if not found:
- Windows: `C:\Windows\Fonts\arial.ttf`
- Can be customized to Inter, Roboto, or custom typefaces

## Future Enhancements

Potential improvements for the generator:

1. **Screenshot Integration:** Auto-load and composite real gameplay screenshots
2. **Font Download:** Auto-download Inter or Norwegian-inspired typefaces
3. **Localization:** Generate variants for different languages
4. **Batch Processing:** Process multiple screenshot sets
5. **A/B Testing:** Generate multiple design variants for comparison
6. **Seasonal Variants:** Create capsules for sales events (Winter, Summer)

## Version History

- **v1.0** (March 15, 2026): Initial template generation with Norwegian fjord aesthetic

## Support

For issues or customization requests:
- Edit `generate_capsules.py` directly
- See Python PIL/Pillow documentation for advanced image manipulation
- Consult Steam's [Graphical Asset Guidelines](https://partner.steamgames.com/doc/store/assets)

---

**Status:** Ready for Steam upload (templates) or screenshot integration (production).
