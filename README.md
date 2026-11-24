<div align="center">

<img src="build/icon.png" width="110" alt="MojarraNav">

# MojarraNav

**El navegador web de Mojarrium, con Claude navegando contigo.**
Pregúntale por la página que estás viendo, pídele que busque, compare o abra pestañas por ti.

![Versión](https://img.shields.io/badge/versión-0.3.0-7c6cff?style=flat-square)
![Windows](https://img.shields.io/badge/Windows-10%20%7C%2011-0078d4?style=flat-square&logo=windows&logoColor=white)
![.NET](https://img.shields.io/badge/.NET_8-WPF-512bd4?style=flat-square&logo=dotnet&logoColor=white)
![Motor](https://img.shields.io/badge/motor-Edge_WebView2-0078d7?style=flat-square&logo=microsoftedge&logoColor=white)
![Claude](https://img.shields.io/badge/Claude-integrado-d97757?style=flat-square&logo=anthropic&logoColor=white)

</div>

---

> *"Resume esta página y busca dos reseñas que digan lo contrario."*
>
> Escríbelo en el panel de Claude: lee la pestaña, busca y te abre lo que encuentre.

## ✨ Características

- **Bloqueador de anuncios y rastreadores** con las listas de uBlock Origin y Brave (EasyList, EasyPrivacy, filtros de uBlock, Peter Lowe, EasyList en español): bloquea peticiones, oculta huecos de anuncios y quita los anuncios de YouTube. Se desactiva por sitio o del todo desde el escudo de la barra o `moja://ajustes`.
- **Pestañas** con arrastrar (animado) para reordenar, clic central para cerrar, silenciar sitios y reabrir la última cerrada.
- **Barra de direcciones inteligente**: escribe una dirección o una búsqueda; sugiere tus favoritos y lo más visitado.
- **Favoritos, historial y descargas** con lista rápida en la barra (botón "Mostrar más") y en páginas propias (`moja://favoritos`, `moja://historial`, `moja://descargas`).
- **Buscar en la página**, zoom, imprimir, pantalla completa y herramientas de desarrollador (las de Edge).
- **Permisos bajo control**: cámara, micrófono, ubicación o notificaciones siempre se preguntan.
- **Páginas de error en español** que explican qué pasó.
- **Restaura tus pestañas** al volver a abrir.

### 🤖 Claude dentro del navegador
- Panel lateral nativo con el mismo chat que MojarraCut: elige **modelo** y **esfuerzo**, adjunta imágenes o una **captura de la página**.
- Claude puede **leer la pestaña**, ver enlaces, **abrir y cerrar pestañas**, **buscar**, resaltar texto y guardar favoritos.
- Clic derecho sobre un texto → **Preguntar a Claude** o **Traducir**. Clic derecho en la página → **Resumir**.
- Puedes hacerlo tu **navegador predeterminado** (desde el instalador o `moja://ajustes`).
- Seguro por diseño: Claude **no puede ejecutar comandos ni editar archivos**; solo usa las herramientas del navegador,
  buscar/leer en la web y leer la carpeta de datos. Lo que dice una página se trata como información, nunca como órdenes.

## ⌨️ Atajos

| Atajo | Acción | Atajo | Acción |
|---|---|---|---|
| `Ctrl+T` | Pestaña nueva | `Ctrl+W` | Cerrar pestaña |
| `Ctrl+Shift+T` | Reabrir pestaña cerrada | `Ctrl+Tab` / `Ctrl+1…9` | Cambiar de pestaña |
| `Ctrl+L` / `F6` | Ir a la barra de direcciones | `Ctrl+F` / `F3` | Buscar en la página |
| `Ctrl+D` | Favorito | `Ctrl+H` / `Ctrl+J` | Historial / Descargas |
| `Ctrl+Shift+L` | Panel de Claude | `Ctrl + / - / 0` | Zoom |
| `Alt+←` / `Alt+→` | Atrás / Adelante | `F5` / `Ctrl+Shift+R` | Recargar / sin caché |
| `F11` | Pantalla completa | `F12` | Herramientas de desarrollador |
| `Alt+F` | Menú | `Ctrl+P` | Imprimir |

## 📦 Instalación

App nativa de Windows 10/11 (C# + WPF) con el motor de Microsoft Edge (WebView2, ya incluido en Windows).
Descarga `MojarraNav-Setup-x.y.z.exe` y ábrelo: se instala solo para tu usuario, sin permisos de administrador,
y queda registrado para poder elegirlo como **navegador predeterminado**.

Para el panel de Claude, instala la [app de Claude](https://claude.com/download) (incluye Claude Code) e inicia sesión;
MojarraNav la encuentra sola.

### Compilar

```powershell
# Windows (necesita .NET 8 SDK y NSIS):  winget install Microsoft.DotNet.SDK.8 NSIS.NSIS
.\scripts\compilar.ps1        # → dist\MojarraNav-Setup-<versión>.exe
dotnet run --project src        # abrirlo sin instalar
dotnet run --project tests/Pruebas   # pruebas de datos y del servidor MCP
```
También compila desde Linux/WSL con `scripts/compilar.sh`.

## 🗂️ Tus datos

Todo se guarda en `Documentos\MojarraNav\` en archivos JSON legibles:
`favoritos.json`, `historial.json`, `ajustes.json`, `sesion.json` y `chat.json`.
Cookies, contraseñas y caché viven en `%LOCALAPPDATA%\MojarraNav\Perfil` (se borran desde `moja://ajustes`).

## 🛠️ Cómo está hecho

| Archivo | Qué hace |
|---|---|
| `src/MainWindow.*` | Ventana nativa (WPF): pestañas, barra de direcciones, menús, atajos, permisos, descargas |
| `src/MainWindow.Claude.cs` | Panel de Claude nativo y sugerencias de la barra |
| `src/ClaudeChat.cs` | Lanza `claude -p` en streaming (igual que MojarraCut) con permisos limitados |
| `src/Puente.cs` | Servidor MCP (`MojarraNav.exe --mcp`) que da a Claude las herramientas del navegador, vía una tubería local con token |
| `src/Datos.cs` | Favoritos, historial, ajustes y sesión |
| `src/Paginas/` | Páginas internas `moja://` (inicio, historial, favoritos, descargas, configuración, error), incluidas en el .exe |
| `installer/` | Instalador NSIS en español |

Parte de **Mojarrium**, junto a [MojarraCut](https://github.com/MilioGa/MojarraCut).
