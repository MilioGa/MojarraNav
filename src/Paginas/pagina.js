// Utilidades comunes de las páginas internas (moja://…).
// Hablan con la ventana del navegador por chrome.webview; el navegador solo responde a páginas moja://.
const $ = (s) => document.querySelector(s);
const _pedidos = new Map(); let _sig = 1;
window.chrome.webview.addEventListener('message', (e) => {
  const m = e.data, p = m && _pedidos.get(m.id); if (!p) return;
  _pedidos.delete(m.id); m.error ? p.reject(new Error(m.error)) : p.resolve(m.resultado);
});
window.nav = { pedir: (accion, ...args) => new Promise((resolve, reject) => { const id = _sig++; _pedidos.set(id, { resolve, reject }); window.chrome.webview.postMessage({ id, accion, args }); }) };
const esc = (s) => String(s ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);
const pedir = (...a) => window.nav.pedir(...a);
const I = (n, s = 16) => window.icon(n, s);
const dominio = (u) => { try { return new URL(u).hostname.replace(/^www\./, ''); } catch { return u; } };
// Icono del sitio sin depender de servicios externos: la favicon del propio dominio, o una letra
function favicon(u, cls = 'fav') {
  let src = ''; try { const x = new URL(u); if (/^https?:$/.test(x.protocol)) src = x.origin + '/favicon.ico'; } catch {}
  return `<img class="${cls}" src="${esc(src)}" alt="" data-letra="${esc((dominio(u)[0] || '?').toUpperCase())}">`;
}
document.addEventListener('error', (e) => {
  const img = e.target; if (img.tagName !== 'IMG' || !img.dataset.letra) return;
  const s = document.createElement('span'); s.className = img.className + ' letra'; s.textContent = img.dataset.letra; img.replaceWith(s);
}, true);
document.querySelectorAll('[data-i]').forEach((e) => e.insertAdjacentHTML('afterbegin', I(e.dataset.i, 18)));
function fecha(t) {
  const d = new Date(t), hoy = new Date(); const ayer = new Date(); ayer.setDate(hoy.getDate() - 1);
  if (d.toDateString() === hoy.toDateString()) return 'Hoy';
  if (d.toDateString() === ayer.toDateString()) return 'Ayer';
  return d.toLocaleDateString('es', { weekday: 'long', day: 'numeric', month: 'long', year: d.getFullYear() === hoy.getFullYear() ? undefined : 'numeric' });
}
const hora = (t) => new Date(t).toLocaleTimeString('es', { hour: '2-digit', minute: '2-digit' });
function tamano(b) { if (!b) return '—'; const u = ['B', 'KB', 'MB', 'GB']; let i = 0; while (b >= 1024 && i < 3) { b /= 1024; i++; } return b.toFixed(i ? 1 : 0) + ' ' + u[i]; }
