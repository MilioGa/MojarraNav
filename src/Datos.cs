using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace MojarraNav;

public class Favorito { public string Url { get; set; } = ""; public string Titulo { get; set; } = ""; public long T { get; set; } }
public class Visita { public string Url { get; set; } = ""; public string Titulo { get; set; } = ""; public long T { get; set; } public int N { get; set; } }
public class Ajustes
{
    public string Buscador { get; set; } = "google";        // google | duckduckgo | bing
    public string Inicio { get; set; } = Marca.Inicio;
    public bool PanelClaude { get; set; } = true;
    public double AnchoClaude { get; set; } = 380;
    public string Modelo { get; set; } = "";
    public string Esfuerzo { get; set; } = "";
    public bool RestaurarPestanas { get; set; } = true;
    public bool Bloqueador { get; set; } = true;
    public List<string> SitiosSinBloqueo { get; set; } = new();
}
public class EntradaChat { public string R { get; set; } = ""; public string T { get; set; } = ""; public List<string>? Imgs { get; set; } }
public class Chat { public string? Session { get; set; } public List<EntradaChat> Log { get; set; } = new(); }

/// <summary>
/// Datos del usuario en Documentos\MojarraNav\ : favoritos, historial, ajustes, sesión y chat.
/// Son JSON legibles para que Claude (o el usuario) puedan consultarlos.
/// </summary>
public class Datos
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
    public static readonly Dictionary<string, (string Nombre, string Url)> Buscadores = new()
    {
        ["google"] = ("Google", "https://www.google.com/search?q=%s"),
        ["duckduckgo"] = ("DuckDuckGo", "https://duckduckgo.com/?q=%s"),
        ["bing"] = ("Bing", "https://www.bing.com/search?q=%s"),
    };

    public string Dir { get; }
    public Ajustes Ajustes { get; private set; }
    public List<Favorito> Favoritos { get; private set; }
    public List<Visita> Historial { get; private set; }
    public List<string> Sesion { get; private set; }
    public Chat Chat { get; private set; }

    readonly Dictionary<string, Func<object>> pendientes = new();
    readonly System.Threading.Timer temporizador;
    readonly object cerrojo = new();

    public Datos(string dir)
    {
        Dir = dir; Directory.CreateDirectory(dir);
        Ajustes = Leer("ajustes.json", new Ajustes());
        Favoritos = Leer("favoritos.json", new List<Favorito>());
        Historial = Leer("historial.json", new List<Visita>());
        Sesion = Leer("sesion.json", new List<string>());
        Chat = Leer("chat.json", new Chat());
        if (!Buscadores.ContainsKey(Ajustes.Buscador)) Ajustes.Buscador = "google";
        temporizador = new System.Threading.Timer(_ => Vaciar(), null, Timeout.Infinite, Timeout.Infinite);
    }

    T Leer<T>(string n, T def)
    {
        try { return JsonSerializer.Deserialize<T>(File.ReadAllText(Path.Combine(Dir, n)), Json) ?? def; } catch { return def; }
    }
    // Escritura diferida y atómica (no deja archivos a medias si la app se cierra)
    void Guardar(string n, Func<object> valor) { lock (cerrojo) { pendientes[n] = valor; } temporizador.Change(300, Timeout.Infinite); }
    public void Vaciar()
    {
        KeyValuePair<string, Func<object>>[] lista; lock (cerrojo) { lista = pendientes.ToArray(); pendientes.Clear(); }
        foreach (var (n, f) in lista)
        {
            try { var tmp = Path.Combine(Dir, n + ".tmp"); string txt; lock (cerrojo) txt = JsonSerializer.Serialize(f(), Json); File.WriteAllText(tmp, txt); File.Move(tmp, Path.Combine(Dir, n), true); } catch { }
        }
    }
    static long Ahora => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    // ---------- Ajustes ----------
    public Ajustes CambiarAjustes(JsonObject c)
    {
        lock (cerrojo)
        {
            if (c["buscador"]?.GetValue<string>() is string b && Buscadores.ContainsKey(b)) Ajustes.Buscador = b;
            if (c["panelClaude"] is JsonValue p && p.TryGetValue(out bool pc)) Ajustes.PanelClaude = pc;
            if (c["restaurarPestanas"] is JsonValue r && r.TryGetValue(out bool rp)) Ajustes.RestaurarPestanas = rp;
            if (c["bloqueador"] is JsonValue bl && bl.TryGetValue(out bool bb)) Ajustes.Bloqueador = bb;
            if (Num(c["anchoClaude"]) is double ac) Ajustes.AnchoClaude = Math.Clamp(ac, 280, 900);
            if (c["modelo"]?.GetValue<string>() is string m) Ajustes.Modelo = m;
            if (c["esfuerzo"]?.GetValue<string>() is string e) Ajustes.Esfuerzo = e;
        }
        Guardar("ajustes.json", () => Ajustes); return Ajustes;
    }
    /// <summary>Número de un nodo JSON, venga de texto JSON o de código (int, double…).</summary>
    public static double? Num(JsonNode? n) => n is JsonValue && double.TryParse(n.ToJsonString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : null;
    public void GuardarAjustes() => Guardar("ajustes.json", () => Ajustes);
    /// <summary>Activa o desactiva el bloqueador en un sitio. Devuelve si queda bloqueando.</summary>
    public bool AlternarSitioSinBloqueo(string host)
    {
        bool bloquea;
        lock (cerrojo)
        {
            var i = Ajustes.SitiosSinBloqueo.FindIndex(x => string.Equals(x, host, StringComparison.OrdinalIgnoreCase));
            if (i >= 0) Ajustes.SitiosSinBloqueo.RemoveAt(i); else Ajustes.SitiosSinBloqueo.Add(host);
            bloquea = i >= 0;
        }
        Guardar("ajustes.json", () => Ajustes); return bloquea;
    }

    // ---------- Favoritos ----------
    public bool EsFavorito(string? url) { lock (cerrojo) return url != null && Favoritos.Any(f => f.Url == url); }
    public bool AlternarFavorito(string url, string? titulo)
    {
        bool agregado;
        lock (cerrojo)
        {
            var i = Favoritos.FindIndex(f => f.Url == url);
            if (i >= 0) Favoritos.RemoveAt(i); else Favoritos.Insert(0, new Favorito { Url = url, Titulo = string.IsNullOrWhiteSpace(titulo) ? url : titulo, T = Ahora });
            agregado = i < 0;
        }
        Guardar("favoritos.json", () => Favoritos); return agregado;
    }
    public void QuitarFavorito(string url) { lock (cerrojo) Favoritos.RemoveAll(f => f.Url == url); Guardar("favoritos.json", () => Favoritos); }
    public void EditarFavorito(string url, string titulo)
    {
        lock (cerrojo) { var f = Favoritos.Find(x => x.Url == url); if (f != null) f.Titulo = titulo.Length > 200 ? titulo[..200] : titulo; }
        Guardar("favoritos.json", () => Favoritos);
    }

    // ---------- Historial ----------
    public void Visitar(string url, string? titulo)
    {
        if (!Regex.IsMatch(url, "^https?:", RegexOptions.IgnoreCase)) return;
        lock (cerrojo)
        {
            var i = Historial.FindIndex(h => h.Url == url);
            var prev = i >= 0 ? Historial[i] : null; if (i >= 0) Historial.RemoveAt(i);
            Historial.Insert(0, new Visita { Url = url, Titulo = !string.IsNullOrWhiteSpace(titulo) ? titulo! : prev?.Titulo ?? url, T = Ahora, N = (prev?.N ?? 0) + 1 });
            if (Historial.Count > 5000) Historial.RemoveRange(5000, Historial.Count - 5000);
        }
        Guardar("historial.json", () => Historial);
    }
    public void Titular(string url, string titulo)
    {
        if (string.IsNullOrWhiteSpace(titulo)) return;
        lock (cerrojo) { var h = Historial.Find(x => x.Url == url); if (h == null) return; h.Titulo = titulo; }
        Guardar("historial.json", () => Historial);
    }
    public void BorrarHistorial(string? url = null) { lock (cerrojo) { if (url == null) Historial.Clear(); else Historial.RemoveAll(h => h.Url == url); } Guardar("historial.json", () => Historial); }
    public List<Visita> BuscarHistorial(string? q, int max = 50)
    {
        var s = (q ?? "").Trim().ToLowerInvariant();
        lock (cerrojo) return (s.Length == 0 ? Historial : Historial.Where(h => h.Url.ToLowerInvariant().Contains(s) || h.Titulo.ToLowerInvariant().Contains(s))).Take(max).ToList();
    }
    public record Sugerencia(string Url, string Titulo, bool Fav, bool Buscar = false);
    /// <summary>Sugerencias para la barra de direcciones: favoritos primero, luego lo más visitado.</summary>
    public List<Sugerencia> Sugerencias(string q)
    {
        var s = q.Trim().ToLowerInvariant(); if (s.Length == 0) return new();
        bool Coincide(string url, string titulo) => Regex.Replace(url.ToLowerInvariant(), "^https?://(www\\.)?", "").Contains(s) || titulo.ToLowerInvariant().Contains(s);
        lock (cerrojo)
        {
            var fav = Favoritos.Where(f => Coincide(f.Url, f.Titulo)).Select(f => new Sugerencia(f.Url, f.Titulo, true));
            var his = Historial.Where(h => Coincide(h.Url, h.Titulo) && !Favoritos.Any(f => f.Url == h.Url))
                .OrderByDescending(h => h.N).ThenByDescending(h => h.T).Select(h => new Sugerencia(h.Url, h.Titulo, false));
            return fav.Concat(his).Take(6).ToList();
        }
    }
    public List<Visita> MasVisitados(int n = 8) { lock (cerrojo) return Historial.Where(h => !Favoritos.Any(f => f.Url == h.Url)).OrderByDescending(h => h.N).ThenByDescending(h => h.T).Take(n).ToList(); }

    // ---------- Sesión y chat ----------
    public void GuardarSesion(List<string> urls) { lock (cerrojo) Sesion = urls; Guardar("sesion.json", () => Sesion); }
    public void GuardarChat(Chat c) { lock (cerrojo) Chat = c; Guardar("chat.json", () => Chat); }

    public string UrlBusqueda(string q) => Buscadores[Ajustes.Buscador].Url.Replace("%s", Uri.EscapeDataString(q));

    /// <summary>Convierte lo que se escribe en la barra en una URL: dirección, dominio o búsqueda.</summary>
    public string? AUrl(string? texto)
    {
        var t = (texto ?? "").Trim(); if (t.Length == 0) return null;
        if (Regex.IsMatch(t, $"^{Marca.Protocolo}://", RegexOptions.IgnoreCase)) { var u = t.ToLowerInvariant(); return u.EndsWith("/") || u.Contains('?') || u.Contains('#') || u.Count(c => c == '/') > 2 ? u : u + "/"; }
        if (Regex.IsMatch(t, "^(https?|file)://", RegexOptions.IgnoreCase)) return t;
        if (Regex.IsMatch(t, @"^([a-zA-Z]:\\|\\\\)")) { try { return new Uri(t.Trim('"')).AbsoluteUri; } catch { } }   // archivo abierto con el navegador
        if (Regex.IsMatch(t, "^(about|data|view-source|edge):", RegexOptions.IgnoreCase)) return t;
        if (Regex.IsMatch(t, @"^localhost(:\d+)?(/.*)?$", RegexOptions.IgnoreCase) || Regex.IsMatch(t, @"^(\d{1,3}\.){3}\d{1,3}(:\d+)?(/.*)?$")) return "http://" + t;
        if (!Regex.IsMatch(t, @"\s") && Regex.IsMatch(t, @"^[\w-]+(\.[\w-]+)*\.[a-z]{2,}(:\d+)?(/.*)?$", RegexOptions.IgnoreCase)) return "https://" + t;
        return UrlBusqueda(t);
    }
}
