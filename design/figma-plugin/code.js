// Lakskongen Figma Populate Plugin - code.js
// This plugin creates pages/frames for tokens, screens and imports SVGs into a 03_Assets page.

figma.showUI(__html__, { width: 620, height: 680 });

// Helper: safe font loader
async function loadFontSafe(family, style) {
  try {
    await figma.loadFontAsync({ family, style });
    return true;
  } catch (e) {
    return false;
  }
}

function hexToRgbNormalized(hex) {
  const h = hex.replace('#', '');
  const bigint = parseInt(h, 16);
  const r = ((bigint >> 16) & 255) / 255;
  const g = ((bigint >> 8) & 255) / 255;
  const b = (bigint & 255) / 255;
  return { r, g, b };
}

function ensurePage(name) {
  let page = figma.root.children.find(p => p.name === name);
  if (!page) {
    page = figma.createPage();
    page.name = name;
  }
  return page;
}

async function createTokensFrame(page, tokens) {
  // try to find an existing Tokens frame
  let frame = page.findOne(n => n.type === 'FRAME' && n.name === 'Tokens');
  if (!frame) {
    frame = figma.createFrame();
    frame.name = 'Tokens';
    page.appendChild(frame);
  }
  frame.resize(1100, 800);
  frame.x = 0;
  frame.y = 0;

  // Load fonts (best-effort)
  await loadFontSafe('Inter', 'Regular');
  await loadFontSafe('Inter', 'Bold');
  await loadFontSafe('Roboto', 'Regular');

  const colors = tokens.colors || {};
  const spacingTokens = tokens.spacing || {};
  const radii = tokens.radii || {};
  const fonts = tokens.fonts || {};

  const startX = 28;
  let y = 28;
  const swW = 120;
  const swH = 40;
  const gapY = 12;

  for (const key of Object.keys(colors)) {
    const hex = colors[key];
    const rect = figma.createRectangle();
    rect.resize(swW, swH);
    rect.x = startX;
    rect.y = y;
    rect.cornerRadius = (radii.sm !== undefined) ? radii.sm : 6;
    rect.fills = [{ type: 'SOLID', color: hexToRgbNormalized(hex) }];
    frame.appendChild(rect);

    const label = figma.createText();
    label.fontSize = 14;
    label.characters = `${key} — ${hex}`;
    label.x = startX + swW + 16;
    label.y = y + 8;
    label.fills = [{ type: 'SOLID', color: { r: 1, g: 1, b: 1 } }];
    frame.appendChild(label);

    y += swH + gapY;
  }

  // Spacing & Radii
  const spFrame = figma.createFrame();
  spFrame.name = 'Spacing & Radii';
  spFrame.x = startX;
  spFrame.y = y + 12;
  spFrame.resize(420, 160);
  frame.appendChild(spFrame);

  let ty = 8;
  for (const [k, v] of Object.entries(spacingTokens)) {
    const t = figma.createText();
    t.fontSize = 14;
    t.characters = `${k}: ${v}px`;
    t.x = 8;
    t.y = ty;
    t.fills = [{ type: 'SOLID', color: { r: 1, g: 1, b: 1 } }];
    spFrame.appendChild(t);
    ty += 20;
  }

  let rx = 200;
  let ry = 8;
  for (const [k, v] of Object.entries(radii)) {
    const t = figma.createText();
    t.fontSize = 14;
    t.characters = `${k}: ${v}px`;
    t.x = rx;
    t.y = ry;
    t.fills = [{ type: 'SOLID', color: { r: 1, g: 1, b: 1 } }];
    spFrame.appendChild(t);
    ry += 20;
  }

  // Typography
  const tyFrame = figma.createFrame();
  tyFrame.name = 'Typography';
  tyFrame.x = startX + 460;
  tyFrame.y = 28;
  tyFrame.resize(420, 240);
  frame.appendChild(tyFrame);

  const baseText = figma.createText();
  baseText.fontSize = 14;
  baseText.characters = `Font stack: ${fonts.base || ''}`;
  baseText.x = 8;
  baseText.y = 8;
  tyFrame.appendChild(baseText);

  let fy = 36;
  const sizes = (fonts.sizes) ? fonts.sizes : {};
  for (const [k, v] of Object.entries(sizes)) {
    const t = figma.createText();
    t.fontSize = 14;
    t.characters = `${k}: ${v}px`;
    t.x = 8;
    t.y = fy;
    tyFrame.appendChild(t);
    fy += 20;
  }

  const weights = (fonts.weights) ? fonts.weights : {};
  for (const [k, v] of Object.entries(weights)) {
    const t = figma.createText();
    t.fontSize = 14;
    t.characters = `${k}: ${v}`;
    t.x = 200;
    t.y = 36 + Object.keys(sizes).length * 20;
    tyFrame.appendChild(t);
  }

  return { frameId: frame.id };
}

