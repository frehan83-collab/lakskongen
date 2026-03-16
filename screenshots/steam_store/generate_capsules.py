#!/usr/bin/env python3
"""
Steam Capsule Generator for Lakskongen
Creates all 6 required Steam capsule image variants with Norwegian fjord aesthetic
"""

from PIL import Image, ImageDraw, ImageFont
import os
from pathlib import Path

# Steam Capsule Dimensions (width, height)
CAPSULE_SIZES = {
    "header": (460, 215),           # Store page header
    "small": (231, 87),              # Library grid view
    "main": (616, 353),              # Featured/search results
    "library_header": (460, 215),    # Library detail view (same as header)
    "library_hero": (3840, 1240),    # Library background (optional)
    "page_background": (1438, 810),  # Store page background
}

# Lakskongen Brand Colors (Norwegian fjord palette)
COLORS = {
    "bg_dark": "#0F1720",      # Deep fjord water
    "accent": "#2AB7B5",       # Turquoise accent
    "text": "#E6EEF3",         # Light text
    "text_dim": "#8B9DAB",     # Dimmed text
    "overlay": "#0F1720CC",    # Semi-transparent overlay
}

# Convert hex to RGB
def hex_to_rgb(hex_color):
    """Convert hex color to RGB tuple"""
    hex_color = hex_color.lstrip('#')
    if len(hex_color) == 8:  # RGBA
        return tuple(int(hex_color[i:i+2], 16) for i in (0, 2, 4, 6))
    return tuple(int(hex_color[i:i+2], 16) for i in (0, 2, 4))


def create_gradient_background(width, height):
    """Create Norwegian fjord-inspired gradient background"""
    img = Image.new('RGB', (width, height), hex_to_rgb(COLORS["bg_dark"]))
    draw = ImageDraw.Draw(img, 'RGBA')
    
    # Create subtle gradient from dark blue to teal
    for y in range(height):
        # Blend from dark to accent color
        ratio = y / height
        r = int(15 + (42 - 15) * ratio * 0.3)
        g = int(23 + (183 - 23) * ratio * 0.3)
        b = int(32 + (181 - 32) * ratio * 0.3)
        draw.line([(0, y), (width, y)], fill=(r, g, b))
    
    return img


def add_gradient_overlay(draw, width, height):
    """Add bottom gradient overlay for text readability"""
    overlay_height = int(height * 0.6)
    for y in range(overlay_height):
        alpha = int((y / overlay_height) * 180)  # Fade from transparent to dark
        y_pos = height - overlay_height + y
        draw.line([(0, y_pos), (width, y_pos)], 
                 fill=(15, 23, 32, alpha))


def get_font_size(capsule_type):
    """Get appropriate font sizes for different capsule types"""
    sizes = {
        "small": {"title": 20, "tagline": 10},
        "header": {"title": 42, "tagline": 16},
        "library_header": {"title": 42, "tagline": 16},
        "main": {"title": 54, "tagline": 20},
        "page_background": {"title": 86, "tagline": 32},
        "library_hero": {"title": 160, "tagline": 60},
    }
    return sizes.get(capsule_type, {"title": 48, "tagline": 18})


def draw_text_with_outline(draw, position, text, font, fill_color, outline_color, outline_width=2):
    """Draw text with outline for better readability"""
    x, y = position
    # Draw outline
    for offset_x in range(-outline_width, outline_width + 1):
        for offset_y in range(-outline_width, outline_width + 1):
            if offset_x != 0 or offset_y != 0:
                draw.text((x + offset_x, y + offset_y), text, font=font, fill=outline_color)
    # Draw main text
    draw.text(position, text, font=font, fill=fill_color)


