// MojarraNav — navegador web de Mojarrium con Claude integrado (proceso principal de Electron)
// `npm start` abre el navegador · `npm test` ejecuta las pruebas y escribe tests/reporte.md
const { app, BrowserWindow, WebContentsView, ipcMain, session, protocol, net, Menu, clipboard, shell, dialog, nativeTheme } = require('electron');
const path = require('path');
const fs = require('fs');
const crypto = require('crypto');
const { pathToFileURL } = require('url');
const MARCA = require('./marca.js');
const { crear: crearDatos, aUrl } = require('./datos.js');
const { crearChat } = require('./claude.js');
const { crearPuente } = require('./puente.js');

const TEST = process.argv.includes('--test');
const PROTO = MARCA.protocolo;
const INICIO = `${PROTO}://inicio`;
const PAGINAS = path.join(__dirname, 'paginas');
const unpack = (p) => p.replace('app.asar' + path.sep, 'app.asar.unpacked' + path.sep);

app.setName(MARCA.nombre);
if (process.env.MOJA_DEV_DIR) app.setPath('userData', path.join(process.env.MOJA_DEV_DIR, '.electron'));
if (TEST) app.setPath('userData', path.join(app.getPath('temp'), MARCA.id + '-test', '.electron'));
if (!TEST && !app.requestSingleInstanceLock()) app.quit();
protocol.registerSchemesAsPrivileged([{ scheme: PROTO, privileges: { standard: true, secure: true, supportFetchAPI: true } }]);

const DIR = TEST ? path.join(app.getPath('temp'), MARCA.id + '-test') : process.env.MOJA_DEV_DIR || path.join(app.getPath('documents'), MARCA.carpeta);
if (TEST) fs.rmSync(DIR, { recursive: true, force: true });
const datos = crearDatos(DIR);
try { fs.copyFileSync(path.join(__dirname, 'CLAUDE.md'), path.join(DIR, 'CLAUDE.md')); } catch {}

// Un error inesperado no debe cerrar el navegador: se registra y se sigue
const logErr = (e) => { try { fs.appendFileSync(path.join(DIR, 'errores.log'), `[${new Date().toISOString()}] ${e && e.stack || e}\n`); } catch {} };
process.on('uncaughtException', logErr); process.on('unhandledRejection', logErr);

let win, ui, ses;
const tabs = [];                 // { id, view, wc, titulo, url, favicon, cargando, audible, error }
let activa = null, sigId = 1;
let rect = { x: 0, y: 84, width: 800, height: 600 };
let overlay = false;
const cerradas = [];             // para Ctrl+Shift+T
const descargas = [];            // { id, nombre, ruta, url, recibido, total, estado, t }
const permisos = new Map();      // "origen|permiso" -> true/false (solo esta sesión)
const pendientes = [];           // solicitudes de permiso en espera: { id, tabId, origen, permiso, cb }
const esInterna = (u) => typeof u === 'string' && u.startsWith(PROTO + '://');
const tabDe = (id) => (id == null ? activa : tabs.find((t) => t.id === +id)) || null;
const origen = (u) => { try { return new URL(u).origin; } catch { return u; } };

// ===================== Estado hacia la interfaz =====================
let estadoT = null;
function enviarEstado() {
  if (estadoT) return;
  estadoT = setTimeout(() => {
    estadoT = null; if (!ui || ui.isDestroyed()) return;
    const a = activa;
    ui.send('estado', {
      pestanas: tabs.map((t) => ({ id: t.id, titulo: t.titulo, url: t.url, favicon: t.favicon, cargando: t.cargando, audible: t.audible, silencio: t.wc.isAudioMuted() })),
      activa: a && a.id,
      url: a ? (a.url === INICIO ? '' : a.url) : '',
      puedeAtras: !!a && a.wc.navigationHistory.canGoBack(), puedeAdelante: !!a && a.wc.navigationHistory.canGoForward(),
      cargando: !!a && a.cargando, favorito: !!a && datos.esFavorito(a.url), seguro: !!a && /^https:|^moja:/.test(a.url),
      zoom: a ? Math.round(a.wc.getZoomFactor() * 100) : 100,
      permiso: pendientes.find((p) => a && p.tabId === a.id) ? (({ id, origen, permiso }) => ({ id, origen, permiso }))(pendientes.find((p) => p.tabId === a.id)) : null,
      descargas: descargas.filter((d) => d.estado === 'progressing').length,
      descargaReciente: descargas[0] && Date.now() - descargas[0].t < 8000 ? { nombre: descargas[0].nombre, estado: descargas[0].estado } : null,
    });
    datos.guardarSesion(tabs.filter((t) => t.url && !t.url.startsWith('data:')).map((t) => t.url));
  }, 16);
}

// ===================== Pestañas =====================
function colocar() {
  if (!activa) return;
  activa.view.setBounds({ x: Math.round(rect.x), y: Math.round(rect.y), width: Math.max(1, Math.round(rect.width)), height: Math.max(1, Math.round(rect.height)) });
}
function mostrarActiva() {
  for (const t of tabs) if (t !== activa) try { win.contentView.removeChildView(t.view); } catch {}
  if (activa && !overlay) { win.contentView.addChildView(activa.view); colocar(); }
}

