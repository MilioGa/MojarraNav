// Genera ui/iconos.js con iconos Lucide (ISC) embebidos -> funciona sin internet
const fs = require('fs'), path = require('path');
const dir = path.join(__dirname, '..', 'node_modules', 'lucide-static', 'icons');
const names = ['arrow-left','arrow-right','rotate-cw','x','house','star','plus','ellipsis-vertical','download','sparkles','search','lock','globe','triangle-alert','volume-2','volume-x','chevron-up','chevron-down','clock','trash-2','folder-open','settings','send','square','loader-circle','file-text','languages','list','circle-check','copy','message-square-plus','paperclip','external-link','pencil','shield-check','camera','mic','map-pin','bell','info','zoom-in','zoom-out','history','bookmark','monitor','panel-right','check'];
const out = {};
for (const n of names) {
  const f = path.join(dir, n + '.svg');
  if (!fs.existsSync(f)) { console.warn('falta', n); continue; }
  out[n] = fs.readFileSync(f, 'utf8').replace(/<!--[\s\S]*?-->/g, '').replace(/^[\s\S]*?<svg[^>]*>/, '').replace(/<\/svg>\s*$/, '').replace(/\s+/g, ' ').trim();
}
fs.writeFileSync(path.join(__dirname, '..', 'ui', 'iconos.js'),
  '// Iconos: Lucide (https://lucide.dev) — licencia ISC\nwindow.ICONS=' + JSON.stringify(out) + ';\n' +
  "window.icon=(n,s=16)=>`<svg class=\"i\" width=\"${s}\" height=\"${s}\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\" aria-hidden=\"true\">${window.ICONS[n]||''}</svg>`;\n");
console.log(Object.keys(out).length, 'iconos');
