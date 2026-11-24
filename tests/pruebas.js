// Pruebas de extremo a extremo (`npm test`): abre el navegador de verdad con datos temporales,
// navega por un sitio local, usa las herramientas de Claude a través del servidor MCP y escribe tests/reporte.md.
const http = require('http');
const fs = require('fs');
const path = require('path');
const { spawn } = require('child_process');
const { aUrl } = require('../datos.js');

const espera = (ms) => new Promise((r) => setTimeout(r, ms));
async function hasta(fn, ms = 8000) { const t0 = Date.now(); while (Date.now() - t0 < ms) { try { if (await fn()) return true; } catch {} await espera(50); } return false; }

module.exports = async function (ctx) {
  const { app, win, ui, tabs, datos, HERRAMIENTAS, puente, PROTO } = ctx;
  const res = [];
  const prueba = async (nombre, fn) => { try { const r = await fn(); res.push({ nombre, ok: r !== false && !(r && r.ok === false), detalle: r && r.detalle }); } catch (e) { res.push({ nombre, ok: false, detalle: e.message }); } };

  // Sitio local de prueba
  const PAGINAS = {
    '/': '<title>Prueba Mojarra</title><h1>Hola</h1><p>La mojarra azul nada en el arrecife. Otra mojarra azul la sigue.</p><a href="/dos">Segunda página</a>',
    '/dos': '<title>Página dos</title><p>Segunda</p>',
    '/descarga': null,
  };
  const srv = http.createServer((q, r) => {
    if (q.url === '/descarga') { r.writeHead(200, { 'Content-Type': 'application/octet-stream', 'Content-Disposition': 'attachment; filename="prueba.txt"' }); return r.end('hola'); }
    const h = PAGINAS[q.url]; r.writeHead(h ? 200 : 404, { 'Content-Type': 'text/html; charset=utf-8' }); r.end('<!doctype html><meta charset="utf-8">' + (h || 'no'));
  });
  await new Promise((r) => srv.listen(0, '127.0.0.1', r));
  const BASE = `http://127.0.0.1:${srv.address().port}`;
  const activa = () => ctx.activa;

  await prueba('La interfaz carga con una pestaña de inicio', async () => {
    await hasta(() => activa() && !activa().cargando && activa().wc.getURL().startsWith(PROTO + '://inicio'));
    return { ok: tabs.length === 1 && (await ui.executeJavaScript('document.querySelectorAll(".pestana").length')) === 1, detalle: activa().wc.getURL() };
  });
  await prueba('La página de inicio muestra el nombre y el buscador', async () => {
    await hasta(async () => (await activa().wc.executeJavaScript('document.querySelector("#nombre").textContent')) === 'MojarraNav');
    const ph = await activa().wc.executeJavaScript('document.querySelector("#q").placeholder');
    return { ok: /Google/.test(ph), detalle: ph };
  });
  await prueba('La barra entiende direcciones y búsquedas', () => {
    const b = (q) => 'B:' + q;
    const ok = aUrl('google.com', b) === 'https://google.com' && aUrl('hola mundo', b) === 'B:hola mundo' && aUrl('localhost:3000', b) === 'http://localhost:3000' && aUrl(`${PROTO}://Historial`, b) === `${PROTO}://historial`;
    return ok;
  });
  await prueba('Escribir una dirección en la barra abre la página', async () => {
    await ui.executeJavaScript(`(() => { const i = document.querySelector('#direccion'); i.focus(); i.value = ${JSON.stringify(BASE + '/')}; i.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true })); })()`);
    await hasta(() => activa().titulo === 'Prueba Mojarra');
    return { ok: activa().titulo === 'Prueba Mojarra', detalle: activa().url };
  });
  await prueba('La barra muestra la dirección y el título va en la pestaña', async () => {
    await hasta(async () => (await ui.executeJavaScript('document.querySelector(".pestana.activa .titulo").textContent')) === 'Prueba Mojarra');
    const v = await ui.executeJavaScript('document.querySelector("#direccion").value');
    return { ok: v.includes('127.0.0.1'), detalle: v };
  });
  await prueba('El historial guarda la visita', () => ({ ok: datos.historial[0] && datos.historial[0].url === BASE + '/' && datos.historial[0].titulo === 'Prueba Mojarra', detalle: datos.historial[0] && datos.historial[0].titulo }));
  await prueba('Las sugerencias encuentran la página visitada', async () => { const s = datos.sugerencias('mojarra'); return { ok: s.length > 0 && s[0].url === BASE + '/', detalle: s.length + ' sugerencia(s)' }; });
  await prueba('Seguir un enlace y volver atrás', async () => {
    await activa().wc.executeJavaScript('document.querySelector("a").click()', true);
    await hasta(() => activa().titulo === 'Página dos');
    activa().wc.navigationHistory.goBack();
    await hasta(() => activa().titulo === 'Prueba Mojarra');
    return activa().titulo === 'Prueba Mojarra';
  });

  // Herramientas de Claude (directo)
  await prueba('Claude puede leer la página activa', async () => { const r = await HERRAMIENTAS.leer_pagina({}); return { ok: r.texto.includes('mojarra azul'), detalle: r.caracteres + ' caracteres' }; });
  await prueba('Claude puede listar los enlaces', async () => { const r = await HERRAMIENTAS.enlaces({}); return { ok: r.length === 1 && r[0].url === BASE + '/dos', detalle: JSON.stringify(r[0]) }; });
  await prueba('Buscar en la página cuenta coincidencias', async () => { const r = await HERRAMIENTAS.buscar_en_pagina({ consulta: 'mojarra azul' }); ctx.ui.send('buscar-en-pagina', 0, ''); return { ok: /^2 /.test(r), detalle: r }; });
  await prueba('Guardar en favoritos (estrella)', async () => {
    await ui.executeJavaScript('document.querySelector("#bFav").click()');
    await hasta(() => datos.esFavorito(BASE + '/'));
    await hasta(async () => ui.executeJavaScript('document.querySelector("#bFav").classList.contains("on")'));
    return datos.esFavorito(BASE + '/') && (await ui.executeJavaScript('document.querySelector("#bFav").classList.contains("on")'));
  });
  await prueba('Claude puede abrir una pestaña nueva', async () => {
    const r = HERRAMIENTAS.abrir({ url: BASE + '/dos' });
    await hasta(() => tabs.length === 2 && activa().titulo === 'Página dos');
    return { ok: tabs.length === 2, detalle: r };
  });
  await prueba('La pestaña nueva aparece en la tira', async () => { await hasta(async () => (await ui.executeJavaScript('document.querySelectorAll(".pestana").length')) === 2); return (await ui.executeJavaScript('document.querySelectorAll(".pestana").length')) === 2; });
  await prueba('Cambiar y cerrar pestañas', async () => {
    const [a, b] = tabs; HERRAMIENTAS.cambiar_pestana({ id: a.id }); const ok1 = activa() === a;
    HERRAMIENTAS.cerrar_pestana({ id: b.id }); return ok1 && tabs.length === 1;
  });
  await prueba('Ctrl+T abre pestaña y Ctrl+W la cierra', async () => {
    ui.focus(); ui.sendInputEvent({ type: 'keyDown', keyCode: 'T', modifiers: ['control'] });
    await hasta(() => tabs.length === 2);
    const abierta = tabs.length === 2;
    ui.sendInputEvent({ type: 'keyDown', keyCode: 'W', modifiers: ['control'] });
    await hasta(() => tabs.length === 1);
    return { ok: abierta && tabs.length === 1, detalle: `abiertas: ${abierta ? 2 : tabs.length}` };
  });
  await prueba('Página interna de favoritos lista el favorito', async () => {
    const t = ctx.nuevaPestana(`${PROTO}://favoritos`);
    await hasta(async () => (await t.wc.executeJavaScript('document.querySelectorAll(".fila").length')) === 1);
    const txt = await t.wc.executeJavaScript('document.querySelector("#lista").innerText');
    ctx.cerrarPestana(t); return { ok: txt.includes('Prueba Mojarra'), detalle: txt.split('\n')[0] };
  });
  await prueba('Página interna de historial', async () => {
    const t = ctx.nuevaPestana(`${PROTO}://historial`);
    await hasta(async () => (await t.wc.executeJavaScript('document.querySelectorAll(".fila").length')) >= 2);
    const n = await t.wc.executeJavaScript('document.querySelectorAll(".fila").length'); ctx.cerrarPestana(t); return { ok: n >= 2, detalle: n + ' entradas' };
  });
  await prueba('Las páginas web NO tienen acceso a la API interna', async () => { const r = await activa().wc.executeJavaScript('typeof window.nav'); return { ok: r === 'undefined', detalle: 'window.nav: ' + r }; });
  await prueba('Las páginas web no pueden usar el puente de Claude', async () => {
    const r = await activa().wc.executeJavaScript(`fetch('http://127.0.0.1:${puente.port}/herramienta', { method: 'POST', body: '{"nombre":"pestanas"}' }).then(r => r.status).catch(() => 'bloqueado')`);
    return { ok: r === 403 || r === 'bloqueado', detalle: String(r) };
  });
  await prueba('Un sitio que no existe muestra la página de error en español', async () => {
    ctx.navegar('http://127.0.0.1:1/');
    await hasta(() => activa().wc.getURL().startsWith(PROTO + '://error'));
    await hasta(async () => (await activa().wc.executeJavaScript('document.querySelector("#titulo").textContent')).length > 5);
    const t = await activa().wc.executeJavaScript('document.querySelector("#titulo").textContent');
    return { ok: activa().wc.getURL().startsWith(PROTO + '://error'), detalle: t };
  });
  await prueba('Las descargas van a la carpeta Descargas', async () => {
    app.setPath('downloads', path.join(ctx.DIR, 'descargas')); fs.mkdirSync(path.join(ctx.DIR, 'descargas'), { recursive: true });
    activa().wc.downloadURL(BASE + '/descarga');
    const f = path.join(ctx.DIR, 'descargas', 'prueba.txt');
    await hasta(() => fs.existsSync(f) && fs.readFileSync(f, 'utf8') === 'hola');
    return fs.existsSync(f);
  });

  // Servidor MCP de verdad (como lo lanza Claude Code)
  await prueba('Servidor MCP: initialize, tools/list y tools/call', async () => {
    ctx.navegar(BASE + '/'); await hasta(() => activa().titulo === 'Prueba Mojarra');
    const p = spawn(process.execPath, [ctx.mcpPath], { env: { ...process.env, ELECTRON_RUN_AS_NODE: '1', MOJA_PUENTE: String(puente.port), MOJA_TOKEN: puente.token }, stdio: ['pipe', 'pipe', 'inherit'] });
    let buf = ''; const resp = {};
    p.stdout.on('data', (d) => { buf += d; let i; while ((i = buf.indexOf('\n')) >= 0) { const o = JSON.parse(buf.slice(0, i)); buf = buf.slice(i + 1); resp[o.id] = o; } });
    const w = (o) => p.stdin.write(JSON.stringify({ jsonrpc: '2.0', ...o }) + '\n');
    w({ id: 1, method: 'initialize', params: { protocolVersion: '2025-06-18', capabilities: {}, clientInfo: { name: 't', version: '1' } } });
    w({ method: 'notifications/initialized' });
    w({ id: 2, method: 'tools/list' });
    w({ id: 3, method: 'tools/call', params: { name: 'leer_pagina', arguments: {} } });
    await hasta(() => resp[3], 10000); p.kill();
    const n = resp[2] && resp[2].result.tools.length, txt = resp[3] && resp[3].result.content[0].text;
    return { ok: resp[1] && resp[1].result.serverInfo.name === 'navegador' && n === 14 && txt.includes('mojarra azul'), detalle: `${n} herramientas` };
  });

  // Capturas para revisar a ojo
  const out = path.join(__dirname, 'salida'); fs.mkdirSync(out, { recursive: true });
  try { fs.writeFileSync(path.join(out, 'interfaz.png'), (await win.webContents.capturePage()).toPNG()); fs.writeFileSync(path.join(out, 'pagina.png'), (await activa().wc.capturePage()).toPNG()); } catch {}
  const t = ctx.nuevaPestana(); await hasta(() => !t.cargando && t.wc.getURL().startsWith(PROTO));
  await espera(400); try { fs.writeFileSync(path.join(out, 'inicio.png'), (await t.wc.capturePage()).toPNG()); } catch {}

  const ok = res.filter((r) => r.ok).length;
  const md = [`# Pruebas de MojarraNav`, '', `Fecha: ${new Date().toISOString().slice(0, 16).replace('T', ' ')} · Resultado: **${ok}/${res.length}**`, '',
    ...res.map((r) => `- ${r.ok ? '✔' : '✘'} ${r.nombre}${r.detalle ? ` — ${typeof r.detalle === 'string' ? r.detalle : JSON.stringify(r.detalle)}` : ''}`), ''].join('\n');
  fs.writeFileSync(path.join(__dirname, 'reporte.md'), md);
  console.log('\n' + md);
  srv.close();
  setTimeout(() => app.exit(ok === res.length ? 0 : 1), 300);
};
