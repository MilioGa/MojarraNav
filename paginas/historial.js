async function pintar() {
  const h = await pedir('historial', $('#q').value); let html = '', dia = '';
  for (const x of h) {
    const f = fecha(x.t); if (f !== dia) { if (dia) html += '</div>'; html += `<h2>${esc(f[0].toUpperCase() + f.slice(1))}</h2><div class="lista">`; dia = f; }
    html += `<div class="fila"><span class="hora">${hora(x.t)}</span>${favicon(x.url)}<a class="txt" href="${esc(x.url)}"><span class="tit">${esc(x.titulo)}</span><span class="url">${esc(x.url)}</span></a><div class="acc"><button class="icon-btn" data-quitar="${esc(x.url)}" title="Quitar del historial" aria-label="Quitar del historial">${I('x', 16)}</button></div></div>`;
  }
  $('#lista').innerHTML = h.length ? html + '</div>' : `<div class="vacio">${$('#q').value ? 'Nada coincide con la búsqueda.' : 'Todavía no hay historial.'}</div>`;
}
$('#lista').onclick = async (e) => { const b = e.target.closest('[data-quitar]'); if (b) { await pedir('borrarHistorial', b.dataset.quitar); pintar(); } };
$('#q').oninput = pintar;
$('#borrar').onclick = async () => { if (confirm('¿Borrar todo el historial de navegación?')) { await pedir('borrarHistorial'); pintar(); } };
pintar();
