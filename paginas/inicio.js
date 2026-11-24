document.querySelectorAll('[data-i]').forEach((e) => { if (!e.querySelector('svg')) e.innerHTML = I(e.dataset.i, 18); });
$('#f').onsubmit = (e) => { e.preventDefault(); const v = $('#q').value.trim(); if (v) pedir('navegar', v); };
(async () => {
  const m = await pedir('marca'); $('#nombre').textContent = m.nombre;
  const d = await pedir('inicio');
  $('#q').placeholder = `Buscar con ${d.buscador} o escribir una dirección`;
  const items = [...d.favoritos.map((f) => ({ ...f, fav: true })), ...d.visitados].slice(0, 12);
  $('#cuadros').innerHTML = items.map((x) => `<a class="cuadro" href="${esc(x.url)}" title="${esc(x.titulo)}\n${esc(x.url)}"><div class="ico">${favicon(x.url)}</div><span class="n">${esc(x.titulo && x.titulo !== x.url ? x.titulo : dominio(x.url))}</span></a>`).join('');
  if (!d.claude) $('#claude').lastElementChild.innerHTML = 'Instala la app de Claude (<a href="https://claude.com/download">claude.com/download</a>) para usar el panel de Claude: resume, compara y busca por ti mientras navegas.';
})();
