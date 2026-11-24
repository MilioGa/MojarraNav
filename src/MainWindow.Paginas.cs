using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace MojarraNav;

// Páginas internas (moja://), descargas, permisos y herramientas para Claude
public partial class MainWindow
{
    static JsonNode? J<T>(T v) => JsonSerializer.SerializeToNode(v, Datos.Json);

    // ===================== Páginas internas =====================
    void ServirInterna(CoreWebView2WebResourceRequestedEventArgs e)
    {
        try
        {
            var (s, tipo) = Recursos.Pagina(new Uri(e.Request.Uri));
            e.Response = s != null
                ? entorno!.CreateWebResourceResponse(s, 200, "OK", $"Content-Type: {tipo}\nCache-Control: no-cache")
                : entorno!.CreateWebResourceResponse(null, 404, "No encontrado", "");
        }
        catch { }
    }

    /// <summary>Lo que pueden pedir las páginas internas (solo ellas: se comprueba el origen del mensaje).</summary>
    async Task MensajePagina(Pestana t, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (!e.Source.StartsWith(Marca.Protocolo + "://", StringComparison.OrdinalIgnoreCase)) return;
        JsonObject? m; try { m = JsonNode.Parse(e.WebMessageAsJson) as JsonObject; } catch { return; }
        if (m == null) return;
        var id = m["id"]?.DeepClone(); var a = m["args"] as JsonArray ?? new JsonArray();
        string A(int i) => a.Count > i ? a[i]?.ToString() ?? "" : "";
        JsonNode? r = null; string? error = null;
        try
        {
            switch (m["accion"]?.ToString())
            {
                case "marca": r = new JsonObject { ["nombre"] = Marca.Nombre, ["version"] = Marca.Version, ["dir"] = datos.Dir, ["motor"] = entorno?.BrowserVersionString }; break;
                case "inicio":
                    r = new JsonObject { ["favoritos"] = J(datos.Favoritos.Take(12)), ["visitados"] = J(datos.MasVisitados(8)), ["buscador"] = Datos.Buscadores[datos.Ajustes.Buscador].Nombre, ["claude"] = chat?.Encontrado == true };
                    break;
                case "navegar": Navegar(A(0)); break;
                case "historial": r = J(datos.BuscarHistorial(A(0), 400)); break;
                case "borrarHistorial": datos.BorrarHistorial(A(0).Length > 0 ? A(0) : null); break;
                case "favoritos": r = J(datos.Favoritos); break;
                case "quitarFavorito": datos.QuitarFavorito(A(0)); PintarBarra(); break;
                case "editarFavorito": datos.EditarFavorito(A(0), A(1)); break;
                case "descargas": r = new JsonArray(descargas.Select(d => (JsonNode)new JsonObject { ["id"] = d.Id, ["nombre"] = d.Nombre, ["ruta"] = d.Ruta, ["url"] = d.Url, ["recibido"] = d.Recibido, ["total"] = d.Total, ["estado"] = d.Estado }).ToArray()); break;
                case "abrirDescarga": { var d = descargas.Find(x => x.Id == A(0)); if (d?.Estado == "completed") AbrirExterno(d.Ruta); break; }
                case "mostrarDescarga": { var d = descargas.Find(x => x.Id == A(0)); if (d != null) System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{d.Ruta}\""); break; }
                case "cancelarDescarga": descargas.Find(x => x.Id == A(0))?.Operacion?.Cancel(); break;
                case "limpiarDescargas": descargas.RemoveAll(x => x.Estado != "progressing"); break;
                case "carpetaDescargas": AbrirExterno(CarpetaDescargas()); break;
                case "ajustes":
                    var aj = J(datos.Ajustes)!.AsObject();
                    aj["buscadores"] = new JsonObject(Datos.Buscadores.Select(b => KeyValuePair.Create(b.Key, (JsonNode?)b.Value.Nombre)));
                    r = aj; break;
                case "setAjustes":
                    if (a.Count > 0 && a[0] is JsonObject c) { datos.CambiarAjustes(c); if (c.ContainsKey("panelClaude")) MostrarClaude(datos.Ajustes.PanelClaude, false); }
                    r = J(datos.Ajustes); break;
                case "borrarDatos":
                    var que = a.Count > 0 ? a[0]?.ToJsonString() ?? "" : "";
                    if (que.Contains("historial")) datos.BorrarHistorial();
                    if (que.Contains("cookies") && t.Core != null) await t.Core.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.AllSite);
                    if (que.Contains("cache") && t.Core != null) await t.Core.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.DiskCache | CoreWebView2BrowsingDataKinds.CacheStorage);
                    r = true; break;
                case "abrirCarpetaDatos": AbrirExterno(datos.Dir); break;
                case "predeterminado": AbrirExterno($"ms-settings:defaultapps?registeredAppUser={Marca.Id}"); break;
                default: error = "Acción desconocida"; break;
            }
        }
        catch (Exception ex) { error = ex.Message; }
        try { t.Core?.PostWebMessageAsJson(new JsonObject { ["id"] = id, ["resultado"] = r, ["error"] = error }.ToJsonString()); } catch { }
    }

    // ===================== Descargas: directo a la carpeta Descargas =====================
    class Descarga_
    {
        public string Id = Guid.NewGuid().ToString("N"), Nombre = "", Ruta = "", Url = "", Estado = "progressing";
        public long Recibido, Total; public CoreWebView2DownloadOperation? Operacion;
    }
    readonly List<Descarga_> descargas = new();
    static string CarpetaDescargas()
    {
        try { return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) is var h ? Path.Combine(h, "Downloads") : ""; } catch { return Path.GetTempPath(); }
    }
    void Descarga(CoreWebView2DownloadStartingEventArgs e)
    {
        var dir = CarpetaDescargas(); Directory.CreateDirectory(dir);
        var nombre = Path.GetFileName(e.ResultFilePath); if (string.IsNullOrWhiteSpace(nombre)) nombre = "descarga";
        var ruta = Path.Combine(dir, nombre); var n = 1;
        while (File.Exists(ruta)) ruta = Path.Combine(dir, $"{Path.GetFileNameWithoutExtension(nombre)} ({n++}){Path.GetExtension(nombre)}");
        e.ResultFilePath = ruta; e.Handled = true;          // sin el panel de descargas de Edge
        var op = e.DownloadOperation;
        var d = new Descarga_ { Nombre = Path.GetFileName(ruta), Ruta = ruta, Url = op.Uri, Total = (long)(op.TotalBytesToReceive ?? 0), Operacion = op };
        descargas.Insert(0, d); PintarBarra(); Aviso($"Descargando {d.Nombre}…");
        op.BytesReceivedChanged += (_, _) => { d.Recibido = op.BytesReceived; d.Total = (long)(op.TotalBytesToReceive ?? 0); };
        op.StateChanged += (_, _) =>
        {
            d.Recibido = op.BytesReceived;
            d.Estado = op.State switch
            {
                CoreWebView2DownloadState.Completed => "completed",
                CoreWebView2DownloadState.Interrupted => op.InterruptReason == CoreWebView2DownloadInterruptReason.UserCanceled ? "cancelled" : "interrupted",
                _ => "progressing",
            };
            if (d.Estado != "progressing") { d.Operacion = null; PintarBarra(); if (d.Estado == "completed") Aviso($"Descargado: {d.Nombre}"); }
        };
    }

    // ===================== Permisos: lo sensible se pregunta =====================
    record PermisoPendiente(Pestana Pestana, CoreWebView2PermissionRequestedEventArgs Args, CoreWebView2Deferral Diferido, string Clave, string Texto);
    readonly List<PermisoPendiente> permisosPendientes = new();
    readonly Dictionary<string, bool> permisos = new();        // "origen|permiso" → sí/no (esta sesión)

    void PedirPermiso(Pestana t, CoreWebView2PermissionRequestedEventArgs e)
    {
        if (e.PermissionKind is CoreWebView2PermissionKind.Autoplay or CoreWebView2PermissionKind.WindowManagement) { e.State = CoreWebView2PermissionState.Allow; return; }
        var origen = Pestana.Origen(e.Uri);
        if (origen.StartsWith(Marca.Protocolo + ":")) { e.State = CoreWebView2PermissionState.Allow; return; }
        var clave = origen + "|" + e.PermissionKind;
        if (permisos.TryGetValue(clave, out var si)) { e.State = si ? CoreWebView2PermissionState.Allow : CoreWebView2PermissionState.Deny; return; }
        var que = e.PermissionKind switch
        {
            CoreWebView2PermissionKind.Camera => "usar tu cámara",
            CoreWebView2PermissionKind.Microphone => "usar tu micrófono",
            CoreWebView2PermissionKind.Geolocation => "saber tu ubicación",
            CoreWebView2PermissionKind.Notifications => "mostrarte notificaciones",
            CoreWebView2PermissionKind.ClipboardRead => "leer tu portapapeles",
            CoreWebView2PermissionKind.MultipleAutomaticDownloads => "descargar varios archivos",
            CoreWebView2PermissionKind.FileReadWrite => "leer y guardar archivos",
            CoreWebView2PermissionKind.LocalFonts => "ver las fuentes instaladas",
            CoreWebView2PermissionKind.MidiSystemExclusiveMessages => "controlar dispositivos MIDI",
            CoreWebView2PermissionKind.OtherSensors => "usar los sensores del equipo",
            _ => "un permiso especial",
        };
        var host = origen.Replace("https://", "").Replace("http://", "");
        permisosPendientes.Add(new PermisoPendiente(t, e, e.GetDeferral(), clave, $"{host} quiere {que}"));
        PintarPermiso();
    }
    void PintarPermiso()
    {
        var p = permisosPendientes.Find(x => x.Pestana == activa);
        Permiso.Visibility = p != null && !pantallaCompleta ? Visibility.Visible : Visibility.Collapsed;
        if (p != null) PermisoTxt.Text = p.Texto;
    }
    void ResponderPermiso(bool si)
    {
        var p = permisosPendientes.Find(x => x.Pestana == activa); if (p == null) return;
        permisosPendientes.Remove(p); permisos[p.Clave] = si;
        p.Args.State = si ? CoreWebView2PermissionState.Allow : CoreWebView2PermissionState.Deny; p.Diferido.Complete();
        PintarPermiso();
    }

    // ===================== Herramientas del navegador para Claude (servidor MCP) =====================
    string Contexto() => activa != null ? $"Pestaña activa (id {activa.Id}): \"{activa.Titulo}\" — {activa.Url}. Hay {pestanas.Count} pestaña(s) abierta(s)." : "No hay pestañas abiertas.";
    Pestana? PestanaDe(JsonObject a) => Datos.Num(a["id"]) is double d ? pestanas.Find(p => p.Id == (int)d) : activa;
    static JsonObject Ok(JsonNode? r) => new() { ["resultado"] = r };
    static JsonObject Err(string e) => new() { ["error"] = e };

    async Task<string?> Script(Pestana t, string js)
    {
        await t.Lista; if (t.Core == null) return null;
        var r = await t.Core.ExecuteScriptAsync(js);
        try { return JsonSerializer.Deserialize<string>(r); } catch { return null; }
    }

    async Task<JsonNode?> Herramienta(string nombre, JsonObject a)
    {
        string S(string k) => a[k]?.ToString() ?? "";
        switch (nombre)
        {
            case "pestanas":
                return Ok(new JsonArray(pestanas.Select(p => (JsonNode)new JsonObject { ["id"] = p.Id, ["titulo"] = p.Titulo, ["url"] = p.Url, ["activa"] = p == activa }).ToArray()));
            case "leer_pagina":
            {
                var t = PestanaDe(a); if (t == null) return Err("No existe esa pestaña");
                var texto = (await Script(t, "JSON.stringify(document.body ? document.body.innerText : '')") is string j ? JsonSerializer.Deserialize<string>(j) : "") ?? "";
                texto = System.Text.RegularExpressions.Regex.Replace(texto, "\n{3,}", "\n\n");
                const int MAX = 30000; var desde = Math.Max(0, (int)(Datos.Num(a["desde"]) ?? 0));
                var trozo = desde < texto.Length ? texto.Substring(desde, Math.Min(MAX, texto.Length - desde)) : "";
                return Ok(new JsonObject { ["id"] = t.Id, ["titulo"] = t.Titulo, ["url"] = t.Url, ["caracteres"] = texto.Length, ["desde"] = desde, ["texto"] = trozo,
                    ["sigue"] = desde + MAX < texto.Length ? $"La página sigue: usa desde={desde + MAX}" : null });
            }
            case "seleccion":
            {
                if (activa == null) return Err("No hay pestañas");
                var s = await Script(activa, "JSON.stringify(String(getSelection()))") is string j ? JsonSerializer.Deserialize<string>(j) : "";
                return Ok(string.IsNullOrWhiteSpace(s) ? "(no hay texto seleccionado)" : s);
            }
            case "enlaces":
            {
                var t = PestanaDe(a); if (t == null) return Err("No existe esa pestaña");
                var j = await Script(t, "JSON.stringify([...document.links].map(a => ({ texto: (a.innerText || a.title || '').trim().slice(0, 120), url: a.href })).filter(a => /^https?:/.test(a.url)))");
                var l = j != null ? JsonNode.Parse(j) as JsonArray ?? new JsonArray() : new JsonArray();
                var f = S("filtro").ToLowerInvariant();
                return Ok(new JsonArray(l.Where(x => f.Length == 0 || (x?["texto"]?.ToString() ?? "").ToLowerInvariant().Contains(f) || (x?["url"]?.ToString() ?? "").ToLowerInvariant().Contains(f)).Take(300).Select(x => x!.DeepClone()).ToArray()));
            }
            case "abrir":
            {
                var nueva = !(a["nueva_pestana"] is JsonValue nv && nv.TryGetValue(out bool b) && !b);
                var url = datos.AUrl(S("url")); if (url == null) return Err("URL vacía");
                if (nueva || activa == null) { var t = await NuevaPestana(url); return Ok($"Abierto en la pestaña {t?.Id}: {url}"); }
                Cargar(activa, url); return Ok($"Abierto en la pestaña {activa.Id}: {url}");
            }
            case "buscar": { var t = await NuevaPestana(datos.UrlBusqueda(S("consulta"))); return Ok($"Búsqueda abierta en la pestaña {t?.Id}"); }
            case "buscar_en_pagina":
            {
                if (activa?.Core == null) return Err("No hay pestañas");
                var q = S("consulta");
                try
                {
                    var o = entorno!.CreateFindOptions(); o.FindTerm = q; o.ShouldHighlightAllMatches = true;
                    await activa.Core.Find.StartAsync(o); await Task.Delay(400);
                    return Ok($"{activa.Core.Find.MatchCount} coincidencia(s) de \"{q}\"");
                }
                catch
                {
                    var j = await Script(activa, $"(() => {{ const q = {JsonSerializer.Serialize(q.ToLowerInvariant())}; const t = (document.body ? document.body.innerText : '').toLowerCase(); let n = 0, i = 0; if (q) while ((i = t.indexOf(q, i)) >= 0) {{ n++; i += q.length; }} window.find && window.find({JsonSerializer.Serialize(q)}); return String(n); }})()");
                    return Ok($"{j ?? "0"} coincidencia(s) de \"{q}\"");
                }
            }
            case "cambiar_pestana": { var t = PestanaDe(a); if (t == null) return Err("No existe esa pestaña"); Activar(t); return Ok("Listo"); }
            case "cerrar_pestana": { var t = PestanaDe(a); if (t == null) return Err("No existe esa pestaña"); CerrarPestana(t); return Ok("Pestaña cerrada"); }
            case "atras": { var t = PestanaDe(a); if (t?.Core == null) return Err("No existe esa pestaña"); if (t.Core.CanGoBack) t.Core.GoBack(); return Ok("Listo"); }
            case "recargar": { var t = PestanaDe(a); if (t?.Core == null) return Err("No existe esa pestaña"); t.Core.Reload(); return Ok("Listo"); }
            case "favoritos": return Ok(J(datos.Favoritos));
            case "agregar_favorito":
            {
                var u = S("url").Length > 0 ? S("url") : activa?.Url; if (string.IsNullOrEmpty(u)) return Err("Falta la URL");
                if (!datos.EsFavorito(u)) datos.AlternarFavorito(u, S("titulo").Length > 0 ? S("titulo") : activa?.Url == u ? activa.Titulo : u);
                PintarBarra(); return Ok("Guardado en favoritos");
            }
            case "historial":
            {
                var max = (int)(Datos.Num(a["max"]) ?? 30);
                return Ok(new JsonArray(datos.BuscarHistorial(S("consulta"), Math.Clamp(max, 1, 200)).Select(h => (JsonNode)new JsonObject
                    { ["url"] = h.Url, ["titulo"] = h.Titulo, ["fecha"] = DateTimeOffset.FromUnixTimeMilliseconds(h.T).LocalDateTime.ToString("g") }).ToArray()));
            }
        }
        return Err("Herramienta desconocida");
    }
}
