const ESTADO = { progressing: 'Descargando', completed: 'Completada', cancelled: 'Cancelada', interrupted: 'Interrumpida' };
async function pintar() {
  const l = await pedir('descargas');
  $('#lista').innerHTML = l.length ? l.map((d) => `<div class="fila"><span class="dim">${I(d.estado === 'completed' ? 'circle-check' : d.estado === 'progressing' ? 'download' : 'triangle-alert', 20)}</span>
    <div class="txt"><${d.estado === 'completed' ? `a href="#" data-abrir="${d.id}"` : 'span'} class="tit">${esc(d.nombre)}</${d.estado === 'completed' ? 'a' : 'span'}>
    <span class="url">${ESTADO[d.estado] || d.estado} · ${d.estado === 'progressing' ? `${tamano(d.recibido)} de ${tamano(d.total)}` : tamano(d.recibido)} · ${esc(dominio(d.url))}</span>
    ${d.estado === 'progressing' ? `<div class="barra-prog"><i style="width:${d.total ? Math.round(d.recibido / d.total * 100) : 30}%"></i></div>` : ''}</div>
    <div class="acc" style="opacity:1">${d.estado === 'progressing' ? `<button class="btn" data-cancelar="${d.id}">Cancelar</button>` : `<button class="icon-btn" data-mostrar="${d.id}" title="Mostrar en la carpeta" aria-label="Mostrar en la carpeta">${I('folder-open', 16)}</button>`}</div></div>`).join('')
    : '<div class="vacio">No hay descargas en esta sesión.</div>';
}
$('#lista').onclick = (e) => {
  const a = e.target.closest('[data-abrir]'); if (a) { e.preventDefault(); pedir('abrirDescarga', a.dataset.abrir); }
  const m = e.target.closest('[data-mostrar]'); if (m) pedir('mostrarDescarga', m.dataset.mostrar);
  const c = e.target.closest('[data-cancelar]'); if (c) pedir('cancelarDescarga', c.dataset.cancelar).then(pintar);
};
$('#carpeta').onclick = () => pedir('carpetaDescargas');
$('#limpiar').onclick = () => pedir('limpiarDescargas').then(pintar);
pintar(); setInterval(() => { if (!document.hidden) pintar(); }, 1000);
