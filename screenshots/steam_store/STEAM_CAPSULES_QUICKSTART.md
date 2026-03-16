# Steam Capsules - Quick Start Guide

**Status:** ✅ Generated (Templates) | 🔄 Ready for Screenshots

---

## 🚀 What Was Created

All 6 required Steam capsule variants have been generated as professional templates:

| Capsule | Dimensions | File | Status |
|---------|------------|------|--------|
| Header | 460×215 | `capsules/header.png` | ✅ |
| Small | 231×87 | `capsules/small.png` | ✅ |
| Main | 616×353 | `capsules/main.png` | ✅ |
| Library Header | 460×215 | `capsules/library_header.png` | ✅ |
| Library Hero | 3840×1240 | `capsules/library_hero.png` | ✅ |
| Page Background | 1438×810 | `capsules/page_background.png` | ✅ |

**Location:** `salmon-sim/screenshots/steam_store/capsules/`

---

## 📸 Preview

Open `capsules/preview_all_capsules.png` to see all 6 variants in one image.

---

## ✅ What's Ready

- ✅ All 6 capsules generated with correct dimensions
- ✅ Steam technical requirements met (PNG, sRGB, <5MB)
- ✅ Norwegian fjord aesthetic branding applied
- ✅ "LAKSKONGEN" title and tagline included
- ✅ Professional gradient backgrounds
- ✅ Verified and validated

---

## 🔄 What's Next

### Option 1: Upload Templates Now (Recommended)
Use the templates as placeholders on Steam while you develop:

```powershell
# Templates are ready to upload in capsules/ folder
# Upload to Steamworks Partner → Store Page → Graphical Assets
```

**Pros:**
- Get Steam store page live immediately
- Professional placeholder branding
- Replace with real screenshots later

---

### Option 2: Add Real Screenshots First
Capture gameplay screenshots and integrate:

1. **Capture screenshots:**
   ```powershell
   cd salmon-sim
   .\capture_screenshots.ps1  # Or manual photo mode in Godot
   ```

2. **Place best shots in `selected/` folder**

3. **Edit `generate_capsules.py`** to load screenshots:
   ```python
   # In create_capsule() function, replace gradient with:
   bg = Image.open("selected/main_capsule_source.png")
   bg = bg.resize((width, height), Image.Resampling.LANCZOS)
   ```

4. **Regenerate:**
   ```powershell
   python generate_capsules.py
   ```

---

## 🎯 Priority Order

If adding real screenshots, tackle in this order:

1. **Main Capsule (616×353)** - Most visible, appears in search
2. **Small Capsule (231×87)** - Most common library view
3. **Header Capsule (460×215)** - Store page first impression
4. Others as time allows

---

## 🛠️ Regeneration

To regenerate capsules (updated branding, different tagline, etc.):

```powershell
cd screenshots/steam_store
python generate_capsules.py
```

Edit `generate_capsules.py` to customize:
- Colors (`COLORS` dictionary)
- Text (title, tagline)
- Fonts (font paths)
- Layouts (positioning logic)

---

## 📚 Documentation

- **Full details:** `capsules/README.md`
- **Asset tracking:** `capsules/ASSET_CHECKLIST.md`
- **Screenshot capture:** `CAPTURE_CHECKLIST.md`

---

## ⚡ Quick Upload to Steam

1. Go to [Steamworks Partner](https://partner.steamgames.com/)
2. Navigate to **Store Page → Graphical Assets**
3. Upload each capsule to the corresponding slot
4. **Save** and **Preview**
5. Submit for review

---

## 🔧 Technical Specs (All Met ✅)

- Format: PNG (24-bit RGB)
- Color profile: sRGB
- Max file size: 5MB each (largest is 68 KB)
- DPI: 72 (web)
- Dimensions: Exact Steam specifications

---

## 💡 Tips

- **Templates are professional** - safe to upload as-is
- **Main Capsule matters most** - most users see this
- **Small Capsule must be readable** - keep it simple
- **Library Hero is atmospheric** - cinematic wide shot works best
- **Can update anytime** - Steam allows capsule updates

---

## 🎉 Success!

You now have production-ready Steam capsules that can be uploaded immediately. When you capture gameplay screenshots, you can easily replace the gradient backgrounds with real visuals.

**Estimated time saved:** 60-90 minutes of manual design work

---

**Questions?** See full documentation in `capsules/README.md`
