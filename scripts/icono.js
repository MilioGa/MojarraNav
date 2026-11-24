// Genera build/icon.png (512 px) a partir de un SVG: `npm run icon` (usa Electron para dibujarlo)
const { app, BrowserWindow } = require('electron');
const fs = require('fs'), path = require('path');
const SVG = `<svg xmlns="http://www.w3.org/2000/svg" width="512" height="512" viewBox="0 0 512 512">
<defs><linearGradient id="g" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#8b7dff"/><stop offset=".55" stop-color="#6a5af0"/><stop offset="1" stop-color="#d97757"/></linearGradient>
<linearGradient id="p" x1="0" y1="0" x2="1" y2="0"><stop offset="0" stop-color="#ffffff"/><stop offset="1" stop-color="#e9e5ff"/></linearGradient></defs>
<rect x="16" y="16" width="480" height="480" rx="112" fill="url(#g)"/>
<circle cx="256" cy="256" r="168" fill="none" stroke="#ffffff" stroke-opacity=".28" stroke-width="14"/>
<path d="M88 256h336M256 88c-60 52-60 284 0 336M256 88c60 52 60 284 0 336" fill="none" stroke="#ffffff" stroke-opacity=".22" stroke-width="12"/>
<g transform="translate(256 262)">
<path d="M-118 0c40-62 128-80 196-30l46-38c6-5 14 1 11 8l-20 60 20 60c3 7-5 13-11 8l-46-38c-68 50-156 32-196-30z" fill="url(#p)"/>
<circle cx="-74" cy="-10" r="11" fill="#5b4ae0"/>
<path d="M-30-40c14 24 14 56 0 80" fill="none" stroke="#8b7dff" stroke-width="9" stroke-linecap="round"/>
</g></svg>`;
app.whenReady().then(async () => {
  const w = new BrowserWindow({ width: 512, height: 512, show: false, frame: false, transparent: true, webPreferences: { offscreen: true } });
  await w.loadURL('data:text/html,' + encodeURIComponent(`<style>html,body{margin:0;background:transparent}</style>${SVG}`));
  await new Promise((r) => setTimeout(r, 300));
  const img = await w.webContents.capturePage({ x: 0, y: 0, width: 512, height: 512 });
  fs.mkdirSync(path.join(__dirname, '..', 'build'), { recursive: true });
  fs.writeFileSync(path.join(__dirname, '..', 'build', 'icon.png'), img.resize({ width: 512, height: 512 }).toPNG());
  fs.writeFileSync(path.join(__dirname, '..', 'build', 'icon.svg'), SVG);
  console.log('build/icon.png'); app.quit();
});
