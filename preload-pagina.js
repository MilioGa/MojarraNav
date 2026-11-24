// Se carga en todas las pestañas, pero solo expone algo a las páginas internas (moja://).
// El proceso principal vuelve a comprobar el origen de cada petición.
const { contextBridge, ipcRenderer } = require('electron');
if (location.protocol === 'moja:') {
  contextBridge.exposeInMainWorld('nav', { pedir: (accion, ...a) => ipcRenderer.invoke('pagina', accion, ...a) });
}
