using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace MojarraNav;

/// <summary>
/// Puente entre el servidor MCP (este mismo .exe lanzado por Claude Code con --mcp) y la ventana del navegador.
/// Usa una tubería con nombre aleatorio (no abre ningún puerto de red) y exige un token en cada petición.
/// Protocolo: una línea JSON { token, nombre, args } → una línea JSON { resultado } o { error }.
/// </summary>
public class Puente
{
    public string Tubo { get; } = $"{Marca.Id}-mcp-{Guid.NewGuid():N}";
    public string Token { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
    readonly Func<string, JsonObject, Task<JsonNode?>> manejar;

    public Puente(Func<string, JsonObject, Task<JsonNode?>> manejar) { this.manejar = manejar; _ = Escuchar(); }

    async Task Escuchar()
    {
        while (true)
        {
            var srv = new NamedPipeServerStream(Tubo, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            try { await srv.WaitForConnectionAsync(); } catch { srv.Dispose(); await Task.Delay(500); continue; }
            _ = Atender(srv);
        }
    }

    async Task Atender(NamedPipeServerStream s)
    {
        using (s)
        {
            try
            {
                var lector = new StreamReader(s, new UTF8Encoding(false), false, 4096, true);
                var escritor = new StreamWriter(s, new UTF8Encoding(false), 4096, true) { AutoFlush = true };
                var linea = await lector.ReadLineAsync(); if (linea == null) return;
                JsonNode? r;
                var pet = JsonNode.Parse(linea) as JsonObject;
                var tk = pet?["token"]?.ToString() ?? "";
                if (pet == null || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(tk), Encoding.UTF8.GetBytes(Token)))
                    r = new JsonObject { ["error"] = "Prohibido" };
                else
                {
                    try { r = await manejar(pet["nombre"]?.ToString() ?? "", pet["args"] as JsonObject ?? new JsonObject()); }
                    catch (Exception e) { r = new JsonObject { ["error"] = e.Message }; }
                }
                await escritor.WriteLineAsync((r ?? new JsonObject { ["resultado"] = "Listo" }).ToJsonString());
            }
            catch { }
        }
    }
}

/// <summary>
/// Servidor MCP por stdio (MojarraNav.exe --mcp). Lo lanza Claude Code; reenvía cada herramienta al navegador abierto.
/// </summary>
public static class McpServidor
{
    static JsonObject S(string desc, JsonObject? props = null, params string[] req) => new()
    {
        ["description"] = desc,
        ["inputSchema"] = new JsonObject { ["type"] = "object", ["properties"] = props ?? new JsonObject(), ["required"] = new JsonArray(req.Select(x => (JsonNode)x).ToArray()) },
    };
    static JsonObject Id() => new() { ["type"] = "number", ["description"] = "Id de la pestaña (de \"pestanas\"). Si falta, la pestaña activa." };
    static JsonObject Txt(string? d = null) => d == null ? new() { ["type"] = "string" } : new() { ["type"] = "string", ["description"] = d };

    public static readonly Dictionary<string, JsonObject> Herramientas = new()
    {
        ["pestanas"] = S("Lista las pestañas abiertas: id, título, URL y cuál está activa."),
        ["leer_pagina"] = S("Devuelve el título, la URL y el texto visible de una pestaña (por defecto la activa).", new() { ["id"] = Id(), ["desde"] = new JsonObject { ["type"] = "number", ["description"] = "Carácter desde el que seguir leyendo una página larga" } }),
        ["seleccion"] = S("Devuelve el texto que el usuario tiene seleccionado en la pestaña activa."),
        ["enlaces"] = S("Lista los enlaces de una pestaña (texto y URL).", new() { ["id"] = Id(), ["filtro"] = Txt("Solo enlaces cuyo texto o URL contenga esto") }),
        ["abrir"] = S("Abre una URL (o un texto a buscar). Por defecto en una pestaña nueva.", new() { ["url"] = Txt(), ["nueva_pestana"] = new JsonObject { ["type"] = "boolean" } }, "url"),
        ["buscar"] = S("Busca en la web con el buscador del usuario, en una pestaña nueva.", new() { ["consulta"] = Txt() }, "consulta"),
        ["buscar_en_pagina"] = S("Resalta un texto en la pestaña activa (como Ctrl+F) y dice cuántas veces aparece.", new() { ["consulta"] = Txt() }, "consulta"),
        ["cambiar_pestana"] = S("Activa una pestaña.", new() { ["id"] = Id() }, "id"),
        ["cerrar_pestana"] = S("Cierra una pestaña.", new() { ["id"] = Id() }, "id"),
        ["atras"] = S("Vuelve a la página anterior en una pestaña.", new() { ["id"] = Id() }),
        ["recargar"] = S("Recarga una pestaña.", new() { ["id"] = Id() }),
        ["favoritos"] = S("Lista los favoritos del usuario."),
        ["agregar_favorito"] = S("Guarda una URL en favoritos (por defecto la de la pestaña activa).", new() { ["url"] = Txt(), ["titulo"] = Txt() }),
        ["historial"] = S("Busca en el historial de navegación.", new() { ["consulta"] = Txt(), ["max"] = new JsonObject { ["type"] = "number" } }),
    };

