// Interfaz del navegador: pestañas, barra de direcciones, franjas y panel de Claude.
// Las páginas web no viven aquí: son vistas del proceso principal colocadas sobre #vista.
const $ = (s) => document.querySelector(s);
const I = (n, s = 16) => window.icon(n, s);
const esc = (s) => String(s ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);
const el = (tag, cls, html) => { const e = document.createElement(tag); if (cls) e.className = cls; if (html != null) e.innerHTML = html; return e; };
const pedir = (...a) => window.nav.pedir(...a);
document.querySelectorAll('[data-i]').forEach((e) => e.insertAdjacentHTML('afterbegin', I(e.dataset.i, e.classList.contains('sm') ? 15 : 17)));

let est = { pestanas: [], activa: null, url: '' };
let ajustes = {};
let marca = { nombre: 'MojarraNav', protocolo: 'moja' };
let toastT;
function toast(t) { const e = $('#toast'); e.textContent = t; e.classList.remove('hidden'); clearTimeout(toastT); toastT = setTimeout(() => e.classList.add('hidden'), 2600); }

// ---------- Posición de la página: se informa al proceso principal ----------
function informarLayout() { const r = $('#vista').getBoundingClientRect(); pedir('layout', { x: r.left, y: r.top, width: r.width, height: r.height }); }
new ResizeObserver(informarLayout).observe($('#vista'));
window.addEventListener('resize', informarLayout);

// ---------- Estado ----------
window.nav.on('estado', (e) => { est = e; pintarPestanas(); pintarBarra(); });
const tituloDe = (p) => p.titulo || p.url || 'Nueva pestaña';
function pintarPestanas() {
  const cont = $('#pestanas'), n = est.pestanas.length;
  const ancho = (cont.parentElement.clientWidth - 200) / Math.max(1, n);
  const viejos = new Map([...cont.children].map((c) => [+c.dataset.id, c]));
  const orden = [];
  for (const p of est.pestanas) {
    let d = viejos.get(p.id); viejos.delete(p.id);
    if (!d) d = crearPestana(p.id);
    const ico = p.cargando ? `<span class="fav spin">${I('loader-circle', 16)}</span>` : p.favicon ? `<img class="fav" src="${esc(p.favicon)}" alt="">` : `<span class="fav">${I(p.url && p.url.startsWith(marca.protocolo + ':') ? 'house' : 'globe', 16)}</span>`;
    const key = [p.cargando, p.favicon, p.titulo, p.audible, p.silencio].join('|');
    if (d.dataset.key !== key) {
      d.dataset.key = key;
      d.innerHTML = `${ico}<span class="titulo">${esc(tituloDe(p))}</span>` +
        (p.audible || p.silencio ? `<button class="icon-btn audio" title="${p.silencio ? 'Activar sonido' : 'Silenciar'}" aria-label="Silenciar">${I(p.silencio ? 'volume-x' : 'volume-2', 14)}</button>` : '') +
        `<button class="icon-btn x" title="Cerrar (Ctrl+W)" aria-label="Cerrar pestaña">${I('x', 13)}</button>`;
      const img = d.querySelector('img.fav'); if (img) img.onerror = () => img.replaceWith(el('span', 'fav', I('globe', 16)));
    }
    d.title = tituloDe(p) + (p.url ? '\n' + p.url : '');
    d.classList.toggle('activa', p.id === est.activa);
    d.setAttribute('aria-selected', p.id === est.activa);
    d.classList.toggle('estrecha', ancho < 72);
    d.querySelector('.x').classList.toggle('ver', p.id === est.activa);
    orden.push(d);
  }
  viejos.forEach((d) => d.remove());
  orden.forEach((d, i) => { if (cont.children[i] !== d) cont.insertBefore(d, cont.children[i] || null); });
  const a = est.pestanas.find((p) => p.id === est.activa);
  document.title = a ? `${tituloDe(a)} — ${marca.nombre}` : marca.nombre;
}
function crearPestana(id) {
  const d = el('div', 'pestana'); d.dataset.id = id; d.setAttribute('role', 'tab'); d.draggable = true;
  d.addEventListener('mousedown', (e) => { if (e.button === 0 && !e.target.closest('button')) pedir('activar', id); });
  d.addEventListener('auxclick', (e) => { if (e.button === 1) { e.preventDefault(); pedir('cerrar', id); } });
  d.addEventListener('click', (e) => { if (e.target.closest('.x')) pedir('cerrar', id); else if (e.target.closest('.audio')) pedir('silenciar', id); });
  d.addEventListener('contextmenu', (e) => { e.preventDefault(); pedir('menuPestana', id); });
  d.addEventListener('dragstart', (e) => { e.dataTransfer.setData('text/x-pestana', id); e.dataTransfer.effectAllowed = 'move'; d.classList.add('arrastrando'); });
  d.addEventListener('dragend', () => { d.classList.remove('arrastrando'); document.querySelectorAll('.destino').forEach((x) => x.classList.remove('destino')); });
  d.addEventListener('dragover', (e) => { if (e.dataTransfer.types.includes('text/x-pestana')) { e.preventDefault(); d.classList.add('destino'); } });
  d.addEventListener('dragleave', () => d.classList.remove('destino'));
  d.addEventListener('drop', (e) => { const de = +e.dataTransfer.getData('text/x-pestana'); if (!de) return; e.preventDefault(); d.classList.remove('destino'); pedir('mover', de, est.pestanas.findIndex((p) => p.id === id)); });
  return d;
}
$('#tira').addEventListener('dblclick', (e) => { if (e.target === $('#tira') || e.target === $('#pestanas')) pedir('nueva'); });
window.addEventListener('resize', pintarPestanas);

