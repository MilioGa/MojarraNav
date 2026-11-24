let todos = [];
async function cargar() { todos = await pedir('favoritos'); pintar(); }
function pintar() {
  const q = $('#q').value.toLowerCase(); const l = todos.filter((f) => !q || f.url.toLowerCase().includes(q) || (f.titulo || '').toLowerCase().includes(q));
  $('#lista').innerHTML = l.length ? l.map((f) => `<div class="fila">${favicon(f.url)}<a class="txt" href="${esc(f.url)}"><span class="tit">${esc(f.titulo)}</span><span class="url">${esc(f.url)}</span></a><div class="acc">
    <button class="icon-btn" data-editar="${esc(f.url)}" title="Cambiar nombre" aria-label="Cambiar nombre">${I('pencil', 16)}</button>
    <button class="icon-btn" data-quitar="${esc(f.url)}" title="Quitar" aria-label="Quitar de favoritos">${I('trash-2', 16)}</button></div></div>`).join('')
    : `<div class="vacio">${q ? 'Nada coincide con la búsqueda.' : 'Aún no tienes favoritos.'}</div>`;
}
$('#lista').onclick = async (e) => {
  const q = e.target.closest('[data-quitar]'); if (q) { await pedir('quitarFavorito', q.dataset.quitar); return cargar(); }
  const ed = e.target.closest('[data-editar]');
  if (ed) { const f = todos.find((x) => x.url === ed.dataset.editar); const t = ed.closest('.fila').querySelector('.tit');
    t.contentEditable = 'plaintext-only'; t.focus(); document.getSelection().selectAllChildren(t);
    const fin = async (ok) => { t.contentEditable = 'false'; t.onblur = t.onkeydown = null; if (ok && t.textContent.trim()) await pedir('editarFavorito', f.url, t.textContent.trim()); cargar(); };
    t.onkeydown = (ev) => { if (ev.key === 'Enter') { ev.preventDefault(); fin(true); } if (ev.key === 'Escape') fin(false); }; t.onblur = () => fin(true);
    e.preventDefault(); }
};
$('#lista').addEventListener('click', (e) => { if (e.target.closest('[contenteditable="plaintext-only"]')) e.preventDefault(); }, true);
$('#q').oninput = pintar;
cargar();
