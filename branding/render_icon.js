// Renders branding/icon.svg to package/icon.png (256x256) with Playwright's Chromium.
// Usage: node branding/render_icon.js [size]
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');

(async () => {
  const size = Number(process.argv[2] || 256);
  const svg = fs.readFileSync(path.join(__dirname, 'icon.svg'), 'utf8');
  const browser = await chromium.launch();
  const page = await browser.newPage({ viewport: { width: size, height: size }, deviceScaleFactor: 1 });
  await page.setContent(`<html><body style="margin:0;background:transparent">${svg.replace('<svg ', `<svg style="width:${size}px;height:${size}px;display:block" `)}</body></html>`);
  const out = path.join(__dirname, '..', 'package', size === 256 ? 'icon.png' : `icon-${size}.png`);
  await page.locator('svg').screenshot({ path: out, omitBackground: true });
  await browser.close();
  console.log('wrote', out);
})();
