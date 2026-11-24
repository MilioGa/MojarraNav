const q = new URLSearchParams(location.search), u = q.get('u') || '', c = q.get('c') || '';
const host = dominio(u);
const MSG = {
  HostNameNotResolved: [`No se encuentra ${host}`, 'Revisa que la dirección esté bien escrita. Si lo está, puede que el sitio no exista o que tu conexión DNS falle.'],
  Disconnected: ['Sin conexión a internet', 'Comprueba tu Wi‑Fi o cable de red y vuelve a intentarlo.'],
  CannotConnect: [`${host} rechazó la conexión`, 'El sitio no está aceptando conexiones ahora mismo.'],
  ConnectionReset: [`Se cortó la conexión con ${host}`, 'Vuelve a intentarlo en un momento.'],
  Timeout: [`${host} tardó demasiado en responder`, 'El sitio puede estar saturado o tu conexión es lenta.'],
  ServerUnreachable: [`No se puede llegar a ${host}`, 'El servidor no responde.'],
  CertificateCommonNameIsIncorrect: ['La conexión no es privada', `El certificado de ${host} no corresponde a este sitio. Alguien podría intentar robar tus datos.`],
  CertificateExpired: ['La conexión no es privada', `El certificado de ${host} caducó. Si el reloj de tu equipo es correcto, no continúes.`],
  ClientCertificateContainsErrors: ['La conexión no es privada', `El certificado de ${host} tiene errores.`],
  CertificateRevoked: ['La conexión no es privada', `El certificado de ${host} fue revocado.`],
  CertificateIsInvalid: ['La conexión no es privada', `El certificado de ${host} no es válido.`],
  ErrorHttpInvalidServerResponse: [`${host} respondió algo que no se entiende`, 'Puede ser un problema temporal del sitio.'],
  RedirectFailed: [`${host} redirige demasiadas veces`, 'Prueba a borrar las cookies de este sitio desde la configuración.'],
  crash: ['La página dejó de funcionar', 'Algo salió mal al mostrar esta página.'],
};
const [t, d] = MSG[c] || (/^Certificate/.test(c) ? MSG.CertificateIsInvalid : [`No se pudo abrir ${host || 'la página'}`, 'Puede ser un problema temporal del sitio o de tu conexión.']);
document.title = t; $('#titulo').textContent = t; $('#detalle').textContent = d;
$('#codigo').textContent = (q.get('d') || '') + (c && c !== 'crash' ? `(${c})` : '') + (u ? ' · ' + u : '');
$('#reintentar').onclick = () => { if (/^https?:/.test(u)) location.href = u; };