function nuevaPestana(url = datos.ajustes.inicio || INICIO, { activar = true, despuesDe = activa, wc: wcExistente } = {}) {
  const view = wcExistente ? new WebContentsView({ webContents: wcExistente }) : new WebContentsView({ webPreferences: {
    session: ses, preload: path.join(__dirname, 'preload-pagina.js'), contextIsolation: true, sandbox: true, nodeIntegration: false,
    spellcheck: true, scrollBounce: true } });
  view.setBackgroundColor('#ffffff');
  const t = { id: sigId++, view, wc: view.webContents, titulo: 'Nueva pestaña', url, favicon: null, cargando: false, audible: false };
  const i = despuesDe ? tabs.indexOf(despuesDe) + 1 : tabs.length;
  tabs.splice(i > 0 ? i : tabs.length, 0, t);
  engancharPestana(t);
  if (!wcExistente) t.wc.loadURL(url).catch(() => {});
  if (activar || !activa) activarPestana(t); else enviarEstado();
  return t;
}
function activarPestana(t) {
  if (!t) return; activa = t; mostrarActiva(); enviarEstado();
  if (!overlay) { if (t.url === INICIO) ui.send('enfocar-barra'); else t.wc.focus(); }
}
function cerrarPestana(t) {
  if (!t) return;
  const i = tabs.indexOf(t); if (i < 0) return;
  if (t.url && !esInterna(t.url)) { cerradas.push({ url: t.url, i }); if (cerradas.length > 25) cerradas.shift(); }
  tabs.splice(i, 1);
  for (let k = pendientes.length - 1; k >= 0; k--) if (pendientes[k].tabId === t.id) { pendientes[k].cb(false); pendientes.splice(k, 1); }
  try { win.contentView.removeChildView(t.view); } catch {}
  try { t.wc.close(); } catch {}
  if (!tabs.length) { if (TEST) { activa = null; enviarEstado(); return; } return nuevaPestana(); }
  if (activa === t) activarPestana(tabs[Math.min(i, tabs.length - 1)]); else enviarEstado();
}
function moverPestana(id, a) { const t = tabDe(id); if (!t) return; const i = tabs.indexOf(t); tabs.splice(i, 1); tabs.splice(Math.max(0, Math.min(tabs.length, a)), 0, t); enviarEstado(); }
function navegar(texto, t = activa, nueva = false) {
  const url = aUrl(texto, datos.urlBusqueda, PROTO); if (!url) return null;
  if (nueva || !t) return nuevaPestana(url);
  t.url = url; t.wc.loadURL(url).catch(() => {}); t.wc.focus(); enviarEstado(); return t;
}

function engancharPestana(t) {
  const wc = t.wc;
  wc.on('page-title-updated', (_e, titulo) => { t.titulo = titulo || t.url; datos.titular(t.url, t.titulo); enviarEstado(); });
  wc.on('page-favicon-updated', (_e, favs) => { t.favicon = favs && favs[0] || null; enviarEstado(); });
  wc.on('did-start-loading', () => { t.cargando = true; t.error = null; enviarEstado(); });
  wc.on('did-stop-loading', () => { t.cargando = false; enviarEstado(); });
  const alNavegar = (_e, url) => {
    if (url.startsWith('data:') && t.error) return;
    t.url = url; t.titulo = wc.getTitle() || t.titulo; if (!esInterna(url)) t.favicon = t.favicon && origen(t.favicon) === origen(url) ? t.favicon : null;
    if (esInterna(url)) t.favicon = null;
    if (!t.error) datos.visitar(url, wc.getTitle()); enviarEstado();
  };
  wc.on('did-navigate', alNavegar);
  wc.on('did-navigate-in-page', (e, url, main) => main && alNavegar(e, url));
  wc.on('did-fail-load', (_e, code, desc, url, main) => {
    if (!main || code === -3) return;                 // -3 = cancelado (normal al navegar rápido)
    t.error = { code, desc, url }; t.url = url; t.titulo = 'No se pudo abrir la página';
    wc.loadURL(`${PROTO}://error/?u=${encodeURIComponent(url)}&c=${code}&d=${encodeURIComponent(desc)}`).catch(() => {});
    enviarEstado();
  });
  wc.on('media-started-playing', () => { t.audible = true; enviarEstado(); });
  wc.on('media-paused', () => { t.audible = wc.isCurrentlyAudible(); enviarEstado(); });
  wc.on('found-in-page', (_e, r) => ui.send('busqueda', { n: r.matches, i: r.activeMatchOrdinal }));
  wc.on('enter-html-full-screen', () => { ui.send('pantalla-completa', true); });
  wc.on('leave-html-full-screen', () => { ui.send('pantalla-completa', false); });
  wc.on('before-input-event', (e, i) => atajo(e, i, t));
  wc.on('context-menu', (_e, p) => menuContexto(t, p));
  wc.on('render-process-gone', (_e, d) => { if (d.reason !== 'clean-exit') { t.error = { code: 0, desc: 'La página dejó de responder', url: t.url }; wc.loadURL(`${PROTO}://error/?u=${encodeURIComponent(t.url)}&c=crash`).catch(() => {}); } });
  wc.on('zoom-changed', (_e, dir) => zoom(dir === 'in' ? 1 : -1, t));
  // Enlaces que abren otra ventana: pestaña nueva; las ventanas emergentes con tamaño (p. ej. iniciar sesión con Google) sí se abren
  wc.setWindowOpenHandler(({ url, disposition }) => {
    if (disposition === 'new-window') return { action: 'allow', overrideBrowserWindowOptions: { autoHideMenuBar: true, width: 520, height: 680, webPreferences: { session: ses, sandbox: true, contextIsolation: true } } };
    if (/^(https?|moja|about|data):/i.test(url)) nuevaPestana(url, { activar: disposition !== 'background-tab', despuesDe: t });
    else shell.openExternal(url).catch(() => {});
    return { action: 'deny' };
  });
  // Enlaces a otros programas (mailto:, tel:, zoommtg:...) se pasan a Windows
  wc.on('will-navigate', (e, url) => { if (!/^(https?|moja|about|data|blob|file|view-source):/i.test(url)) { e.preventDefault(); shell.openExternal(url).catch(() => {}); } });
}

