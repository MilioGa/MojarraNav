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
Directory.Delete(dir, true);
var ok = res.Count(r => r.Item2);
Console.WriteLine($"\nResultado: {ok}/{res.Count}");
return ok == res.Count ? 0 : 1;