def create_capsule(capsule_type, output_dir):
    """Create a single Steam capsule variant"""
    width, height = CAPSULE_SIZES[capsule_type]
    
    # Create base gradient background
    img = create_gradient_background(width, height)
    
    # Create drawing context with alpha support
    overlay = Image.new('RGBA', (width, height), (0, 0, 0, 0))
    draw = ImageDraw.Draw(overlay)
    
    # Add gradient overlay for text readability
    add_gradient_overlay(draw, width, height)
    
    # Composite the overlay
    img = img.convert('RGBA')
    img = Image.alpha_composite(img, overlay)
    draw = ImageDraw.Draw(img)
    
    # Get font sizes for this capsule type
    font_sizes = get_font_size(capsule_type)
    
    # Try to use system fonts (fallback to default if not found)
    try:
        # Try common Windows fonts
        title_font = ImageFont.truetype("C:\\Windows\\Fonts\\arial.ttf", font_sizes["title"])
        tagline_font = ImageFont.truetype("C:\\Windows\\Fonts\\arial.ttf", font_sizes["tagline"])
    except:
        # Fallback to default font
        title_font = ImageFont.load_default()
        tagline_font = ImageFont.load_default()
    
    # Text content
    title = "LAKSKONGEN"
    tagline = "Build Your Salmon Empire"
    
    # Calculate text positions
    if capsule_type == "small":
        # Small capsule - minimal text
        title_bbox = draw.textbbox((0, 0), title, font=title_font)
        title_width = title_bbox[2] - title_bbox[0]
        title_height = title_bbox[3] - title_bbox[1]
        
        title_x = (width - title_width) // 2
        title_y = (height - title_height) // 2
        
        draw_text_with_outline(draw, (title_x, title_y), title, title_font, 
                             hex_to_rgb(COLORS["text"]), hex_to_rgb(COLORS["bg_dark"]), 1)
    
    elif capsule_type in ["library_hero", "page_background"]:
        # Large formats - centered with more spacing
        title_bbox = draw.textbbox((0, 0), title, font=title_font)
        tagline_bbox = draw.textbbox((0, 0), tagline, font=tagline_font)
        
        title_width = title_bbox[2] - title_bbox[0]
        tagline_width = tagline_bbox[2] - tagline_bbox[0]
        
        title_x = (width - title_width) // 2
        tagline_x = (width - tagline_width) // 2
        
        # Vertical centering with spacing
        total_height = (title_bbox[3] - title_bbox[1]) + 40 + (tagline_bbox[3] - tagline_bbox[1])
        title_y = (height - total_height) // 2
        tagline_y = title_y + (title_bbox[3] - title_bbox[1]) + 40
        
        # Draw title
        draw_text_with_outline(draw, (title_x, title_y), title, title_font,
                             hex_to_rgb(COLORS["text"]), hex_to_rgb(COLORS["bg_dark"]), 3)
        
        # Draw accent line
        line_y = tagline_y - 20
        line_width = min(tagline_width, title_width) // 2
        line_x = (width - line_width) // 2
        draw.rectangle([line_x, line_y, line_x + line_width, line_y + 4], 
                      fill=hex_to_rgb(COLORS["accent"]))
        
        # Draw tagline
        draw_text_with_outline(draw, (tagline_x, tagline_y), tagline, tagline_font,
                             hex_to_rgb(COLORS["text_dim"]), hex_to_rgb(COLORS["bg_dark"]), 2)
    
    else:
        # Standard capsules - bottom-aligned text
        title_bbox = draw.textbbox((0, 0), title, font=title_font)
        tagline_bbox = draw.textbbox((0, 0), tagline, font=tagline_font)
        
        margin = int(height * 0.1)
        
        # Position from bottom
        tagline_y = height - margin - (tagline_bbox[3] - tagline_bbox[1])
        title_y = tagline_y - (title_bbox[3] - title_bbox[1]) - 10
        
        # Draw title
        draw_text_with_outline(draw, (margin, title_y), title, title_font,
                             hex_to_rgb(COLORS["text"]), hex_to_rgb(COLORS["bg_dark"]), 2)
        
        # Draw accent line under title
        line_width = min(150, width - 2 * margin)
        draw.rectangle([margin, title_y + (title_bbox[3] - title_bbox[1]) + 5, 
                       margin + line_width, title_y + (title_bbox[3] - title_bbox[1]) + 8],
                      fill=hex_to_rgb(COLORS["accent"]))
        
        # Draw tagline
        draw_text_with_outline(draw, (margin, tagline_y), tagline, tagline_font,
                             hex_to_rgb(COLORS["text_dim"]), hex_to_rgb(COLORS["bg_dark"]), 1)
    
    # Save the image
    output_path = output_dir / f"{capsule_type}.png"
    img = img.convert('RGB')  # Convert to RGB for PNG saving
    img.save(output_path, 'PNG', optimize=True)
    
    return output_path