function zoom(dir, t = activa) {
  if (!t) return; const pasos = [0.33, 0.5, 0.67, 0.75, 0.8, 0.9, 1, 1.1, 1.25, 1.5, 1.75, 2, 2.5, 3, 4, 5];
  const z = t.wc.getZoomFactor(); let k = pasos.findIndex((p) => p >= z - 0.001); if (k < 0) k = pasos.length - 1;
  t.wc.setZoomFactor(dir === 0 ? 1 : pasos[Math.max(0, Math.min(pasos.length - 1, k + dir))]); enviarEstado();
}

// ===================== Atajos de teclado (funcionan con el foco en la página o en la barra) =====================
function atajo(e, i, t) {
  if (i.type !== 'keyDown') return;
  const c = i.control || i.meta, k = i.key.length === 1 ? i.key.toLowerCase() : i.key;
  const hacer = (f) => { e.preventDefault(); f(); };
  if (c && !i.shift && k === 't') return hacer(() => nuevaPestana());
  if (c && !i.shift && (k === 'w' || k === 'F4')) return hacer(() => cerrarPestana(activa));
  if (c && i.shift && k === 't') return hacer(() => { const r = cerradas.pop(); if (r) nuevaPestana(r.url); });
  if (c && k === 'Tab') return hacer(() => { const n = tabs.length, j = tabs.indexOf(activa); activarPestana(tabs[(j + (i.shift ? n - 1 : 1)) % n]); });
  if (c && k === 'PageDown') return hacer(() => activarPestana(tabs[(tabs.indexOf(activa) + 1) % tabs.length]));
  if (c && k === 'PageUp') return hacer(() => activarPestana(tabs[(tabs.indexOf(activa) + tabs.length - 1) % tabs.length]));
  if (c && /^[1-9]$/.test(k)) return hacer(() => activarPestana(k === '9' ? tabs[tabs.length - 1] : tabs[+k - 1]));
  if ((c && k === 'l') || k === 'F6' || (i.alt && k === 'd')) return hacer(() => ui.send('enfocar-barra'));
  if (c && k === 'e' || c && k === 'k') return hacer(() => ui.send('enfocar-barra', '?'));
  if ((c && k === 'r') || k === 'F5') return hacer(() => activa && (i.shift || c && k === 'F5' ? activa.wc.reloadIgnoringCache() : activa.wc.reload()));
  if (i.alt && k === 'ArrowLeft' || k === 'BrowserBack') return hacer(() => activa && activa.wc.navigationHistory.goBack());
  if (i.alt && k === 'ArrowRight' || k === 'BrowserForward') return hacer(() => activa && activa.wc.navigationHistory.goForward());
  if (i.alt && k === 'Home') return hacer(() => navegar(datos.ajustes.inicio || INICIO));
  if (c && k === 'f' || k === 'F3') return hacer(() => ui.send('buscar-en-pagina', k === 'F3' ? (i.shift ? -1 : 1) : 0));
  if (c && k === 'd') return hacer(() => activa && (datos.alternarFavorito(activa.url, activa.titulo), enviarEstado()));
  if (c && k === 'h') return hacer(() => nuevaPestana(`${PROTO}://historial`));
  if (c && k === 'j') return hacer(() => nuevaPestana(`${PROTO}://descargas`));
  if (c && i.shift && k === 'o') return hacer(() => nuevaPestana(`${PROTO}://favoritos`));
  if (c && i.shift && k === 'l') return hacer(() => ui.send('claude', 'alternar'));
  if (c && (k === '+' || k === '=')) return hacer(() => zoom(1));
  if (c && k === '-') return hacer(() => zoom(-1));
  if (c && k === '0') return hacer(() => zoom(0));
  if (c && k === 'p') return hacer(() => activa && activa.wc.print());
  if (c && k === 'u') return hacer(() => activa && /^https?:/.test(activa.url) && nuevaPestana('view-source:' + activa.url));
  if (k === 'F11') return hacer(() => win.setFullScreen(!win.isFullScreen()));
  if (k === 'F12' || c && i.shift && k === 'i') return hacer(() => activa && activa.wc.toggleDevTools());
  if (k === 'Escape' && t && t.cargando) return activa.wc.stop();
}

