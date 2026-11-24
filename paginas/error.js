const q = new URLSearchParams(location.search), u = q.get('u') || '', c = q.get('c') || '';
const host = dominio(u);
const MSG = {
  '-105': [`No se encuentra ${host}`, 'Revisa que la dirección esté bien escrita. Si lo está, puede que el sitio no exista o que tu conexión DNS falle.'],
  '-106': ['Sin conexión a internet', 'Comprueba tu Wi‑Fi o cable de red y vuelve a intentarlo.'],
  '-102': [`${host} rechazó la conexión`, 'El sitio no está aceptando conexiones ahora mismo.'],
  '-118': [`${host} tardó demasiado en responder`, 'El sitio puede estar saturado o tu conexión es lenta.'],
  '-7': [`${host} tardó demasiado en responder`, 'El sitio puede estar saturado o tu conexión es lenta.'],
  '-201': ['El reloj de tu equipo no es correcto', 'Ajusta la fecha y hora de Windows para abrir sitios seguros.'],
  '-200': ['La conexión no es privada', `El certificado de ${host} no es válido. Alguien podría intentar robar tus datos.`],
  '-202': ['La conexión no es privada', `El certificado de ${host} no es de confianza.`],
  crash: ['La página dejó de funcionar', 'Algo salió mal al mostrar esta página.'],
};
const [t, d] = MSG[c] || (/^-2\d\d$/.test(c) ? MSG['-200'] : [`No se pudo abrir ${host || 'la página'}`, 'Puede ser un problema temporal del sitio o de tu conexión.']);
document.title = t; $('#titulo').textContent = t; $('#detalle').textContent = d;
$('#codigo').textContent = (q.get('d') || '') + (c && c !== 'crash' ? ` (${c})` : '') + (u ? ' · ' + u : '');
$('#reintentar').onclick = () => { if (/^https?:/.test(u)) location.href = u; };