def create_preview_composite(capsule_dir, output_dir):
    """Create a composite preview image showing all 6 variants"""
    preview_width = 2000
    preview_height = 2400
    preview = Image.new('RGB', (preview_width, preview_height), hex_to_rgb("#1A1A1A"))
    draw = ImageDraw.Draw(preview)
    
    try:
        font = ImageFont.truetype("C:\\Windows\\Fonts\\arial.ttf", 24)
        small_font = ImageFont.truetype("C:\\Windows\\Fonts\\arial.ttf", 16)
    except:
        font = ImageFont.load_default()
        small_font = ImageFont.load_default()
    
    # Layout positions
    y_offset = 50
    x_margin = 50
    
    layouts = [
        ("main", "Main Capsule (616×353)", 100),
        ("header", "Header Capsule (460×215)", 100),
        ("library_header", "Library Header (460×215)", 100),
        ("small", "Small Capsule (231×87)", 50),
        ("page_background", "Page Background (1438×810)", 150),
        ("library_hero", "Library Hero (3840×1240)", 200),
    ]
    
    for capsule_name, label, spacing in layouts:
        capsule_path = capsule_dir / f"{capsule_name}.png"
        if capsule_path.exists():
            capsule_img = Image.open(capsule_path)
            
            # Scale to fit preview width
            max_width = preview_width - 2 * x_margin
            if capsule_img.width > max_width:
                scale = max_width / capsule_img.width
                new_width = int(capsule_img.width * scale)
                new_height = int(capsule_img.height * scale)
                capsule_img = capsule_img.resize((new_width, new_height), Image.Resampling.LANCZOS)
            
            # Draw label
            draw.text((x_margin, y_offset), label, fill=(255, 255, 255), font=font)
            
            # Draw capsule image
            y_offset += 40
            preview.paste(capsule_img, (x_margin, y_offset))
            
            # Add dimension info
            dim_text = f"{capsule_img.width}×{capsule_img.height}px"
            draw.text((x_margin + capsule_img.width + 20, y_offset + capsule_img.height // 2),
                     dim_text, fill=(150, 150, 150), font=small_font)
            
            y_offset += capsule_img.height + spacing
    
    # Save preview
    preview_path = output_dir / "preview_all_capsules.png"
    preview.save(preview_path, 'PNG', optimize=True)
    
    return preview_path


def verify_capsule_specs(capsule_dir):
    """Verify all capsules meet Steam requirements"""
    print("\n=== Steam Capsule Verification ===\n")
    
    all_valid = True
    
    for capsule_type, (width, height) in CAPSULE_SIZES.items():
        capsule_path = capsule_dir / f"{capsule_type}.png"
        
        if not capsule_path.exists():
            print(f"❌ {capsule_type}: FILE NOT FOUND")
            all_valid = False
            continue
        
        # Open and check
        img = Image.open(capsule_path)
        file_size_mb = capsule_path.stat().st_size / (1024 * 1024)
        
        checks = {
            "Dimensions": img.size == (width, height),
            "Format": img.format == 'PNG',
            "Size < 5MB": file_size_mb < 5.0,
            "Color mode": img.mode in ['RGB', 'RGBA'],
        }
        
        status = "[OK]" if all(checks.values()) else "[WARN]"
        print(f"{status} {capsule_type}.png ({width}x{height})")
        print(f"   Size: {file_size_mb:.2f} MB | Mode: {img.mode} | Format: {img.format}")
        
        if not all(checks.values()):
            for check, result in checks.items():
                if not result:
                    print(f"   [FAIL] {check}")
            all_valid = False
    
    print(f"\n{'[OK] All capsules valid!' if all_valid else '[WARN] Some capsules need attention'}")
    print("\nSteam Requirements:")
    print("  • Format: PNG (24-bit RGB or 32-bit RGBA)")
    print("  • Color profile: sRGB")
    print("  • Max file size: 5MB per image")
    print("  • DPI: 72 (web)")
    
    return all_valid


def main():
    """Generate all Steam capsule variants"""
    script_dir = Path(__file__).parent
    output_dir = script_dir / "capsules"
    output_dir.mkdir(exist_ok=True)
    
    print("=== Lakskongen Steam Capsule Generator ===\n")
    print(f"Output directory: {output_dir}\n")
    
    # Generate all capsules
    print("Generating capsules...")
    for capsule_type in CAPSULE_SIZES.keys():
        print(f"  Creating {capsule_type}...", end=" ")
        path = create_capsule(capsule_type, output_dir)
        print(f"OK {path.name}")
    
    # Create preview composite
    print("\nCreating preview composite...", end=" ")
    preview_path = create_preview_composite(output_dir, output_dir)
    print(f"OK {preview_path.name}")
    
    # Verify specs
    verify_capsule_specs(output_dir)
    
    print(f"\n[COMPLETE] All capsules saved to: {output_dir}")
    print(f"\n[PREVIEW] {preview_path}")
    print("\nNext steps:")
    print("  1. Review preview_all_capsules.png")
    print("  2. Replace with actual screenshots when available")
    print("  3. Upload to Steam Partner dashboard")


if __name__ == "__main__":
    main()