// ===================== Menús nativos (se dibujan encima de la página) =====================
function menuContexto(t, p) {
  const wc = t.wc, m = [];
  const sep = () => m.length && m[m.length - 1].type !== 'separator' && m.push({ type: 'separator' });
  if (p.linkURL) {
    m.push({ label: 'Abrir enlace en una pestaña nueva', click: () => nuevaPestana(p.linkURL, { activar: false, despuesDe: t }) },
      { label: 'Copiar dirección del enlace', click: () => clipboard.writeText(p.linkURL) });
    sep();
  }
  if (p.mediaType === 'image' && p.srcURL) {
    m.push({ label: 'Abrir imagen en una pestaña nueva', click: () => nuevaPestana(p.srcURL, { despuesDe: t }) },
      { label: 'Guardar imagen…', click: () => wc.downloadURL(p.srcURL) },
      { label: 'Copiar imagen', click: () => wc.copyImageAt(p.x, p.y) },
      { label: 'Copiar dirección de la imagen', click: () => clipboard.writeText(p.srcURL) });
    sep();
  }
  if (p.misspelledWord && p.dictionarySuggestions.length) { for (const s of p.dictionarySuggestions.slice(0, 4)) m.push({ label: s, click: () => wc.replaceMisspelling(s) }); sep(); }
  if (p.isEditable) {
    m.push({ label: 'Deshacer', role: 'undo', enabled: p.editFlags.canUndo }, { label: 'Rehacer', role: 'redo', enabled: p.editFlags.canRedo }, { type: 'separator' },
      { label: 'Cortar', role: 'cut', enabled: p.editFlags.canCut }, { label: 'Copiar', role: 'copy', enabled: p.editFlags.canCopy },
      { label: 'Pegar', role: 'paste', enabled: p.editFlags.canPaste }, { label: 'Seleccionar todo', role: 'selectAll' });
    sep();
  } else if (p.selectionText.trim()) {
    const s = p.selectionText.trim(), corto = s.length > 30 ? s.slice(0, 30) + '…' : s;
    m.push({ label: 'Copiar', role: 'copy' },
      { label: `Buscar "${corto}"`, click: () => nuevaPestana(datos.urlBusqueda(s), { despuesDe: t }) },
      { label: 'Preguntar a Claude sobre esto', click: () => ui.send('claude', 'preguntar', s) },
      { label: 'Traducir con Claude', click: () => ui.send('claude', 'enviar', `Traduce al español (o al inglés si ya está en español):\n\n${s}`) });
    sep();
  }
  if (!p.linkURL && !p.selectionText.trim() && !p.isEditable && p.mediaType === 'none') {
    m.push({ label: 'Atrás', enabled: wc.navigationHistory.canGoBack(), click: () => wc.navigationHistory.goBack() },
      { label: 'Adelante', enabled: wc.navigationHistory.canGoForward(), click: () => wc.navigationHistory.goForward() },
      { label: 'Volver a cargar', click: () => wc.reload() }, { type: 'separator' },
      { label: 'Resumir esta página con Claude', click: () => ui.send('claude', 'enviar', 'Resume la página que estoy viendo.') },
      { label: 'Guardar página como…', click: () => guardarComo(t) },
      { label: 'Imprimir…', click: () => wc.print() });
    sep();
  }
  m.push({ label: 'Inspeccionar', click: () => wc.inspectElement(p.x, p.y) });
  Menu.buildFromTemplate(m).popup({ window: win });
}
async function guardarComo(t) {
  const r = await dialog.showSaveDialog(win, { defaultPath: path.join(app.getPath('downloads'), (t.titulo || 'pagina').replace(/[\\/:*?"<>|]/g, '_').slice(0, 80) + '.html'), filters: [{ name: 'Página web', extensions: ['html'] }] });
  if (!r.canceled && r.filePath) t.wc.savePage(r.filePath, 'HTMLComplete').catch(() => {});
}
function menuPrincipal() {
  const a = activa;
  Menu.buildFromTemplate([
    { label: 'Pestaña nueva', accelerator: 'Ctrl+T', click: () => nuevaPestana() },
    { label: 'Reabrir pestaña cerrada', accelerator: 'Ctrl+Shift+T', enabled: cerradas.length > 0, click: () => { const r = cerradas.pop(); if (r) nuevaPestana(r.url); } },
    { type: 'separator' },
    { label: 'Historial', accelerator: 'Ctrl+H', click: () => nuevaPestana(`${PROTO}://historial`) },
    { label: 'Descargas', accelerator: 'Ctrl+J', click: () => nuevaPestana(`${PROTO}://descargas`) },
    { label: 'Favoritos', accelerator: 'Ctrl+Shift+O', click: () => nuevaPestana(`${PROTO}://favoritos`) },
    { type: 'separator' },
    { label: `Zoom: ${a ? Math.round(a.wc.getZoomFactor() * 100) : 100} %`, submenu: [
      { label: 'Acercar', accelerator: 'Ctrl+Plus', click: () => zoom(1) }, { label: 'Alejar', accelerator: 'Ctrl+-', click: () => zoom(-1) }, { label: 'Tamaño normal', accelerator: 'Ctrl+0', click: () => zoom(0) }] },
    { label: 'Buscar en la página…', accelerator: 'Ctrl+F', click: () => ui.send('buscar-en-pagina', 0) },
    { label: 'Imprimir…', accelerator: 'Ctrl+P', enabled: !!a, click: () => a.wc.print() },
    { label: 'Guardar página como…', enabled: !!a && /^https?:/.test(a.url), click: () => guardarComo(a) },
    { label: 'Pantalla completa', accelerator: 'F11', click: () => win.setFullScreen(!win.isFullScreen()) },
    { type: 'separator' },
    { label: 'Panel de Claude', accelerator: 'Ctrl+Shift+L', click: () => ui.send('claude', 'alternar') },
    { label: 'Abrir carpeta de datos', click: () => shell.openPath(DIR) },
    { label: 'Herramientas para desarrolladores', accelerator: 'F12', enabled: !!a, click: () => a.wc.toggleDevTools() },
    { type: 'separator' },
    { label: 'Configuración', click: () => nuevaPestana(`${PROTO}://ajustes`) },
    { label: `Acerca de ${MARCA.nombre}`, click: () => nuevaPestana(`${PROTO}://ajustes#acerca`) },
  ]).popup({ window: win });
}
function menuPestana(id) {
  const t = tabDe(id); if (!t) return;
  const i = tabs.indexOf(t);
  Menu.buildFromTemplate([
    { label: 'Pestaña nueva a la derecha', click: () => nuevaPestana(undefined, { despuesDe: t }) },
    { label: 'Volver a cargar', click: () => t.wc.reload() },
    { label: 'Duplicar', click: () => nuevaPestana(t.url, { despuesDe: t }) },
    { label: t.wc.isAudioMuted() ? 'Activar sonido del sitio' : 'Silenciar sitio', click: () => { t.wc.setAudioMuted(!t.wc.isAudioMuted()); enviarEstado(); } },
    { type: 'separator' },
    { label: 'Cerrar', accelerator: 'Ctrl+W', click: () => cerrarPestana(t) },
    { label: 'Cerrar las demás pestañas', enabled: tabs.length > 1, click: () => tabs.filter((x) => x !== t).forEach(cerrarPestana) },
    { label: 'Cerrar las pestañas de la derecha', enabled: i < tabs.length - 1, click: () => tabs.slice(i + 1).forEach(cerrarPestana) },
  ]).popup({ window: win });
}

// ===================== Páginas internas: moja://inicio, historial, favoritos, descargas, ajustes, error =====================
const PAGINAS_OK = ['inicio', 'historial', 'favoritos', 'descargas', 'ajustes', 'error'];
function servirInterna(req) {
  const u = new URL(req.url);
  let f = u.pathname === '/' || u.pathname === '' ? (PAGINAS_OK.includes(u.hostname) ? u.hostname + '.html' : null) : u.pathname.slice(1);
  const ruta = f && path.resolve(PAGINAS, f);
  if (!ruta || !ruta.startsWith(PAGINAS + path.sep) || !fs.existsSync(ruta)) return new Response('No encontrado', { status: 404 });
  return net.fetch(pathToFileURL(ruta).toString());
}
// Lo que pueden pedir las páginas internas (solo ellas: se comprueba el origen del remitente)
const API_PAGINA = {
  marca: () => ({ nombre: MARCA.nombre, version: app.getVersion(), dir: DIR, electron: process.versions.electron, chrome: process.versions.chrome }),
  inicio: () => ({ favoritos: datos.favoritos.slice(0, 12), visitados: datos.masVisitados(8).filter((h) => !datos.esFavorito(h.url)), buscador: datos.BUSCADORES[datos.ajustes.buscador].nombre, claude: chat.encontrado() }),
  navegar: (texto) => { navegar(texto); },
  historial: (q) => datos.buscarHistorial(q, 400),
  borrarHistorial: (url) => datos.borrarHistorial(url || null),
  favoritos: () => datos.favoritos,
  quitarFavorito: (url) => { datos.quitarFavorito(url); enviarEstado(); },
  editarFavorito: (url, titulo) => datos.editarFavorito(url, titulo),
  descargas: () => descargas.map(({ item, ...d }) => d),
  abrirDescarga: (id) => { const d = descargas.find((x) => x.id === id); if (d && d.estado === 'completed') shell.openPath(d.ruta); },
  mostrarDescarga: (id) => { const d = descargas.find((x) => x.id === id); if (d) shell.showItemInFolder(d.ruta); },
  cancelarDescarga: (id) => { const d = descargas.find((x) => x.id === id); if (d && d.item) d.item.cancel(); },
  limpiarDescargas: () => { for (let i = descargas.length - 1; i >= 0; i--) if (descargas[i].estado !== 'progressing') descargas.splice(i, 1); },
  carpetaDescargas: () => shell.openPath(app.getPath('downloads')),
  ajustes: () => ({ ...datos.ajustes, buscadores: Object.fromEntries(Object.entries(datos.BUSCADORES).map(([k, v]) => [k, v.nombre])) }),
  setAjustes: (c) => { const r = datos.setAjustes(c); ui.send('ajustes', r); return r; },
  borrarDatos: async (que) => {
    if (que.includes('historial')) datos.borrarHistorial();
    if (que.includes('cookies')) await ses.clearStorageData();
    if (que.includes('cache')) await ses.clearCache();
    return true;
  },
  abrirCarpetaDatos: () => shell.openPath(DIR),
};

// ===================== Puente para Claude (servidor MCP → navegador) =====================
async function leerPagina(t, desde = 0) {
  if (!t) return { error: 'No hay pestañas' };
  const r = await t.wc.executeJavaScriptInIsolatedWorld(1001, [{ code: `(() => { const b = document.body; return { titulo: document.title, texto: b ? b.innerText : '' }; })()` }]).catch(() => null);
  const texto = (r && r.texto || '').replace(/\n{3,}/g, '\n\n'), MAX = 30000, d = Math.max(0, desde | 0);
  return { id: t.id, titulo: t.titulo, url: t.url, caracteres: texto.length, desde: d, texto: texto.slice(d, d + MAX), sigue: d + MAX < texto.length ? `La página sigue: usa desde=${d + MAX}` : undefined };
}
const HERRAMIENTAS = {
  pestanas: () => tabs.map((t) => ({ id: t.id, titulo: t.titulo, url: t.url, activa: t === activa })),
  leer_pagina: ({ id, desde }) => leerPagina(tabDe(id), desde),
  seleccion: async () => (activa ? await activa.wc.executeJavaScriptInIsolatedWorld(1001, [{ code: 'String(getSelection())' }]).catch(() => '') : '') || '(no hay texto seleccionado)',
  enlaces: async ({ id, filtro }) => {
    const t = tabDe(id); if (!t) return { error: 'No existe esa pestaña' };
    const l = await t.wc.executeJavaScriptInIsolatedWorld(1001, [{ code: `[...document.links].map(a => ({ texto: (a.innerText || a.title || '').trim().slice(0, 120), url: a.href })).filter(a => /^https?:/.test(a.url))` }]).catch(() => []);
    const f = String(filtro || '').toLowerCase();
    return (f ? l.filter((a) => a.texto.toLowerCase().includes(f) || a.url.toLowerCase().includes(f)) : l).slice(0, 300);
  },
  abrir: ({ url, nueva_pestana = true }) => { const t = navegar(url, activa, nueva_pestana !== false); return t ? `Abierto en la pestaña ${t.id}: ${t.url}` : { error: 'URL vacía' }; },
  buscar: ({ consulta }) => { const t = nuevaPestana(datos.urlBusqueda(String(consulta))); return `Búsqueda abierta en la pestaña ${t.id}`; },
  buscar_en_pagina: ({ consulta }) => new Promise((res) => {
    if (!activa) return res({ error: 'No hay pestañas' });
    const wc = activa.wc, fin = (_e, r) => { if (r.finalUpdate) { wc.off('found-in-page', fin); res(`${r.matches} coincidencia(s) de "${consulta}"`); } };
    wc.on('found-in-page', fin); wc.findInPage(String(consulta)); ui.send('buscar-en-pagina', 0, String(consulta));
    setTimeout(() => { wc.off('found-in-page', fin); res('Sin respuesta de la página'); }, 4000);
  }),
  cambiar_pestana: ({ id }) => { const t = tabDe(id); if (!t) return { error: 'No existe esa pestaña' }; activarPestana(t); return 'Listo'; },
  cerrar_pestana: ({ id }) => { const t = tabDe(id); if (!t) return { error: 'No existe esa pestaña' }; cerrarPestana(t); return 'Pestaña cerrada'; },
  atras: ({ id }) => { const t = tabDe(id); if (!t) return { error: 'No existe esa pestaña' }; t.wc.navigationHistory.goBack(); return 'Listo'; },
  recargar: ({ id }) => { const t = tabDe(id); if (!t) return { error: 'No existe esa pestaña' }; t.wc.reload(); return 'Listo'; },
  favoritos: () => datos.favoritos,
  agregar_favorito: ({ url, titulo }) => { const u = url || (activa && activa.url); if (!u) return { error: 'Falta la URL' }; if (!datos.esFavorito(u)) datos.alternarFavorito(u, titulo || (activa && activa.url === u ? activa.titulo : u)); enviarEstado(); return 'Guardado en favoritos'; },
  historial: ({ consulta, max }) => datos.buscarHistorial(consulta, Math.min(200, max || 30)).map(({ url, titulo, t }) => ({ url, titulo, fecha: new Date(t).toLocaleString('es') })),
};

let chat, puente;
const contexto = () => activa ? `Pestaña activa (id ${activa.id}): "${activa.titulo}" — ${activa.url}. Hay ${tabs.length} pestaña(s) abierta(s).` : 'No hay pestañas abiertas.';

// ===================== Arranque =====================
app.whenReady().then(async () => {
  nativeTheme.themeSource = 'dark';
  ses = session.fromPartition(TEST ? 'web-test' : 'persist:web');
  ses.protocol.handle(PROTO, servirInterna);
  protocol.handle(PROTO, servirInterna);
  ses.setUserAgent(ses.getUserAgent().replace(/\s*Electron\/\S+/, '').replace(new RegExp(`\\s*${MARCA.id}/\\S+`, 'i'), ''));
  ses.setSpellCheckerLanguages(['es', 'en-US']);

  // Permisos: lo inofensivo se permite; cámara, micrófono, ubicación, notificaciones… se preguntan
  const SIEMPRE = ['fullscreen', 'clipboard-sanitized-write', 'pointerLock', 'window-management'];
  ses.setPermissionRequestHandler((wc, permiso, cb, det) => {
    if (SIEMPRE.includes(permiso)) return cb(true);
    const o = origen(det.requestingUrl || wc.getURL());
    if (esInterna(o)) return cb(true);
    const k = o + '|' + permiso; if (permisos.has(k)) return cb(permisos.get(k));
    const t = tabs.find((x) => x.wc === wc); if (!t) return cb(false);
    const nombre = permiso === 'media' ? ((det.mediaTypes || []).includes('video') ? 'camara' : 'microfono') : permiso;
    pendientes.push({ id: crypto.randomUUID(), tabId: t.id, origen: o, permiso: nombre, clave: k, cb }); enviarEstado();
  });
  ses.setPermissionCheckHandler((_wc, permiso, o) => SIEMPRE.includes(permiso) || permisos.get(origen(o) + '|' + permiso) === true);

  // Descargas: directo a la carpeta Descargas, con progreso en la barra
  ses.on('will-download', (_e, item) => {
    const dir = app.getPath('downloads'); let nombre = item.getFilename() || 'descarga', ruta = path.join(dir, nombre), n = 1;
    const { name, ext } = path.parse(nombre);
    while (fs.existsSync(ruta)) ruta = path.join(dir, `${name} (${n++})${ext}`);
    item.setSavePath(ruta);
    const d = { id: crypto.randomUUID(), nombre: path.basename(ruta), ruta, url: item.getURL(), recibido: 0, total: item.getTotalBytes(), estado: 'progressing', t: Date.now(), item };
    descargas.unshift(d); enviarEstado();
    item.on('updated', (_e, st) => { d.recibido = item.getReceivedBytes(); d.total = item.getTotalBytes(); d.estado = st === 'interrupted' ? 'interrupted' : 'progressing'; enviarEstado(); });
    item.once('done', (_e, st) => { d.estado = st; d.recibido = item.getReceivedBytes(); d.t = Date.now(); delete d.item; enviarEstado(); setTimeout(enviarEstado, 8100); });
  });

  puente = await crearPuente(async (nombre, args) => {
    const f = HERRAMIENTAS[nombre]; if (!f) return { error: 'Herramienta desconocida' };
    const r = await f(args || {}); return r && r.error ? r : { resultado: r };
  });
  chat = crearChat({
    id: MARCA.id, nombre: MARCA.nombre, dir: DIR, contexto,
    mcp: { command: process.execPath, args: [unpack(path.join(__dirname, 'mcp.js'))], env: { ELECTRON_RUN_AS_NODE: '1', MOJA_PUENTE: String(puente.port), MOJA_TOKEN: puente.token } },
    emitir: (ev) => ui && !ui.isDestroyed() && ui.send('chat', ev),
  });

  Menu.setApplicationMenu(null);
  win = new BrowserWindow({
    width: 1400, height: 900, minWidth: 640, minHeight: 420, backgroundColor: '#0f1115', show: false,
    title: MARCA.nombre, icon: path.join(__dirname, 'build', 'icon.png'),
    titleBarStyle: 'hidden', titleBarOverlay: { color: '#0f1115', symbolColor: '#e6e8ee', height: 40 },
    webPreferences: { preload: path.join(__dirname, 'preload-ui.js'), contextIsolation: true, sandbox: true },
  });
  ui = win.webContents;
  ui.on('before-input-event', (e, i) => atajo(e, i, null));
  ui.setWindowOpenHandler(({ url }) => { if (/^https?:/.test(url)) nuevaPestana(url); return { action: 'deny' }; });
  ui.on('will-navigate', (e) => e.preventDefault());
  win.on('enter-full-screen', () => ui.send('pantalla-completa', true));
  win.on('leave-full-screen', () => ui.send('pantalla-completa', false));
  win.on('focus', () => { if (activa && !overlay) activa.wc.focus(); });
  win.on('close', () => { datos.vaciar(); });
  win.once('ready-to-show', () => { if (!TEST) win.maximize(); win.show(); });
  await win.loadFile(path.join(__dirname, 'ui', 'index.html'));

  // Pestañas iniciales: las de la última vez, o la página de inicio
  const args = process.argv.slice(app.isPackaged ? 1 : 2).filter((a) => /^(https?:|[\w-]+\.[a-z]{2,})/i.test(a));
  const prev = !TEST && datos.ajustes.restaurarPestanas && Array.isArray(datos.sesion) ? datos.sesion.filter((u) => typeof u === 'string').slice(0, 30) : [];
  if (prev.length) { prev.forEach((u, i) => nuevaPestana(u, { activar: i === 0 })); }
  for (const a of args) navegar(a, null, true);
  if (!tabs.length) nuevaPestana();
  if (TEST) require('./tests/pruebas.js')({ app, win, ui, tabs, get activa() { return activa; }, nuevaPestana, cerrarPestana, navegar, datos, HERRAMIENTAS, puente, DIR, PROTO, mcpPath: path.join(__dirname, 'mcp.js') });
});

// Desde la interfaz (barra de pestañas y de direcciones, panel de Claude)
ipcMain.handle('ui', async (e, accion, ...a) => {
  if (!ui || e.sender !== ui) return null;
  const t = activa;
  switch (accion) {
    case 'layout': rect = a[0]; colocar(); return;
    case 'overlay': {
      // La página se dibuja encima de la interfaz: para mostrar menús desplegables se cambia por una captura
      const on = !!a[0]; if (on === overlay) return null;
      let img = null;
      if (on && t) { try { img = (await t.wc.capturePage()).toDataURL(); } catch {} }
      overlay = on; mostrarActiva(); if (!on && t) t.wc.focus(); return img;
    }
    case 'nueva': return void nuevaPestana(a[0] || undefined);
    case 'cerrar': return void cerrarPestana(tabDe(a[0]));
    case 'activar': return void activarPestana(tabDe(a[0]));
    case 'mover': return void moverPestana(a[0], a[1]);
    case 'navegar': return void navegar(a[0], t, !!a[1]);
    case 'atras': return void (t && t.wc.navigationHistory.goBack());
    case 'adelante': return void (t && t.wc.navigationHistory.goForward());
    case 'recargar': return void (t && (t.cargando ? t.wc.stop() : t.wc.reload()));
    case 'inicio': return void navegar(datos.ajustes.inicio || INICIO);
    case 'favorito': if (t) { datos.alternarFavorito(t.url, t.titulo); enviarEstado(); } return;
    case 'silenciar': { const x = tabDe(a[0]); if (x) { x.wc.setAudioMuted(!x.wc.isAudioMuted()); enviarEstado(); } return; }
    case 'sugerencias': return datos.sugerencias(a[0]);
    case 'menu': return void menuPrincipal();
    case 'menuPestana': return void menuPestana(a[0]);
    case 'buscar': if (t) { if (a[0]) t.wc.findInPage(a[0], { forward: a[1] !== false, findNext: !!a[2] }); else t.wc.stopFindInPage('clearSelection'); } return;
    case 'pararBusqueda': if (t) t.wc.stopFindInPage('keepSelection'); return;
    case 'zoom': return void zoom(a[0]);
    case 'permiso': {
      const i = pendientes.findIndex((p) => p.id === a[0]); if (i < 0) return;
      const p = pendientes.splice(i, 1)[0]; permisos.set(p.clave, !!a[1]); p.cb(!!a[1]); enviarEstado(); return;
    }
    case 'descargas': return void nuevaPestana(`${PROTO}://descargas`);
    case 'ajustes': return datos.ajustes;
    case 'setAjustes': return datos.setAjustes(a[0]);
    case 'marca': return { nombre: MARCA.nombre, protocolo: PROTO, plataforma: process.platform };
    case 'chat:enviar': return chat.enviar(a[0] || {});
    case 'chat:detener': return void chat.detener();
    case 'chat:estado': return { ocupado: chat.ocupado(), encontrado: chat.encontrado() };
    case 'chat:historial': return datos.chat;
    case 'chat:guardar': return void datos.guardarChat(a[0]);
    case 'captura': { if (!t) return null; try { const img = await t.wc.capturePage(); const s = img.getSize(), k = Math.min(1, 1568 / Math.max(s.width, s.height)); return (k < 1 ? img.resize({ width: Math.round(s.width * k) }) : img).toJPEG(85).toString('base64'); } catch { return null; } }
    case 'abrirExterno': if (/^https:\/\//.test(a[0])) shell.openExternal(a[0]); return;
  }
  return null;
});
ipcMain.handle('pagina', async (e, accion, ...a) => {
  const url = e.senderFrame && e.senderFrame.url || '';
  if (!esInterna(url) || !API_PAGINA[accion]) return null;
  return API_PAGINA[accion](...a);
});

app.on('second-instance', (_e, argv) => {
  if (!win) return; if (win.isMinimized()) win.restore(); win.focus();
  for (const a of argv.slice(1).filter((x) => /^https?:/i.test(x))) nuevaPestana(a);
});
app.on('window-all-closed', () => { datos.vaciar(); if (chat) chat.limpiar(); app.quit(); });