async function createScreens(page, tokens) {
  const colors = tokens.colors || {};
  const radii = tokens.radii || {};
  const panelColor = colors.panel || '#121826';

  const screens = [
    {
      name: 'Dashboard', w: 1440, h: 1024,
      elements: [
        { name: 'Header / Header (72)', x: 0, y: 0, w: 1440, h: 72 },
        { name: 'Sidebar / Left Nav (280)', x: 0, y: 72, w: 280, h: 880 },
        { name: 'Main / Game Canvas', x: 280, y: 72, w: 880, h: 700 },
        { name: 'RightPanel / Stats', x: 1160, y: 72, w: 280, h: 700 },
        { name: 'Footer / BottomBar (96)', x: 0, y: 972, w: 1440, h: 96 }
      ]
    },
    {
      name: 'Processing Plant Builder', w: 1280, h: 920,
      elements: [
        { name: 'Toolbox / Toolbar (220)', x: 0, y: 0, w: 220, h: 920 },
        { name: 'Canvas Area', x: 220, y: 0, w: 740, h: 920 },
        { name: 'Properties Panel (320)', x: 960, y: 0, w: 320, h: 920 }
      ]
    },
    {
      name: 'Contract Manager', w: 1200, h: 880,
      elements: [
        { name: 'Contracts List (360)', x: 0, y: 0, w: 360, h: 880 },
        { name: 'Contract Detail', x: 360, y: 0, w: 840, h: 720 },
        { name: 'Top Filters / Search', x: 360, y: 720, w: 840, h: 160 }
      ]
    },
    {
      name: 'WorkTab (crew priorities)', w: 1000, h: 800,
      elements: [
        { name: 'Crew List', x: 0, y: 0, w: 260, h: 800 },
        { name: 'Priority Board', x: 260, y: 0, w: 640, h: 800 },
        { name: 'Details Modal Placeholder', x: 380, y: 120, w: 400, h: 320 }
      ]
    },
    {
      name: 'MapOverview (Norwegian coastline)', w: 1600, h: 1200,
      elements: [
        { name: 'Map Canvas', x: 0, y: 0, w: 1600, h: 1040 },
        { name: 'Legend & Controls', x: 1200, y: 20, w: 360, h: 260 },
        { name: 'Timeline / Control Bar', x: 0, y: 1040, w: 1600, h: 160 }
      ]
    },
    {
      name: 'Financial Dashboard', w: 1280, h: 900,
      elements: [
        { name: 'KPI Tiles', x: 0, y: 0, w: 1280, h: 160 },
        { name: 'Timeseries Chart Area', x: 0, y: 160, w: 960, h: 560 },
        { name: 'Cashflow Table', x: 0, y: 720, w: 1280, h: 180 }
      ]
    }
  ];

  // Create or append frames
  const created = [];
  for (const s of screens) {
    let frame = page.findOne(n => n.type === 'FRAME' && n.name === s.name);
    if (!frame) {
      frame = figma.createFrame();
      frame.name = s.name;
      frame.resize(s.w, s.h);
      page.appendChild(frame);
    }
    for (const el of s.elements) {
      const rect = figma.createRectangle();
      rect.name = el.name;
      rect.resize(el.w, el.h);
      rect.x = el.x;
      rect.y = el.y;
      rect.fills = [{ type: 'SOLID', color: hexToRgbNormalized(panelColor) }];
      rect.cornerRadius = (radii.md !== undefined) ? radii.md : 10;
      frame.appendChild(rect);
    }
    created.push({ name: s.name, id: frame.id });
  }
  return created;
}

async function importSvgsToAssetsPage(page, svgs) {
  const created = [];
  let sx = 0;
  let sy = 0;
  const colWidth = 160;
  for (const svg of svgs) {
    try {
      const node = figma.createNodeFromSvg(svg.content);
      // wrap into a component
      const comp = figma.createComponent();
      comp.name = svg.name.replace(/\.svg$/i, '') || 'icon';
      comp.x = sx;
      comp.y = sy;
      // normalize node position
      node.x = 0;
      node.y = 0;
      comp.appendChild(node);
      page.appendChild(comp);
      created.push({ name: comp.name, id: comp.id });
      sx += colWidth;
      if (sx > 1200) { sx = 0; sy += 160; }
    } catch (e) {
      console.log('Failed to import SVG', svg.name, e);
    }
  }
  return created;
}

figma.ui.onmessage = async (msg) => {
  if (msg.type === 'populate') {
    try {
      const tokens = msg.tokens || {};
      const svgs = msg.svgs || [];

      // Create pages
      const pages = ['00_Tokens', '01_Components', '02_Screens', '03_Assets', '04_Prototypes'];
      for (const p of pages) ensurePage(p);

      // Populate Tokens
      const tokensPage = ensurePage('00_Tokens');
      const tokensResult = await createTokensFrame(tokensPage, tokens);

      // Create Screens
      const screensPage = ensurePage('02_Screens');
      const screensCreated = await createScreens(screensPage, tokens);

      // Import SVGs
      const assetsPage = ensurePage('03_Assets');
      const assetsCreated = await importSvgsToAssetsPage(assetsPage, svgs);

      const result = {
        tokensFrameId: tokensResult.frameId,
        screens: screensCreated,
        assets: assetsCreated
      };

      figma.ui.postMessage({ type: 'done', result });
      figma.notify('Lakskongen populate: Finished. Check pages 00_Tokens, 02_Screens, 03_Assets.');
      // keep plugin open so user can review results in the UI
    } catch (e) {
      figma.ui.postMessage({ type: 'error', message: e.message });
      figma.closePlugin('Error: ' + e.message);
    }
  }
};
