// Datos del usuario en Documentos\<carpeta>\ : favoritos, historial, ajustes y chat.
// Archivos JSON legibles para que Claude (o el usuario) puedan consultarlos y editarlos.
const fs = require('fs');
const path = require('path');

const AJUSTES_BASE = {
  buscador: 'google',            // google | duckduckgo | bing
  inicio: 'moja://inicio',       // página de inicio
  panelClaude: true,             // mostrar el panel de Claude al abrir
  anchoClaude: 380,
  modelo: '',                    // vacío = el predeterminado de Claude Code
  esfuerzo: '',
  restaurarPestanas: true,
};
const BUSCADORES = {
  google: { nombre: 'Google', url: 'https://www.google.com/search?q=%s' },
  duckduckgo: { nombre: 'DuckDuckGo', url: 'https://duckduckgo.com/?q=%s' },
  bing: { nombre: 'Bing', url: 'https://www.bing.com/search?q=%s' },
};

function crear(dir) {
  fs.mkdirSync(dir, { recursive: true });
  const archivo = (n) => path.join(dir, n);
  const leer = (n, def) => { try { return JSON.parse(fs.readFileSync(archivo(n), 'utf8')); } catch { return def; } };
  const pend = new Map();
  // Escritura diferida y atómica (evita archivos a medias si la app se cierra)
  const guardar = (n, v, ya) => {
    clearTimeout(pend.get(n));
    const w = () => { pend.delete(n); try { const t = archivo(n) + '.tmp'; fs.writeFileSync(t, JSON.stringify(v, null, 2)); fs.renameSync(t, archivo(n)); } catch {} };
    if (ya) w(); else pend.set(n, setTimeout(w, 300));
  };

  const d = {
    dir,
    ajustes: { ...AJUSTES_BASE, ...leer('ajustes.json', {}) },
    favoritos: leer('favoritos.json', []),          // [{ url, titulo, t }]
    historial: leer('historial.json', []),          // [{ url, titulo, t, n }] más reciente primero
    sesion: leer('sesion.json', null),              // pestañas abiertas al cerrar
    chat: leer('chat.json', { session: null, log: [] }),
    BUSCADORES,
  };

  d.setAjustes = (cambios) => {
    for (const [k, v] of Object.entries(cambios || {})) if (k in AJUSTES_BASE && typeof v === typeof AJUSTES_BASE[k]) d.ajustes[k] = v;
    if (!BUSCADORES[d.ajustes.buscador]) d.ajustes.buscador = 'google';
    d.ajustes.anchoClaude = Math.max(280, Math.min(900, d.ajustes.anchoClaude | 0));
    guardar('ajustes.json', d.ajustes); return d.ajustes;
  };

  d.esFavorito = (url) => d.favoritos.some((f) => f.url === url);
  d.alternarFavorito = (url, titulo) => {
    const i = d.favoritos.findIndex((f) => f.url === url);
    if (i >= 0) d.favoritos.splice(i, 1); else d.favoritos.unshift({ url, titulo: titulo || url, t: Date.now() });
    guardar('favoritos.json', d.favoritos); return i < 0;
  };
  d.quitarFavorito = (url) => { d.favoritos = d.favoritos.filter((f) => f.url !== url); guardar('favoritos.json', d.favoritos); };
  d.editarFavorito = (url, titulo) => { const f = d.favoritos.find((x) => x.url === url); if (f) { f.titulo = String(titulo).slice(0, 200); guardar('favoritos.json', d.favoritos); } };

  d.visitar = (url, titulo) => {
    if (!/^https?:/i.test(url)) return;
    const i = d.historial.findIndex((h) => h.url === url);
    const prev = i >= 0 ? d.historial.splice(i, 1)[0] : { n: 0 };
    d.historial.unshift({ url, titulo: titulo || prev.titulo || url, t: Date.now(), n: (prev.n || 0) + 1 });
    if (d.historial.length > 5000) d.historial.length = 5000;
    guardar('historial.json', d.historial);
  };
  d.titular = (url, titulo) => { const h = d.historial.find((x) => x.url === url); if (h && titulo) { h.titulo = titulo; guardar('historial.json', d.historial); } };
  d.borrarHistorial = (url) => { d.historial = url ? d.historial.filter((h) => h.url !== url) : []; guardar('historial.json', d.historial); };
  d.buscarHistorial = (q, max = 50) => {
    const s = String(q || '').toLowerCase().trim();
    return (s ? d.historial.filter((h) => h.url.toLowerCase().includes(s) || (h.titulo || '').toLowerCase().includes(s)) : d.historial).slice(0, max);
  };
  // Sugerencias para la barra de direcciones: favoritos primero, luego lo más visitado
  d.sugerencias = (q) => {
    const s = String(q || '').toLowerCase().trim(); if (!s) return [];
    const m = (x) => x.url.toLowerCase().replace(/^https?:\/\/(www\.)?/, '').includes(s) || (x.titulo || '').toLowerCase().includes(s);
    const fav = d.favoritos.filter(m).map((f) => ({ ...f, fav: true }));
    const his = d.historial.filter((h) => m(h) && !d.esFavorito(h.url)).sort((a, b) => (b.n || 1) - (a.n || 1) || b.t - a.t);
    return [...fav, ...his].slice(0, 6);
  };
  // Más visitados para la página de inicio
  d.masVisitados = (n = 8) => [...d.historial].sort((a, b) => (b.n || 1) - (a.n || 1) || b.t - a.t).slice(0, n);

  d.guardarSesion = (pestanas, ya) => { d.sesion = pestanas; guardar('sesion.json', pestanas, ya); };
  d.guardarChat = (c, ya) => { d.chat = c; guardar('chat.json', c, ya); };
  d.urlBusqueda = (q) => (BUSCADORES[d.ajustes.buscador] || BUSCADORES.google).url.replace('%s', encodeURIComponent(q));
  d.vaciar = () => { for (const [n, t] of pend) { clearTimeout(t); } pend.clear();
    guardar('ajustes.json', d.ajustes, 1); guardar('favoritos.json', d.favoritos, 1); guardar('historial.json', d.historial, 1); if (d.sesion) guardar('sesion.json', d.sesion, 1); guardar('chat.json', d.chat, 1); };
  return d;
}

// Convierte lo que se escribe en la barra en una URL: dirección, dominio o búsqueda
function aUrl(texto, buscar, protocolo = 'moja') {
  const t = String(texto || '').trim();
  if (!t) return null;
  if (new RegExp(`^${protocolo}://`, 'i').test(t)) return t.toLowerCase();
  if (/^(https?|file):\/\//i.test(t)) return t;
  if (/^(about|data|view-source):/i.test(t)) return t;
  if (/^localhost(:\d+)?(\/.*)?$/i.test(t) || /^(\d{1,3}\.){3}\d{1,3}(:\d+)?(\/.*)?$/.test(t)) return 'http://' + t;
  if (!/\s/.test(t) && /^[\w-]+(\.[\w-]+)*\.[a-z]{2,}(:\d+)?(\/.*)?$/i.test(t)) return 'https://' + t;
  return buscar(t);
}

module.exports = { crear, aUrl, BUSCADORES };