let editando = false;
function pintarBarra() {
  $('#bAtras').disabled = !est.puedeAtras; $('#bAdelante').disabled = !est.puedeAdelante;
  const r = $('#bRecargar'); const ic = est.cargando ? 'x' : 'rotate-cw';
  if (r.dataset.ic !== ic) { r.dataset.ic = ic; r.innerHTML = I(ic, 17); r.title = est.cargando ? 'Detener (Esc)' : 'Volver a cargar (F5)'; r.setAttribute('aria-label', r.title); }
  const inp = $('#direccion');
  if (!editando && document.activeElement !== inp) inp.value = est.url ? mostrarUrl(est.url) : '';
  const f = $('#bFav'); f.classList.toggle('on', !!est.favorito); f.title = est.favorito ? 'Quitar de favoritos (Ctrl+D)' : 'Añadir a favoritos (Ctrl+D)';
  f.disabled = !est.url;
  const o = $('#omniIco'), seg = est.url && est.seguro, ico = !est.url ? 'search' : seg ? 'lock' : 'triangle-alert';
  if (o.dataset.ic !== ico) { o.dataset.ic = ico; o.innerHTML = I(ico, 15); }
  o.classList.toggle('seguro', !!seg); o.title = !est.url ? '' : seg ? 'Conexión segura' : 'Conexión no segura: no escribas contraseñas ni datos de pago';
  const z = $('#zoomInd'); z.classList.toggle('hidden', !est.zoom || est.zoom === 100); z.textContent = est.zoom + ' %';
  const nd = $('#nDesc'); nd.classList.toggle('hidden', !est.descargas); nd.textContent = est.descargas || '';
  $('#bDescargas').classList.toggle('activa', !!est.descargas);
  if (est.descargaReciente && est.descargaReciente.nombre !== pintarBarra.ultimaDesc) {
    pintarBarra.ultimaDesc = est.descargaReciente.nombre;
    toast(est.descargaReciente.estado === 'completed' ? `Descargado: ${est.descargaReciente.nombre}` : `Descargando ${est.descargaReciente.nombre}…`);
  }
  // Permiso pendiente de la pestaña activa
  const p = est.permiso, fr = $('#permiso'), antes = !fr.classList.contains('hidden');
  if (p) {
    const que = { camara: 'usar tu cámara', microfono: 'usar tu micrófono', geolocation: 'saber tu ubicación', notifications: 'mostrarte notificaciones', 'clipboard-read': 'leer tu portapapeles',
      midi: 'usar dispositivos MIDI', 'display-capture': 'ver tu pantalla', openExternal: 'abrir otro programa', 'idle-detection': 'saber si estás usando el equipo' }[p.permiso] || `usar "${p.permiso}"`;
    $('#permisoTxt').innerHTML = `<b>${esc(p.origen.replace(/^https?:\/\//, ''))}</b> quiere ${esc(que)}`;
    fr.dataset.id = p.id;
  }
  fr.classList.toggle('hidden', !p);
  if (antes !== !!p) informarLayout();
}
// Las direcciones se muestran sin "https://" y en texto legible (acentos, espacios)
function mostrarUrl(u) { try { return decodeURI(u).replace(/^https:\/\//, '').replace(/^([^/]+)\/$/, '$1'); } catch { return u; } }
$('#permSi').onclick = () => pedir('permiso', $('#permiso').dataset.id, true);
$('#permNo').onclick = () => pedir('permiso', $('#permiso').dataset.id, false);

// ---------- Botones ----------
$('#bNueva').onclick = () => pedir('nueva');
$('#bAtras').onclick = () => pedir('atras');
$('#bAdelante').onclick = () => pedir('adelante');
$('#bRecargar').onclick = () => pedir('recargar');
$('#bInicio').onclick = () => pedir('inicio');
$('#bFav').onclick = () => { pedir('favorito'); toast(est.favorito ? 'Quitado de favoritos' : 'Guardado en favoritos'); };
$('#bDescargas').onclick = () => pedir('descargas');
$('#bMenu').onclick = () => pedir('menu');
$('#zoomInd').onclick = () => pedir('zoom', 0);
$('#bClaude').onclick = () => alternarClaude();

// ---------- Barra de direcciones con sugerencias ----------
const inp = $('#direccion');
let sugs = [], sel = -1, overlayOn = false, sugT;
inp.addEventListener('focus', () => { editando = true; setTimeout(() => inp.select(), 0); });
inp.addEventListener('blur', () => { editando = false; setTimeout(cerrarSugerencias, 120); pintarBarra(); });
inp.addEventListener('input', () => { clearTimeout(sugT); sugT = setTimeout(sugerir, 60); });
inp.addEventListener('keydown', (e) => {
  if (e.key === 'Enter') { e.preventDefault(); const s = sel >= 0 && sugs[sel]; const v = s ? s.url : inp.value; if (!v.trim()) return; cerrarSugerencias(); editando = false; pedir('navegar', v, e.altKey); inp.blur(); }
  else if (e.key === 'Escape') { if (!$('#sugerencias').classList.contains('hidden')) cerrarSugerencias(); else { inp.value = est.url ? mostrarUrl(est.url) : ''; inp.blur(); } }
  else if (e.key === 'ArrowDown' || e.key === 'ArrowUp') { if (!sugs.length) return; e.preventDefault(); sel = (sel + (e.key === 'ArrowDown' ? 1 : -1) + sugs.length + 1) % (sugs.length + 1) - (0); if (sel === sugs.length) sel = -1; marcarSel(); }
});
async function sugerir() {
  const q = inp.value.trim();
  const lista = q ? await pedir('sugerencias', q) : [];
  sugs = q ? [...lista, { url: q, titulo: `Buscar "${q}"`, buscar: true }] : [];
  sel = -1;
  if (!sugs.length || document.activeElement !== inp) return cerrarSugerencias();
  const pop = $('#sugerencias'), r = $('#omni').getBoundingClientRect();
  pop.style.left = r.left + 'px'; pop.style.top = r.bottom + 4 + 'px'; pop.style.width = r.width + 'px';
  pop.innerHTML = sugs.map((s, i) => `<div class="sug${s.fav ? ' fav' : ''}" data-i="${i}" role="option">${I(s.buscar ? 'search' : s.fav ? 'star' : 'clock', 15)}<span class="t">${esc(s.titulo)}</span>${s.buscar ? '' : `<span class="u">${esc(mostrarUrl(s.url))}</span>`}</div>`).join('');
  pop.querySelectorAll('.sug').forEach((d) => d.addEventListener('mousedown', (e) => { e.preventDefault(); const s = sugs[+d.dataset.i]; cerrarSugerencias(); editando = false; pedir('navegar', s.url); inp.blur(); }));
  await ponerOverlay(true);
  if (document.activeElement === inp && sugs.length) pop.classList.remove('hidden');
}
function marcarSel() { document.querySelectorAll('.sug').forEach((d, i) => d.classList.toggle('sel', i === sel)); if (sel >= 0 && !sugs[sel].buscar) inp.value = sugs[sel].url; }
function cerrarSugerencias() { $('#sugerencias').classList.add('hidden'); sugs = []; ponerOverlay(false); }
// Mientras hay un menú desplegable, la página se sustituye por una captura (las vistas web se dibujan encima del HTML)
let overlayPend = Promise.resolve();
function ponerOverlay(on) {
  overlayPend = overlayPend.then(async () => {
    if (on === overlayOn) return; overlayOn = on;
    const img = await pedir('overlay', on);
    $('#vista').style.backgroundImage = on && img ? `url("${img}")` : '';
  });
  return overlayPend;
}
window.nav.on('enfocar-barra', (pre) => { inp.focus(); if (pre) { inp.value = ''; } inp.select(); });

// ---------- Buscar en la página ----------
const bq = $('#busqTxt');
function abrirBusqueda(dir, texto) {
  const fr = $('#busqueda'), estaba = !fr.classList.contains('hidden');
  fr.classList.remove('hidden'); if (!estaba) informarLayout();
  if (texto != null) bq.value = texto;
  bq.focus(); bq.select();
  if (dir && bq.value) pedir('buscar', bq.value, dir > 0, true);
}
function cerrarBusqueda() { $('#busqueda').classList.add('hidden'); pedir('buscar', ''); $('#busqN').textContent = ''; informarLayout(); }
bq.addEventListener('input', () => { if (bq.value) pedir('buscar', bq.value, true, false); else { pedir('buscar', ''); $('#busqN').textContent = ''; bq.classList.remove('nada'); } });
bq.addEventListener('keydown', (e) => { if (e.key === 'Enter') pedir('buscar', bq.value, !e.shiftKey, true); if (e.key === 'Escape') cerrarBusqueda(); });
$('#busqSig').onclick = () => bq.value && pedir('buscar', bq.value, true, true);
$('#busqAnt').onclick = () => bq.value && pedir('buscar', bq.value, false, true);
$('#busqX').onclick = cerrarBusqueda;
window.nav.on('buscar-en-pagina', (dir, texto) => abrirBusqueda(dir, texto));
window.nav.on('busqueda', (r) => { $('#busqN').textContent = bq.value ? `${r.i} de ${r.n}` : ''; bq.classList.toggle('nada', !!bq.value && !r.n); });

// ---------- Pantalla completa (F11 o video en pantalla completa) ----------
window.nav.on('pantalla-completa', (on) => { document.body.classList.toggle('completa', on); setTimeout(informarLayout, 30); });

// ---------- Panel de Claude ----------
const MODELOS = [['', 'Modelo predeterminado'], ['fable', 'Fable (el más capaz)'], ['opus', 'Opus'], ['sonnet', 'Sonnet (equilibrado)'], ['haiku', 'Haiku (rápido)']];
const ESFUERZOS = [['', 'Esfuerzo auto'], ['low', 'Esfuerzo bajo'], ['medium', 'Esfuerzo medio'], ['high', 'Esfuerzo alto'], ['xhigh', 'Esfuerzo muy alto'], ['max', 'Esfuerzo máximo']];
const IDEAS = ['Resume esta página', 'Explícame esto como si tuviera 12 años', 'Busca 3 reseñas de esto y compáralas', 'Traduce esta página al español', 'Abre en pestañas los 5 enlaces más importantes'];
let cc = { busy: false, session: null, log: [], imgs: [], bot: null, typing: null };
function alternarClaude(mostrar) {
  const on = mostrar === undefined ? document.body.classList.contains('sin-claude') : mostrar;
  document.body.classList.toggle('sin-claude', !on); $('#bClaude').classList.toggle('on', on);
  pedir('setAjustes', { panelClaude: on }); informarLayout();
  if (on) setTimeout(() => $('#clTexto').focus(), 30);
}
$('#clCerrar').onclick = () => alternarClaude(false);
window.nav.on('claude', (accion, texto) => {
  if (accion === 'alternar') return alternarClaude();
  alternarClaude(true);
  if (accion === 'preguntar') { const t = $('#clTexto'); t.value = `Sobre este texto de la página:\n"${texto}"\n\n`; t.focus(); t.setSelectionRange(t.value.length, t.value.length); }
  if (accion === 'enviar') enviar(texto);
});
// Divisor para cambiar el ancho del panel
$('#divisor').addEventListener('pointerdown', (e) => {
  const d = $('#divisor'); d.setPointerCapture(e.pointerId); d.classList.add('activo');
  const mv = (ev) => { const w = Math.max(280, Math.min(900, window.innerWidth - ev.clientX)); $('#claude').style.width = w + 'px'; informarLayout(); };
  const up = () => { d.classList.remove('activo'); d.removeEventListener('pointermove', mv); d.removeEventListener('pointerup', up); pedir('setAjustes', { anchoClaude: $('#claude').offsetWidth }); };
  d.addEventListener('pointermove', mv); d.addEventListener('pointerup', up);
});
for (const [id, lista, k] of [['#clModelo', MODELOS, 'modelo'], ['#clEsfuerzo', ESFUERZOS, 'esfuerzo']]) {
  const s = $(id); s.innerHTML = lista.map(([v, n]) => `<option value="${v}">${n}</option>`).join('');
  s.onchange = () => pedir('setAjustes', { [k]: s.value });
}
function md(t) {
  return esc(t).replace(/`([^`\n]+)`/g, '<code>$1</code>').replace(/\*\*([^*\n]+)\*\*/g, '<b>$1</b>')
    .replace(/\[([^\]\n]+)\]\((https?:\/\/[^\s)]+)\)/g, '<a href="$2" data-url="$2">$1</a>')
    .replace(/(^|[\s(])(https?:\/\/[^\s<)]+)/g, '$1<a href="$2" data-url="$2">$2</a>');
}
$('#clLog').addEventListener('click', (e) => { const a = e.target.closest('a[data-url]'); if (a) { e.preventDefault(); pedir('nueva', a.dataset.url); } });
function entrada(e) {
  if (e.r === 'user') return el('div', 'cl-msg user', (e.imgs || []).map((s) => `<img src="${s}" alt="">`).join('') + esc(e.t));
  if (e.r === 'bot') return el('div', 'cl-msg bot', md(e.t));
  if (e.r === 'tool') return el('div', 'cl-tool', I('sparkles', 13) + esc(e.t));
  if (e.r === 'err') return el('div', 'cl-msg err', e.html || esc(e.t));
  return el('div', 'dim small', esc(e.t));
}
function pintarChat() {
  const log = $('#clLog'); log.replaceChildren();
  if (!cc.log.length) {
    log.innerHTML = `<div class="cl-vacio">${I('sparkles', 28)}<p><b>Claude navega contigo.</b><br>Puede leer la página que estás viendo, buscar, abrir pestañas y guardar favoritos.</p><div class="chips">${IDEAS.map((t) => `<button class="chip">${t}</button>`).join('')}</div></div>`;
    log.querySelectorAll('.chip').forEach((b) => (b.onclick = () => enviar(b.textContent)));
  } else cc.log.forEach((e) => log.append(entrada(e)));
  if (cc.busy) escribiendo();
  log.scrollTop = log.scrollHeight;
}
let guardarT;
function guardarChat() { clearTimeout(guardarT); guardarT = setTimeout(() => pedir('chat:guardar', { session: cc.session, log: cc.log.slice(-200).map(({ node, ...e }) => e) }), 400); }
function empujar(e) {
  const log = $('#clLog'); if (log.querySelector('.cl-vacio')) log.replaceChildren();
  cc.log.push(e); const n = entrada(e); log.insertBefore(n, cc.typing && cc.typing.isConnected ? cc.typing : null); log.scrollTop = log.scrollHeight; guardarChat(); return n;
}
function escribiendo() { if (!cc.typing) cc.typing = el('div', 'cl-typing', '<i></i><i></i><i></i>'); $('#clLog').append(cc.typing); }
function ocupado(b) {
  cc.busy = b; const s = $('#clEnviar'); s.innerHTML = b ? I('square', 14) + 'Detener' : I('send', 14) + 'Enviar';
  $('#clEstado').textContent = b ? 'trabajando…' : '';
  if (b) escribiendo(); else { cc.typing && cc.typing.remove(); cc.bot = null; }
}
function adjuntar(dataUrl) { if (cc.imgs.length >= 8) return toast('Máximo 8 imágenes por mensaje'); cc.imgs.push(dataUrl); miniaturas(); }
function adjuntarArchivo(f) {
  if (!/^image\/(png|jpeg|gif|webp)$/.test(f.type)) return toast('Solo imágenes PNG, JPG, GIF o WebP');
  const img = new Image(), url = URL.createObjectURL(f);
  img.onload = () => { // se reduce a 1568 px como máximo (lo que Claude aprovecha)
    const k = Math.min(1, 1568 / Math.max(img.width, img.height)), c = document.createElement('canvas');
    c.width = Math.round(img.width * k); c.height = Math.round(img.height * k); c.getContext('2d').drawImage(img, 0, 0, c.width, c.height);
    URL.revokeObjectURL(url); adjuntar(c.toDataURL(f.type === 'image/png' ? 'image/png' : 'image/jpeg', 0.88));
  };
  img.onerror = () => { URL.revokeObjectURL(url); toast('No se pudo leer la imagen'); }; img.src = url;
}
function miniaturas() {
  $('#clThumbs').innerHTML = cc.imgs.map((s, i) => `<div class="cl-thumb"><img src="${s}" alt=""><button data-k="${i}" title="Quitar" aria-label="Quitar imagen">×</button></div>`).join('');
  $('#clThumbs').querySelectorAll('button').forEach((b) => (b.onclick = () => { cc.imgs.splice(+b.dataset.k, 1); miniaturas(); }));
}
$('#clArchivo').onchange = (e) => { [...e.target.files].forEach(adjuntarArchivo); e.target.value = ''; };
$('#clCaptura').onclick = async () => { const b64 = await pedir('captura'); if (b64) { adjuntar('data:image/jpeg;base64,' + b64); toast('Captura de la página adjuntada'); } else toast('No se pudo capturar la página'); };
$('#clTexto').addEventListener('paste', (e) => { const fl = [...(e.clipboardData?.files || [])].filter((f) => f.type.startsWith('image/')); if (fl.length) { e.preventDefault(); fl.forEach(adjuntarArchivo); } });
const clIn = $('#clInput');
clIn.addEventListener('dragover', (e) => { if ([...e.dataTransfer.types].includes('Files')) { e.preventDefault(); clIn.classList.add('drop'); } });
clIn.addEventListener('dragleave', () => clIn.classList.remove('drop'));
clIn.addEventListener('drop', (e) => { clIn.classList.remove('drop'); const fl = [...e.dataTransfer.files].filter((f) => f.type.startsWith('image/')); if (fl.length) { e.preventDefault(); fl.forEach(adjuntarArchivo); } });
$('#clTexto').addEventListener('keydown', (e) => { if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); enviar(); } });
$('#clEnviar').onclick = () => (cc.busy ? pedir('chat:detener') : enviar());
$('#clNuevo').onclick = () => { if (cc.busy) return toast('Espera a que Claude termine o detenlo'); cc.session = null; cc.log = []; guardarChat(); pintarChat(); toast('Conversación nueva'); };
async function enviar(textoDirecto) {
  const ta = $('#clTexto'), text = (textoDirecto ?? ta.value).trim();
  if ((!text && !cc.imgs.length) || cc.busy) return;
  const imgs = cc.imgs; empujar({ r: 'user', t: text, imgs }); if (textoDirecto == null) ta.value = ''; cc.imgs = []; miniaturas(); ocupado(true);
  let r; try { r = await pedir('chat:enviar', { text, images: imgs, model: $('#clModelo').value, effort: $('#clEsfuerzo').value, session: cc.session }); } catch (e) { r = { ok: false, error: e.message }; }
  if (!r || !r.ok) {
    ocupado(false);
    if (r && r.found === false) empujar({ r: 'err', t: 'No encontré Claude Code en este equipo.', html: 'No encontré Claude Code en este equipo. Instala la app de Claude desde <a href="https://claude.com/download" data-url="https://claude.com/download">claude.com/download</a> e inicia sesión; luego vuelve a intentarlo.' });
    else empujar({ r: 'err', t: (r && r.error) || 'No se pudo enviar' });
  }
}
window.nav.on('chat', (d) => {
  if (d.k === 'start') { if (!cc.busy) ocupado(true); }
  else if (d.k === 'session') { cc.session = d.session; guardarChat(); }
  else if (d.k === 'msg') cc.bot = null;
  else if (d.k === 'text') {
    if (!cc.bot) { cc.bot = { r: 'bot', t: '' }; cc.bot.node = empujar(cc.bot); }
    cc.bot.t += d.t; cc.bot.node.innerHTML = md(cc.bot.t); const log = $('#clLog'); log.scrollTop = log.scrollHeight;
  }
  else if (d.k === 'tool') { cc.bot = null; empujar({ r: 'tool', t: d.d }); }
  else if (d.k === 'done') { if (d.session) cc.session = d.session; if (d.error) empujar({ r: 'err', t: d.error }); ocupado(false); guardarChat(); }
});
window.nav.on('ajustes', (a) => { ajustes = a; });

// ---------- Inicio ----------
(async () => {
  marca = await pedir('marca') || marca;
  ajustes = await pedir('ajustes') || {};
  $('#clModelo').value = ajustes.modelo || ''; $('#clEsfuerzo').value = ajustes.esfuerzo || '';
  $('#claude').style.width = (ajustes.anchoClaude || 380) + 'px';
  document.body.classList.toggle('sin-claude', ajustes.panelClaude === false); $('#bClaude').classList.toggle('on', ajustes.panelClaude !== false);
  const h = await pedir('chat:historial') || {}; cc.session = h.session || null; cc.log = h.log || [];
  const s = await pedir('chat:estado'); ocupado(!!(s && s.ocupado));
  pintarChat(); informarLayout();
})();
