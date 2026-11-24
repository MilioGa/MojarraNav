// Servidor MCP (stdio, sin dependencias) que da a Claude Code herramientas para usar el navegador.
// Lo lanza Claude Code con Electron en modo Node (ELECTRON_RUN_AS_NODE=1). Reenvía cada llamada al
// puente local del navegador (http://127.0.0.1:MOJA_PUENTE, con el token MOJA_TOKEN).
const http = require('http');

const PORT = Number(process.env.MOJA_PUENTE), TOKEN = process.env.MOJA_TOKEN || '';
const s = (desc, props = {}, req = []) => ({ description: desc, inputSchema: { type: 'object', properties: props, required: req } });
const id = { type: 'number', description: 'Id de la pestaña (de "pestanas"). Si falta, la pestaña activa.' };
const TOOLS = {
  pestanas: s('Lista las pestañas abiertas: id, título, URL y cuál está activa.'),
  leer_pagina: s('Devuelve el título, la URL y el texto visible de una pestaña (por defecto la activa).', { id, desde: { type: 'number', description: 'Carácter desde el que seguir leyendo una página larga' } }),
  seleccion: s('Devuelve el texto que el usuario tiene seleccionado en la pestaña activa.'),
  enlaces: s('Lista los enlaces de una pestaña (texto y URL).', { id, filtro: { type: 'string', description: 'Solo enlaces cuyo texto o URL contenga esto' } }),
  abrir: s('Abre una URL (o un texto a buscar). Por defecto en una pestaña nueva.', { url: { type: 'string' }, nueva_pestana: { type: 'boolean' } }, ['url']),
  buscar: s('Busca en la web con el buscador del usuario, en una pestaña nueva.', { consulta: { type: 'string' } }, ['consulta']),
  buscar_en_pagina: s('Resalta un texto en la pestaña activa (como Ctrl+F) y dice cuántas veces aparece.', { consulta: { type: 'string' } }, ['consulta']),
  cambiar_pestana: s('Activa una pestaña.', { id }, ['id']),
  cerrar_pestana: s('Cierra una pestaña.', { id }, ['id']),
  atras: s('Vuelve a la página anterior en una pestaña.', { id }),
  recargar: s('Recarga una pestaña.', { id }),
  favoritos: s('Lista los favoritos del usuario.'),
  agregar_favorito: s('Guarda una URL en favoritos (por defecto la de la pestaña activa).', { url: { type: 'string' }, titulo: { type: 'string' } }),
  historial: s('Busca en el historial de navegación.', { consulta: { type: 'string' }, max: { type: 'number' } }),
};

function llamar(nombre, args) {
  return new Promise((resolve) => {
    const body = JSON.stringify({ nombre, args });
    const req = http.request({ host: '127.0.0.1', port: PORT, path: '/herramienta', method: 'POST',
      headers: { 'Content-Type': 'application/json', 'X-Moja-Token': TOKEN, 'Content-Length': Buffer.byteLength(body) } }, (res) => {
      const ch = []; res.on('data', (c) => ch.push(c));
      res.on('end', () => { try { resolve(JSON.parse(Buffer.concat(ch).toString('utf8'))); } catch { resolve({ error: 'Respuesta no válida del navegador' }); } });
    });
    req.on('error', () => resolve({ error: 'El navegador no está abierto' }));
    req.setTimeout(30000, () => { req.destroy(); resolve({ error: 'El navegador tardó demasiado en responder' }); });
    req.end(body);
  });
}

const out = (o) => process.stdout.write(JSON.stringify(o) + '\n');
async function manejar(m) {
  if (m.id === undefined) return;                       // notificaciones
  if (m.method === 'initialize') return out({ jsonrpc: '2.0', id: m.id, result: { protocolVersion: m.params?.protocolVersion || '2025-06-18', capabilities: { tools: {} }, serverInfo: { name: 'navegador', version: '1.0.0' } } });
  if (m.method === 'ping') return out({ jsonrpc: '2.0', id: m.id, result: {} });
  if (m.method === 'tools/list') return out({ jsonrpc: '2.0', id: m.id, result: { tools: Object.entries(TOOLS).map(([name, t]) => ({ name, ...t })) } });
  if (m.method === 'tools/call') {
    const n = m.params?.name;
    if (!TOOLS[n]) return out({ jsonrpc: '2.0', id: m.id, result: { content: [{ type: 'text', text: 'Herramienta desconocida: ' + n }], isError: true } });
    const r = await llamar(n, m.params.arguments || {});
    const text = r.error ? r.error : typeof r.resultado === 'string' ? r.resultado : JSON.stringify(r.resultado, null, 1);
    return out({ jsonrpc: '2.0', id: m.id, result: { content: [{ type: 'text', text }], isError: !!r.error } });
  }
  out({ jsonrpc: '2.0', id: m.id, error: { code: -32601, message: 'Método no encontrado' } });
}
let buf = '';
process.stdin.setEncoding('utf8');
process.stdin.on('data', (d) => { buf += d; let i; while ((i = buf.indexOf('\n')) >= 0) { const l = buf.slice(0, i).trim(); buf = buf.slice(i + 1); if (l) { try { manejar(JSON.parse(l)); } catch {} } } });
process.stdin.on('end', () => process.exit(0));
