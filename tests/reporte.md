# Pruebas de MojarraNav

Fecha: 2026-11-24 14:52 · Resultado: **23/23**

- ✔ La interfaz carga con una pestaña de inicio — moja://inicio/
- ✔ La página de inicio muestra el nombre y el buscador — Buscar con Google o escribir una dirección
- ✔ La barra entiende direcciones y búsquedas
- ✔ Escribir una dirección en la barra abre la página — http://127.0.0.1:35951/
- ✔ La barra muestra la dirección y el título va en la pestaña — http://127.0.0.1:35951/
- ✔ El historial guarda la visita — Prueba Mojarra
- ✔ Las sugerencias encuentran la página visitada — 1 sugerencia(s)
- ✔ Seguir un enlace y volver atrás
- ✔ Claude puede leer la página activa — 86 caracteres
- ✔ Claude puede listar los enlaces — {"texto":"Segunda página","url":"http://127.0.0.1:35951/dos"}
- ✔ Buscar en la página cuenta coincidencias — 2 coincidencia(s) de "mojarra azul"
- ✔ Guardar en favoritos (estrella)
- ✔ Claude puede abrir una pestaña nueva — Abierto en la pestaña 2: http://127.0.0.1:35951/dos
- ✔ La pestaña nueva aparece en la tira
- ✔ Cambiar y cerrar pestañas
- ✔ Ctrl+T abre pestaña y Ctrl+W la cierra — abiertas: 2
- ✔ Página interna de favoritos lista el favorito — 1
- ✔ Página interna de historial — 2 entradas
- ✔ Las páginas web NO tienen acceso a la API interna — window.nav: undefined
- ✔ Las páginas web no pueden usar el puente de Claude — bloqueado
- ✔ Un sitio que no existe muestra la página de error en español — No se pudo abrir 127.0.0.1
- ✔ Las descargas van a la carpeta Descargas
- ✔ Servidor MCP: initialize, tools/list y tools/call — 14 herramientas
