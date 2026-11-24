using System.Diagnostics;
using System.Text.Json.Nodes;
using MojarraNav;

if (args.Contains("--mcp")) return McpServidor.Ejecutar();

var res = new List<(string, bool, string)>();
void P(string n, bool ok, string d = "") { res.Add((n, ok, d)); Console.WriteLine($"{(ok ? "✔" : "✘")} {n}{(d != "" ? " — " + d : "")}"); }

var dir = Path.Combine(Path.GetTempPath(), "mojarranav-pruebas-" + Guid.NewGuid().ToString("N")[..8]);
var datos = new Datos(dir);
P("Dominio → https", datos.AUrl("google.com") == "https://google.com");
P("Texto → búsqueda", datos.AUrl("hola mundo")!.StartsWith("https://www.google.com/search?q=hola%20mundo"), datos.AUrl("hola mundo")!);
P("localhost → http", datos.AUrl("localhost:3000") == "http://localhost:3000");
P("IP → http", datos.AUrl("192.168.1.1") == "http://192.168.1.1");
P("Página interna", datos.AUrl("moja://Historial") == "moja://historial/", datos.AUrl("moja://Historial")!);
P("URL completa se respeta", datos.AUrl("https://x.y/z?a=1") == "https://x.y/z?a=1");
datos.Visitar("https://mojarra.mx/", "Mojarra azul"); datos.Visitar("https://otra.com/", "Otra"); datos.Visitar("https://mojarra.mx/", "Mojarra azul");
P("Historial cuenta visitas", datos.Historial[0].N == 2 && datos.Historial.Count == 2);
P("Sugerencias encuentran por título", datos.Sugerencias("azul").FirstOrDefault()?.Url == "https://mojarra.mx/");
datos.AlternarFavorito("https://otra.com/", "Otra");
P("Favoritos primero en sugerencias", datos.Sugerencias("o").FirstOrDefault()?.Fav == true);
datos.CambiarAjustes(new JsonObject { ["buscador"] = "duckduckgo", ["anchoClaude"] = 5000 });
P("Ajustes validados", datos.Ajustes.Buscador == "duckduckgo" && datos.Ajustes.AnchoClaude == 900);
datos.Vaciar();
P("Se guardan los JSON", File.Exists(Path.Combine(dir, "historial.json")) && File.ReadAllText(Path.Combine(dir, "favoritos.json")).Contains("\"titulo\": \"Otra\""));
var datos2 = new Datos(dir);
P("Se vuelven a leer", datos2.Historial.Count == 2 && datos2.Favoritos.Count == 1 && datos2.Ajustes.Buscador == "duckduckgo");

// Puente + servidor MCP de verdad (proceso aparte, como lo lanza Claude Code)
var puente = new Puente(async (n, a) => { await Task.Yield(); return n == "pestanas" ? new JsonObject { ["resultado"] = new JsonArray(new JsonObject { ["id"] = 1, ["titulo"] = "Prueba" }) } : new JsonObject { ["resultado"] = "leído: " + a["id"] }; });
var psi = new ProcessStartInfo(Environment.ProcessPath!, "--mcp") { RedirectStandardInput = true, RedirectStandardOutput = true, UseShellExecute = false };
if (Environment.ProcessPath!.EndsWith("dotnet")) psi.Arguments = typeof(Datos).Assembly.Location.Replace("Datos", "Pruebas") + " --mcp";
psi.Environment["MOJA_TUBO"] = puente.Tubo; psi.Environment["MOJA_TOKEN"] = puente.Token;
var p = Process.Start(psi)!;
void W(string s) => p.StandardInput.WriteLine(s);
W("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"t","version":"1"}}}""");
W("""{"jsonrpc":"2.0","method":"notifications/initialized"}""");
W("""{"jsonrpc":"2.0","id":2,"method":"tools/list"}""");
W("""{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"pestanas","arguments":{}}}""");
W("""{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"leer_pagina","arguments":{"id":7}}}""");
p.StandardInput.Close();
var resp = new Dictionary<int, JsonNode>();
string? l; while ((l = p.StandardOutput.ReadLine()) != null) { var o = JsonNode.Parse(l)!; resp[o["id"]!.GetValue<int>()] = o; }
p.WaitForExit(10000);
P("MCP initialize", resp.GetValueOrDefault(1)?["result"]?["serverInfo"]?["name"]?.ToString() == "navegador");
P("MCP tools/list = 14 herramientas", resp.GetValueOrDefault(2)?["result"]?["tools"]?.AsArray().Count == 14);
P("MCP → puente → navegador (pestanas)", resp.GetValueOrDefault(3)?["result"]?["content"]?[0]?["text"]?.ToString().Contains("Prueba") == true, resp.GetValueOrDefault(3)?["result"]?["content"]?[0]?["text"]?.ToString().Replace("\n", " ") ?? "sin respuesta");
P("MCP pasa argumentos", resp.GetValueOrDefault(4)?["result"]?["content"]?[0]?["text"]?.ToString() == "leído: 7");

