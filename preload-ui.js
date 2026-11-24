// Puente seguro entre la interfaz del navegador (ui/) y el proceso principal
const { contextBridge, ipcRenderer } = require('electron');
contextBridge.exposeInMainWorld('nav', {
  pedir: (accion, ...a) => ipcRenderer.invoke('ui', accion, ...a),
  on: (canal, fn) => { const ok = ['estado', 'chat', 'busqueda', 'enfocar-barra', 'buscar-en-pagina', 'pantalla-completa', 'claude', 'ajustes']; if (ok.includes(canal)) ipcRenderer.on(canal, (_e, ...a) => fn(...a)); },
});
