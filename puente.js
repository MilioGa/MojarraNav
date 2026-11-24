// Puente local entre el servidor MCP (mcp.js, lanzado por Claude Code) y el navegador.
// Solo escucha en 127.0.0.1 y exige un token aleatorio en cada petición: las páginas web
// abiertas en el navegador no pueden usarlo (no conocen el token y la cabecera propia exige CORS).
const http = require('http');
const crypto = require('crypto');

function crearPuente(manejar) {
  const token = crypto.randomBytes(24).toString('hex');
  const server = http.createServer((req, res) => {
    const responder = (code, obj) => { res.writeHead(code, { 'Content-Type': 'application/json; charset=utf-8' }); res.end(JSON.stringify(obj)); };
    const tk = String(req.headers['x-moja-token'] || '');
    if (req.method !== 'POST' || req.url !== '/herramienta' || tk.length !== token.length || !crypto.timingSafeEqual(Buffer.from(tk), Buffer.from(token))) return responder(403, { error: 'Prohibido' });
    const ch = []; let n = 0;
    req.on('data', (c) => { n += c.length; if (n > 1e6) req.destroy(); else ch.push(c); });
    req.on('end', async () => {
      try { const b = JSON.parse(Buffer.concat(ch).toString('utf8')); responder(200, await manejar(String(b.nombre), b.args || {})); }
      catch (e) { responder(200, { error: e.message }); }
    });
  });
  return new Promise((resolve) => server.listen(0, '127.0.0.1', () => resolve({ port: server.address().port, token, cerrar: () => server.close() })));
}
module.exports = { crearPuente };