// Sin token correcto, el puente no responde
Environment.SetEnvironmentVariable("MOJA_TUBO", puente.Tubo); Environment.SetEnvironmentVariable("MOJA_TOKEN", "malo");
using (var c = new System.IO.Pipes.NamedPipeClientStream(".", puente.Tubo, System.IO.Pipes.PipeDirection.InOut))
{
    c.Connect(3000); var w = new StreamWriter(c) { AutoFlush = true }; w.WriteLine("""{"token":"malo","nombre":"pestanas"}""");
    var r = new StreamReader(c).ReadLine() ?? "";
    P("El puente rechaza un token falso", r.Contains("Prohibido"), r);
}

// ---------- Bloqueador de anuncios ----------
var bl = new Bloqueador(Path.Combine(dir, "filtros"));
P("Bloqueador: dominios básicos sin listas", bl.Bloquear("https://securepubads.g.doubleclick.net/tag/js/gpt.js", "https://elpais.com/"));
bl.Compilar(new[] { """
! comentario
||anuncios.ejemplo^
||rastreo.ejemplo^$third-party
/banner/*/publicidad.
||cdn.ejemplo/ads/*$script,domain=sitio.com
@@||anuncios.ejemplo/permitido.js
##.anuncio-generico
sitio.com##.anuncio-sitio
otro.com#@#.anuncio-generico
sitio.com##div:has-text(Publicidad)
||com^$doc,ipaddress=1.2.3.4
||imgs.ejemplo^$~image
""" });
P("Opciones desconocidas no bloquean todo (||com^$ipaddress)", !bl.Bloquear("https://i.ytimg.com/vi/x/hqdefault.jpg", "https://www.youtube.com/", "image"));
P("$~image respeta las imágenes", !bl.Bloquear("https://imgs.ejemplo/a.jpg", "https://sitio.com/", "image") && bl.Bloquear("https://imgs.ejemplo/a.js", "https://sitio.com/", "script"));
P("Bloquea dominio y subdominios", bl.Bloquear("https://x.anuncios.ejemplo/a.js", "https://sitio.com/"));
P("Excepción @@ gana", !bl.Bloquear("https://anuncios.ejemplo/permitido.js", "https://sitio.com/"));
P("$third-party: bloquea de terceros", bl.Bloquear("https://rastreo.ejemplo/p.gif", "https://sitio.com/"));
P("$third-party: no en su propio sitio", !bl.Bloquear("https://rastreo.ejemplo/p.gif", "https://www.rastreo.ejemplo/"));
P("Patrón con comodín", bl.Bloquear("https://foo.com/banner/300x250/publicidad.png", "https://sitio.com/"));
P("$domain= solo en ese sitio", bl.Bloquear("https://cdn.ejemplo/ads/a.js", "https://sitio.com/", "script") && !bl.Bloquear("https://cdn.ejemplo/ads/a.js", "https://otro.com/", "script"));
P("No bloquea lo normal", !bl.Bloquear("https://sitio.com/app.js", "https://sitio.com/"));
P("CSS por sitio (sin procedurales)", bl.CssSitio("www.sitio.com").Contains(".anuncio-sitio") && !bl.CssSitio("sitio.com").Contains("has-text"));
P("Script genérico con excepciones", bl.ScriptGenerico.Contains(".anuncio-generico") && bl.ScriptGenerico.Contains("otro.com"));
bl.SitiosPermitidos.Add("sitio.com");
P("Desactivado en un sitio", !bl.Bloquear("https://x.anuncios.ejemplo/a.js", "https://www.sitio.com/") && bl.CssSitio("sitio.com") == "");
bl.SitiosPermitidos.Clear(); bl.Activo = false;
P("Desactivado del todo", !bl.Bloquear("https://x.anuncios.ejemplo/a.js", "https://sitio.com/"));
bl.Activo = true;
P("Dominio registrable .com.mx", Bloqueador.Base("a.b.mercadolibre.com.mx") == "mercadolibre.com.mx" && Bloqueador.Base("x.y.google.com") == "google.com");
var listasDir = Environment.GetEnvironmentVariable("MOJA_LISTAS");
if (listasDir != null && Directory.Exists(listasDir))
{
    var sw = Stopwatch.StartNew();
    var real = new Bloqueador(Path.Combine(dir, "f2"));
    real.Compilar(Directory.GetFiles(listasDir, "*.txt").Select(File.ReadAllText));
    var tc = sw.ElapsedMilliseconds; sw.Restart();
    string[] anuncios = {
        "https://pagead2.googlesyndication.com/pagead/js/adsbygoogle.js", "https://www.googletagservices.com/tag/js/gpt.js",
        "https://static.criteo.net/js/ld/publishertag.js", "https://cdn.taboola.com/libtrc/x/loader.js", "https://www.google-analytics.com/analytics.js",
        "https://connect.facebook.net/en_US/fbevents.js", "https://c.amazon-adsystem.com/aax2/apstag.js", "https://sb.scorecardresearch.com/beacon.js",
        "https://www.googletagmanager.com/gtag/js?id=G-XXXX", "https://ads.pubmatic.com/AdServer/js/pwt.js", "https://static.hotjar.com/c/hotjar-1.js",
    };
    string[] normales = {
        "https://www.youtube.com/s/player/abc/base.js", "https://code.jquery.com/jquery-3.7.1.min.js", "https://fonts.googleapis.com/css2?family=Inter",
        "https://cdnjs.cloudflare.com/ajax/libs/react/18.3.1/umd/react.production.min.js", "https://www.google.com/search?q=hola", "https://github.githubassets.com/assets/app.js",
        "https://upload.wikimedia.org/wikipedia/commons/a/a9/Example.jpg", "https://i.ytimg.com/vi/dQw4w9WgXcQ/hqdefault.jpg", "https://pbs.twimg.com/media/abc.jpg", "https://m.media-amazon.com/images/I/71abc.jpg", "https://http2.mlstatic.com/frontend-assets/app.js",
    };
    var fallan = anuncios.Where(u => !real.Bloquear(u, "https://www.ejemplo-noticias.com/")).ToList();
    var falsos = normales.Where(u => real.Bloquear(u, "https://www.ejemplo-noticias.com/")).ToList();
    for (int k = 0; k < 2000; k++) foreach (var u in anuncios.Concat(normales)) real.Bloquear(u, "https://www.ejemplo-noticias.com/");
    var porPeticion = sw.Elapsed.TotalMilliseconds * 1000 / (2000.0 * (anuncios.Length + normales.Length));
    P($"Listas reales: {real.Reglas:N0} reglas en {tc} ms", real.Reglas > 50000);
    P($"Bloquea anuncios/rastreadores conocidos ({anuncios.Length - fallan.Count}/{anuncios.Length})", fallan.Count == 0, string.Join(" ", fallan));
    P("No bloquea recursos normales", falsos.Count == 0, string.Join(" ", falsos));
    P($"Rápido: {porPeticion:0.0} µs por petición", porPeticion < 200);
    P($"CSS genérico: {real.ScriptGenerico.Length / 1024} KB", real.ScriptGenerico.Length > 10000);
}
Directory.Delete(dir, true);
var ok = res.Count(r => r.Item2);
Console.WriteLine($"\nResultado: {ok}/{res.Count}");
return ok == res.Count ? 0 : 1;