    static async Task<JsonNode> Llamar(string nombre, JsonNode? args)
    {
        var tubo = Environment.GetEnvironmentVariable("MOJA_TUBO") ?? ""; var token = Environment.GetEnvironmentVariable("MOJA_TOKEN") ?? "";
        try
        {
            using var c = new NamedPipeClientStream(".", tubo, PipeDirection.InOut, PipeOptions.Asynchronous);
            await c.ConnectAsync(5000);
            var w = new StreamWriter(c, new UTF8Encoding(false), 4096, true) { AutoFlush = true };
            await w.WriteLineAsync(new JsonObject { ["token"] = token, ["nombre"] = nombre, ["args"] = args?.DeepClone() ?? new JsonObject() }.ToJsonString());
            var r = new StreamReader(c, new UTF8Encoding(false));
            using var cts = new CancellationTokenSource(30000);
            var l = await r.ReadLineAsync(cts.Token);
            return (l != null ? JsonNode.Parse(l) : null) ?? new JsonObject { ["error"] = "Respuesta vacía del navegador" };
        }
        catch (TimeoutException) { return new JsonObject { ["error"] = "El navegador no está abierto" }; }
        catch (OperationCanceledException) { return new JsonObject { ["error"] = "El navegador tardó demasiado en responder" }; }
        catch (Exception e) { return new JsonObject { ["error"] = "No se pudo hablar con el navegador: " + e.Message }; }
    }

    public static int Ejecutar()
    {
        var entrada = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
        var salida = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };
        var cerrojo = new object();
        void Enviar(JsonObject o) { lock (cerrojo) salida.WriteLine(o.ToJsonString()); }
        var tareas = new List<Task>();
        string? linea;
        while ((linea = entrada.ReadLine()) != null)
        {
            JsonObject? m; try { m = JsonNode.Parse(linea) as JsonObject; } catch { continue; }
            if (m == null || m["id"] == null) continue;                    // notificaciones
            var id = m["id"]!.DeepClone(); var metodo = m["method"]?.ToString();
            JsonObject R(JsonNode r) => new() { ["jsonrpc"] = "2.0", ["id"] = id.DeepClone(), ["result"] = r };
            switch (metodo)
            {
                case "initialize":
                    Enviar(R(new JsonObject { ["protocolVersion"] = m["params"]?["protocolVersion"]?.ToString() ?? "2025-06-18", ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() }, ["serverInfo"] = new JsonObject { ["name"] = "navegador", ["version"] = Marca.Version } }));
                    break;
                case "ping": Enviar(R(new JsonObject())); break;
                case "tools/list":
                    Enviar(R(new JsonObject { ["tools"] = new JsonArray(Herramientas.Select(h => { var t = (JsonObject)h.Value.DeepClone(); t["name"] = h.Key; return (JsonNode)t; }).ToArray()) }));
                    break;
                case "tools/call":
                    var n = m["params"]?["name"]?.ToString() ?? ""; var a = m["params"]?["arguments"];
                    if (!Herramientas.ContainsKey(n)) { Enviar(R(new JsonObject { ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = "Herramienta desconocida: " + n }), ["isError"] = true })); break; }
                    tareas.Add(Task.Run(async () =>
                    {
                        var r = await Llamar(n, a);
                        var err = r["error"]?.ToString();
                        var res = r["resultado"];
                        var texto = err ?? (res is JsonValue v && v.TryGetValue(out string? s) ? s : res?.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) ?? "Listo");
                        Enviar(R(new JsonObject { ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = texto }), ["isError"] = err != null }));
                    }));
                    break;
                default:
                    Enviar(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id.DeepClone(), ["error"] = new JsonObject { ["code"] = -32601, ["message"] = "Método no encontrado" } });
                    break;
            }
        }
        Task.WaitAll(tareas.ToArray(), 5000);
        return 0;
    }
}
