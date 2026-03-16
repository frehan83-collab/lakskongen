# Steam Capsule Upload Guide

Step-by-step instructions for uploading Lakskongen capsules to Steam.

---

## 🎯 Pre-Upload Checklist

Before you start, verify:

- [ ] You have Steamworks Partner account access
- [ ] Lakskongen app is registered on Steamworks
- [ ] You have permission to edit store page assets
- [ ] All 6 capsule files are in `capsules/` folder
- [ ] You've reviewed `preview_all_capsules.png`

---

## 📤 Upload Steps

### 1. Access Steamworks Partner

1. Go to https://partner.steamgames.com/
2. Log in with your Steamworks credentials
3. Navigate to **Apps & Packages**
4. Select **Lakskongen** from your app list

---

### 2. Navigate to Graphical Assets

1. In the left sidebar, find **Store Presence**
2. Click **Graphical Assets**
3. You'll see sections for:
   - Capsule Images
   - Header Images
   - Library Assets
   - Page Background

---

### 3. Upload Each Capsule

#### Header Capsule (460×215)
- **Section:** Capsule Images → Header Capsule
- **File:** `header.png`
- **Click:** "Choose File" → Browse → Select `header.png`
- **Upload:** Click "Upload"
- ✅ **Verify:** Preview shows correctly

#### Small Capsule (231×87)
- **Section:** Capsule Images → Small Capsule
- **File:** `small.png`
- **Click:** "Choose File" → Browse → Select `small.png`
- **Upload:** Click "Upload"
- ✅ **Verify:** Preview shows correctly

#### Main Capsule (616×353)
- **Section:** Capsule Images → Main Capsule
- **File:** `main.png`
- **Click:** "Choose File" → Browse → Select `main.png`
- **Upload:** Click "Upload"
- ✅ **Verify:** Preview shows correctly

#### Library Header (460×215)
- **Section:** Library Assets → Library Header
- **File:** `library_header.png`
- **Click:** "Choose File" → Browse → Select `library_header.png`
- **Upload:** Click "Upload"
- ✅ **Verify:** Preview shows correctly

#### Library Hero (3840×1240)
- **Section:** Library Assets → Library Hero
- **File:** `library_hero.png`
- **Click:** "Choose File" → Browse → Select `library_hero.png`
- **Upload:** Click "Upload"
- ✅ **Verify:** Preview shows correctly

#### Page Background (1438×810)
- **Section:** Page Background
- **File:** `page_background.png`
- **Click:** "Choose File" → Browse → Select `page_background.png`
- **Upload:** Click "Upload"
- ✅ **Verify:** Preview shows correctly

---

### 4. Save Changes

- Scroll to bottom of page
- Click **"Save"** button
- Wait for confirmation message

---

### 5. Preview Your Store Page

1. Click **"Preview Store Page"** button (top-right)
2. Review how capsules appear:
   - Featured section (main capsule)
   - Search results (main capsule)
   - Library grid (small capsule)
   - Library detail (library header)
   - Library background (library hero)
3. Check on different screen sizes if possible
4. Verify colors look good on Steam's dark background

---

### 6. Submit for Review (Optional)

If your store page is ready for public viewing:

1. Navigate to **Store Presence → Review**
2. Click **"Submit for Review"**
3. Steam will review within 1-5 business days
4. You'll receive email notification when approved

**Note:** You can update capsules anytime, even after approval!

---

## 🔄 Updating Capsules

To replace templates with real screenshots later:

1. Generate new capsules with real screenshots
2. Return to **Graphical Assets** page
3. Upload new files (same process as above)
4. Click **"Save"**
5. Changes take effect immediately (or after re-review if needed)

---

## ❓ Troubleshooting

### "Invalid dimensions" error
- **Cause:** Wrong file size
- **Fix:** Verify file dimensions with `generate_capsules.py` verification

### "File too large" error
- **Cause:** File exceeds 5MB
- **Fix:** Re-export with higher compression (all current files are well under limit)

### "Wrong format" error
- **Cause:** Not PNG or wrong color mode
- **Fix:** Re-save as PNG (RGB or RGBA)

### Capsule looks blurry
- **Cause:** Browser scaling or compression
- **Fix:** View in full resolution, check original file quality

### Colors look wrong
- **Cause:** Color profile mismatch
- **Fix:** Ensure sRGB color space (already set in generator)

---

## 📧 Support

If you encounter issues:

1. **Steam Support:** https://partner.steamgames.com/support/
2. **Steamworks Documentation:** https://partner.steamgames.com/doc/store/assets
3. **Community Forums:** https://steamcommunity.com/groups/steamworks

---

## 📊 After Upload

### Monitor Performance

Once live, you can track:
- **Store impressions** (how many see your capsules)
- **Click-through rate** (how many click after seeing capsule)
- **Wishlist conversions**

Access via: **Steamworks → Reports → Store Traffic**

### A/B Testing

Consider creating alternative capsule designs:
- Test different taglines
- Try different screenshot compositions
- Experiment with seasonal themes

Update capsules and compare performance metrics.

---

## 🎨 Best Practices

### Visual Consistency
- Keep branding consistent across all capsules
- Use same color palette
- Maintain readable text at all sizes

### Regular Updates
- Refresh capsules with new content/features
- Create seasonal variants (Winter Sale, Summer Sale)
- Update when major updates ship

### Localization
- Consider regional variants (different languages)
- Respect cultural differences
- Test readability in target markets

---

## ✅ Upload Complete!

Once all 6 capsules are uploaded and saved:

- ✅ Your Steam store page has professional branding
- ✅ Capsules appear in search, library, and store
- ✅ Ready for wishlist collection and launch
- ✅ Can update anytime with real screenshots

**Congratulations!** Your Steam capsules are live! 🎉

---

**Next Steps:**
1. Preview store page on different devices
2. Share store link with beta testers for feedback
3. Capture real screenshots when game is polished
4. Update capsules with final visuals before launch

---

**Questions or need help?** Contact Steamworks Partner Support or review their comprehensive [Asset Documentation](https://partner.steamgames.com/doc/store/assets/standard).
