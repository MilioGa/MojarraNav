(async () => {
  const a = await pedir('ajustes'), m = await pedir('marca');
  $('#buscador').innerHTML = Object.entries(a.buscadores).map(([k, n]) => `<option value="${k}">${esc(n)}</option>`).join('');
  $('#buscador').value = a.buscador; $('#restaurarPestanas').checked = a.restaurarPestanas; $('#panelClaude').checked = a.panelClaude; $('#bloqueador').checked = a.bloqueador;
  if (a.bloqEstado) $('#bloqEstado').textContent = a.bloqEstado + (a.sitiosSinBloqueo?.length ? ` · Desactivado en: ${a.sitiosSinBloqueo.join(', ')}` : '');
  $('#buscador').onchange = () => pedir('setAjustes', { buscador: $('#buscador').value });
  for (const k of ['restaurarPestanas', 'panelClaude', 'bloqueador']) $('#' + k).onchange = () => pedir('setAjustes', { [k]: $('#' + k).checked });
  document.querySelectorAll('[data-borrar]').forEach((b) => (b.onclick = async () => { b.disabled = true; await pedir('borrarDatos', [b.dataset.borrar]); b.textContent = 'Hecho'; }));
  $('#info').innerHTML = `<p style="margin:0 0 6px"><b>${esc(m.nombre)}</b> ${esc(m.version)} · parte de Mojarrium</p>
    <p class="dim" style="margin:0 0 6px">Motor Microsoft Edge WebView2 ${esc(m.motor || '')}</p>
    <p class="dim" style="margin:0">Tus favoritos, historial y chat se guardan en <code>${esc(m.dir)}</code> <a href="#" id="abrir">Abrir carpeta</a></p>`;
  $('#predeterminado').onclick = () => pedir('predeterminado');
  $('#abrir').onclick = (e) => { e.preventDefault(); pedir('abrirCarpetaDatos'); };
})();
