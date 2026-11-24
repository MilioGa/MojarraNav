using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace MojarraNav;

/// <summary>
/// Bloqueador de anuncios y rastreadores con las mismas listas que usan uBlock Origin y Brave
/// (EasyList, EasyPrivacy, filtros de uBlock, Peter Lowe, EasyList en español…).
/// Bloquea peticiones de red y oculta elementos con CSS (filtros cosméticos).
/// </summary>
public class Bloqueador
{
    public static readonly (string Nombre, string Url, string? Respaldo)[] Listas =
    {
        ("easylist", "https://easylist.to/easylist/easylist.txt", "https://raw.githubusercontent.com/easylist/easylist/gh-pages/easylist.txt"),
        ("easyprivacy", "https://easylist.to/easylist/easyprivacy.txt", "https://raw.githubusercontent.com/easylist/easylist/gh-pages/easyprivacy.txt"),
        ("ublock", "https://ublockorigin.github.io/uAssets/filters/filters.txt", "https://raw.githubusercontent.com/uBlockOrigin/uAssets/master/filters/filters.txt"),
        ("ublock-privacidad", "https://ublockorigin.github.io/uAssets/filters/privacy.txt", "https://raw.githubusercontent.com/uBlockOrigin/uAssets/master/filters/privacy.txt"),
        ("ublock-malware", "https://ublockorigin.github.io/uAssets/filters/badware.txt", "https://raw.githubusercontent.com/uBlockOrigin/uAssets/master/filters/badware.txt"),
        ("ublock-unbreak", "https://ublockorigin.github.io/uAssets/filters/unbreak.txt", "https://raw.githubusercontent.com/uBlockOrigin/uAssets/master/filters/unbreak.txt"),
        ("peterlowe", "https://pgl.yoyo.org/adservers/serverlist.php?hostformat=adblockplus&showintro=0&mimetype=plaintext", null),
        ("easylist-es", "https://easylist-downloads.adblockplus.org/easylistspanish.txt", null),
    };

    /// <summary>Dominios de anuncios más comunes: funcionan desde el primer arranque, antes de descargar las listas.</summary>
    static readonly string[] Basicos =
    {
        "doubleclick.net", "googlesyndication.com", "googleadservices.com", "adservice.google.com", "pagead2.googlesyndication.com",
        "amazon-adsystem.com", "adnxs.com", "taboola.com", "outbrain.com", "criteo.com", "criteo.net", "pubmatic.com",
        "rubiconproject.com", "openx.net", "adsrvr.org", "casalemedia.com", "smartadserver.com", "moatads.com",
        "scorecardresearch.com", "quantserve.com", "adform.net", "yieldmo.com", "media.net", "teads.tv", "33across.com",
        "sharethrough.com", "indexww.com", "bidswitch.net", "zedo.com", "popads.net", "popcash.net", "propellerads.com",
        "adcash.com", "exoclick.com", "juicyads.com", "mgid.com", "revcontent.com", "hotjar.com", "mouseflow.com",
    };

    // ---- reglas compiladas ----
    class Regla
    {
        public string Patron = "", Texto = "";      // en minúsculas, con * y ^
        public bool AnclaHost, AnclaInicio, AnclaFin, SoloTerceros, SoloPropios;
        public HashSet<string>? Tipos, NoTipos;   // script, image, stylesheet…
        public string[]? Dominios, NoDominios;   // $domain=
    }
    class Conjunto
    {
        public HashSet<string> Hosts = new(), HostsTerceros = new(), Permitidos = new(), SitiosExcepcion = new(), SinGenerico = new();
        public Dictionary<string, List<Regla>> PorToken = new(), PermisosPorToken = new();
        public List<Regla> SinToken = new();
        public List<string> CssGenerico = new();
        public Dictionary<string, List<string>> CssDominio = new(), CssExcepcion = new();
        public int Reglas;
    }
    volatile Conjunto c = new();

    public bool Activo { get; set; } = true;
    public HashSet<string> SitiosPermitidos { get; } = new(StringComparer.OrdinalIgnoreCase);
    public int Reglas => c.Reglas;
    public DateTime? Actualizado { get; private set; }
    public string Estado { get; private set; } = "Cargando listas…";
    public event Action? Cambio;
    public string ScriptGenerico { get; private set; } = "";
    readonly string dir;

    public Bloqueador(string dir)
    {
        this.dir = dir;
        var b = new Conjunto(); foreach (var h in Basicos) b.Hosts.Add(h); b.Reglas = Basicos.Length; c = b;
    }

    // ===================== Carga y descarga =====================
    /// <summary>Carga las listas guardadas y, si tienen más de 4 días (o no hay), las descarga de nuevo.</summary>
    public async Task IniciarAsync(bool forzar = false)
    {
        try
        {
            Directory.CreateDirectory(dir);
            var archivos = Listas.Select(l => Path.Combine(dir, l.Nombre + ".txt")).ToArray();
            var hay = archivos.Where(File.Exists).ToArray();
            if (hay.Length > 0 && !forzar) await Task.Run(() => Compilar(hay.Select(File.ReadAllText)));
            var viejo = hay.Length < archivos.Length || hay.Select(File.GetLastWriteTimeUtc).Min() < DateTime.UtcNow.AddDays(-4);
            if (forzar || viejo)
            {
                Estado = "Descargando listas…"; Cambio?.Invoke();
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(40) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd($"{Marca.Nombre}/{Marca.Version}");
                var tareas = Listas.Select(async l =>
                {
                    try
                    {
                        string txt;
                        try { txt = await http.GetStringAsync(l.Url); }
                        catch when (l.Respaldo != null) { txt = await http.GetStringAsync(l.Respaldo); }
                        if (txt.Length > 200) { var p = Path.Combine(dir, l.Nombre + ".txt"); await File.WriteAllTextAsync(p + ".tmp", txt); File.Move(p + ".tmp", p, true); }
                    }
                    catch { }
                });
                await Task.WhenAll(tareas);
                hay = archivos.Where(File.Exists).ToArray();
                if (hay.Length > 0) await Task.Run(() => Compilar(hay.Select(File.ReadAllText)));
            }
            Actualizado = hay.Length > 0 ? hay.Select(File.GetLastWriteTime).Max() : null;
            Estado = hay.Length > 0 ? $"{Reglas:N0} reglas de {hay.Length} listas" : "No se pudieron descargar las listas (sin conexión)";
        }
        catch (Exception e) { Estado = "Error al cargar las listas"; System.Diagnostics.Debug.WriteLine(e); }
        Cambio?.Invoke();
    }

    // ===================== Compilar las listas =====================
    static readonly string[] Procedurales = { ":has-text", ":-abp-", ":xpath", ":matches-", ":upward", ":remove", ":style", ":watch-attr", ":min-text-length", ":others", ":matches-path", ":if", ":nth-ancestor", ":contains" };

    public void Compilar(IEnumerable<string> textos)
    {
        var n = new Conjunto(); foreach (var h in Basicos) n.Hosts.Add(h);
        var cssGen = new HashSet<string>();
        foreach (var texto in textos)
            foreach (var bruta in texto.Split('\n'))
            {
                var l = bruta.Trim();
                if (l.Length < 3 || l[0] == '!' || l[0] == '[') continue;
                try { if (Linea(n, l, cssGen)) n.Reglas++; } catch { }
            }
        n.CssGenerico = cssGen.ToList();
        c = n;
        ScriptGenerico = CrearScript(n);
    }

    static bool Linea(Conjunto n, string l, HashSet<string> cssGen)
    {
        // ---- cosméticos ----
        int i = l.IndexOf("##"), j = l.IndexOf("#@#");
        if (j >= 0 && (i < 0 || j < i))
        {
            var sel = l[(j + 3)..]; if (sel.StartsWith('+') || sel.StartsWith('^')) return false;
            foreach (var d in l[..j].Split(',', StringSplitOptions.RemoveEmptyEntries)) Agregar(n.CssExcepcion, d.Trim().ToLowerInvariant(), sel);
            return true;
        }
        if (i >= 0)
        {
            var sel = l[(i + 2)..];
            if (sel.Length == 0 || sel[0] == '+' || sel[0] == '^' || Procedurales.Any(p => sel.Contains(p, StringComparison.Ordinal))) return false;
            var doms = l[..i];
            if (doms.Length == 0) { cssGen.Add(sel); return true; }
            var positivos = doms.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(d => d.Trim().ToLowerInvariant()).Where(d => !d.StartsWith('~')).ToList();
            if (positivos.Count == 0) { cssGen.Add(sel); return true; }
            foreach (var d in positivos) Agregar(n.CssDominio, d.EndsWith(".*") ? d[..^2] : d, sel);
            return true;
        }
        if (l.Contains("#?#") || l.Contains("#$#") || l.Contains("#%#")) return false;

        // ---- red ----
        var excepcion = l.StartsWith("@@"); if (excepcion) l = l[2..];
        if (l.StartsWith('/') && l.EndsWith('/') && l.Length > 2) return false;        // expresiones regulares: se omiten
        string patron = l, opciones = "";
        var dolar = l.LastIndexOf('$');
        if (dolar > 0 && !l[(dolar + 1)..].Contains('/')) { patron = l[..dolar]; opciones = l[(dolar + 1)..].ToLowerInvariant(); }
        var r = new Regla { Texto = l };
        foreach (var o in opciones.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            if (o is "third-party" or "3p") r.SoloTerceros = true;
            else if (o is "~third-party" or "first-party" or "1p") r.SoloPropios = true;
            else if (o.StartsWith("domain=") || o.StartsWith("from="))
            {
                var ds = o[(o.IndexOf('=') + 1)..].Split('|');
                r.Dominios = ds.Where(d => !d.StartsWith('~')).ToArray(); if (r.Dominios.Length == 0) r.Dominios = null;
                r.NoDominios = ds.Where(d => d.StartsWith('~')).Select(d => d[1..]).ToArray();
            }
            else if (excepcion && o is "document" or "doc")
            {
                var h = HostDe(patron); if (h != null && Regex1(patron)) n.SitiosExcepcion.Add(h); return true;
            }
            else if (Tipo(o.TrimStart('~')) is string tp)
            {
                if (o.StartsWith('~')) (r.NoTipos ??= new()).Add(tp); else (r.Tipos ??= new()).Add(tp);
            }
            else if (o is "important" or "match-case" or "all" or "strict3p" or "strict1p") { }
            else if (excepcion && (o is "generichide" or "ghide" or "elemhide" or "ehide"))
            {
                var h = HostDe(patron); if (h != null) n.SinGenerico.Add(h); return true;
            }
            else return false;   // opción desconocida (ipaddress=, redirect=, csp=, popup, document…): mejor no aplicar la regla
        }
        if (r.Tipos != null && r.Tipos.Remove("document") && r.Tipos.Count == 0) return false;   // bloqueo de páginas enteras: no se usa
        patron = patron.ToLowerInvariant();
        if (patron.StartsWith("||") && HostDe(patron) == null && !patron[2..].Contains('/')) return false;   // "||com^" y similares: demasiado amplio
        if (patron.Length == 0 || patron == "*" || patron == "|" || patron == "||") return false;

        // Atajo: "||host^" sin más = dominio completo
        var soloHost = HostDe(patron);
        if (soloHost != null && Regex1(patron) && r.Dominios == null && (r.NoDominios == null || r.NoDominios.Length == 0) && !r.SoloPropios && r.Tipos == null && r.NoTipos == null)
        {
            if (excepcion) n.Permitidos.Add(soloHost);
            else if (r.SoloTerceros) n.HostsTerceros.Add(soloHost);
            else n.Hosts.Add(soloHost);
            return true;
        }
        if (patron.StartsWith("||")) { r.AnclaHost = true; patron = patron[2..]; }
        else if (patron.StartsWith('|')) { r.AnclaInicio = true; patron = patron[1..]; }
        if (patron.EndsWith('|')) { r.AnclaFin = true; patron = patron[..^1]; }
        if (patron.Length < 3 && r.Dominios == null) return false;                          // demasiado genérico
        r.Patron = patron;
        var token = Token(patron);
        var tabla = excepcion ? n.PermisosPorToken : n.PorToken;
        if (token != null) { if (!tabla.TryGetValue(token, out var lista)) tabla[token] = lista = new(); lista.Add(r); }
        else if (!excepcion) n.SinToken.Add(r);
        return true;
    }
    static bool Regex1(string p) { var h = HostDe(p); return h != null && (p == "||" + h + "^" || p == "||" + h || p == "||" + h + "^|" || p == "||" + h + "/"); }
    static string? HostDe(string p)
    {
        if (!p.StartsWith("||")) return null;
        var s = p[2..]; var k = 0;
        while (k < s.Length && (char.IsLetterOrDigit(s[k]) || s[k] is '.' or '-' or '_')) k++;
        if (k == 0 || !s[..k].Contains('.')) return null;
        return s[..k].ToLowerInvariant();
    }
    static void Agregar(Dictionary<string, List<string>> d, string k, string v) { if (!d.TryGetValue(k, out var l)) d[k] = l = new(); l.Add(v); }

    /// <summary>La "palabra" más larga y rara del patrón, para encontrar candidatas rápido.</summary>
    static string? Token(string patron)
    {
        string? mejor = null; int k = 0;
        while (k < patron.Length)
        {
            if (!char.IsLetterOrDigit(patron[k])) { k++; continue; }
            int ini = k; while (k < patron.Length && char.IsLetterOrDigit(patron[k])) k++;
            // un token en el borde junto a * podría estar cortado: no sirve
            if (ini > 0 && patron[ini - 1] == '*' || k < patron.Length && patron[k] == '*') continue;
            var t = patron[ini..k];
            if (t.Length >= 3 && t is not ("http" or "https" or "www" or "com" or "net" or "org") && (mejor == null || t.Length > mejor.Length)) mejor = t;
        }
        return mejor;
    }

    // ===================== Consultas =====================
    /// <summary>¿Bloquear esta petición? pagina = URL de la pestaña (para saber si es de terceros).</summary>
    static string? Tipo(string o) => o switch
    {
        "script" => "script", "image" => "image", "stylesheet" or "css" => "stylesheet", "xmlhttprequest" or "xhr" => "xhr",
        "subdocument" or "frame" => "subdocument", "media" => "media", "font" => "font", "websocket" => "websocket",
        "ping" or "beacon" => "ping", "object" or "object-subrequest" => "object", "other" => "other", "document" or "doc" => "document",
        _ => null,
    };

    public bool Bloquear(string url, string pagina, string tipo = "other")
    {
        if (!Activo) return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || (u.Scheme != "http" && u.Scheme != "https" && u.Scheme != "ws" && u.Scheme != "wss")) return false;
        var hostPag = Uri.TryCreate(pagina, UriKind.Absolute, out var p) ? p.Host.ToLowerInvariant() : "";
        if (SitioPermitido(hostPag)) return false;
        var n = c;
        if (CadenaHost(hostPag).Any(n.SitiosExcepcion.Contains)) return false;
        var host = u.Host.ToLowerInvariant();
        var terceros = hostPag.Length > 0 && Base(host) != Base(hostPag);
        var lurl = url.ToLowerInvariant();
        bool bloq = false;
        foreach (var h in CadenaHost(host))
            if (n.Hosts.Contains(h) || terceros && n.HostsTerceros.Contains(h)) { bloq = true; break; }
        if (!bloq)
            foreach (var t in Tokens(lurl))
            {
                if (n.PorToken.TryGetValue(t, out var lista) && lista.Any(r => Coincide(r, lurl, host, hostPag, terceros, tipo))) { bloq = true; break; }
            }
        if (!bloq) bloq = n.SinToken.Any(r => Coincide(r, lurl, host, hostPag, terceros, tipo));
        if (!bloq) return false;
        // excepciones (@@)
        foreach (var h in CadenaHost(host)) if (n.Permitidos.Contains(h)) return false;
        foreach (var t in Tokens(lurl))
            if (n.PermisosPorToken.TryGetValue(t, out var lista) && lista.Any(r => Coincide(r, lurl, host, hostPag, terceros, tipo))) return false;
        return true;
    }
    /// <summary>Para depurar: la regla que bloquearía la petición.</summary>
    public string? Explicar(string url, string pagina, string tipo = "other")
    {
        var n = c; var lurl = url.ToLowerInvariant(); var u = new Uri(url); var host = u.Host.ToLowerInvariant();
        var hostPag = new Uri(pagina).Host.ToLowerInvariant(); var terceros = Base(host) != Base(hostPag);
        foreach (var h in CadenaHost(host)) { if (n.Hosts.Contains(h)) return "host " + h; if (terceros && n.HostsTerceros.Contains(h)) return "host3p " + h; }
        foreach (var t in Tokens(lurl)) if (n.PorToken.TryGetValue(t, out var l)) { var r = l.FirstOrDefault(r => Coincide(r, lurl, host, hostPag, terceros, tipo)); if (r != null) return r.Texto; }
        return n.SinToken.FirstOrDefault(r => Coincide(r, lurl, host, hostPag, terceros, tipo))?.Texto;
    }
    public bool SitioPermitido(string host) => host.Length > 0 && CadenaHost(host).Any(SitiosPermitidos.Contains);

    static IEnumerable<string> Tokens(string s)
    {
        int k = 0;
        while (k < s.Length)
        {
            if (!char.IsLetterOrDigit(s[k])) { k++; continue; }
            int ini = k; while (k < s.Length && char.IsLetterOrDigit(s[k])) k++;
            if (k - ini >= 3) yield return s[ini..k];
        }
    }
    /// <summary>a.b.ejemplo.com → a.b.ejemplo.com, b.ejemplo.com, ejemplo.com, com</summary>
    public static IEnumerable<string> CadenaHost(string host)
    {
        var h = host;
        while (h.Length > 0) { yield return h; var k = h.IndexOf('.'); if (k < 0) yield break; h = h[(k + 1)..]; }
    }
    /// <summary>Dominio registrable aproximado (ejemplo.com, ejemplo.com.mx, ejemplo.co.uk).</summary>
    public static string Base(string host)
    {
        var p = host.Split('.'); if (p.Length <= 2) return host;
        var sld = p[^2];
        var compuesto = p[^1].Length == 2 && sld is "com" or "co" or "org" or "net" or "gob" or "gov" or "edu" or "ac" or "ne" or "or" or "go";
        return string.Join('.', p.Skip(p.Length - (compuesto ? 3 : 2)));
    }

    static bool Coincide(Regla r, string url, string host, string hostPag, bool terceros, string tipo)
    {
        if (r.Tipos != null && !r.Tipos.Contains(tipo) || r.NoTipos != null && r.NoTipos.Contains(tipo)) return false;
        if (r.SoloTerceros && !terceros || r.SoloPropios && terceros) return false;
        if (r.Dominios != null && !r.Dominios.Any(d => DominioDe(hostPag, d))) return false;
        if (r.NoDominios != null && r.NoDominios.Any(d => DominioDe(hostPag, d))) return false;
        if (r.AnclaHost)
        {
            // el patrón empieza en el host o en un subdominio de él
            var ini = url.IndexOf("://", StringComparison.Ordinal); if (ini < 0) return false; ini += 3;
            var finHost = ini + host.Length;
            for (int k = ini; k < finHost; k++)
                if ((k == ini || url[k - 1] == '.') && Glob(r.Patron, 0, url, k, r.AnclaFin)) return true;
            return false;
        }
        if (r.AnclaInicio) return Glob(r.Patron, 0, url, 0, r.AnclaFin);
        for (int k = 0; k < url.Length; k++) if (Glob(r.Patron, 0, url, k, r.AnclaFin)) return true;
        return false;
    }
    static bool DominioDe(string host, string d) => d.EndsWith(".*") ? CadenaHost(host).Any(h => h.StartsWith(d[..^1])) : host == d || host.EndsWith("." + d);
    static bool Separador(char ch) => !(char.IsLetterOrDigit(ch) || ch is '_' or '-' or '.' or '%');

    /// <summary>Compara el patrón (con * y ^) contra url desde la posición u.</summary>
    static bool Glob(string p, int i, string s, int u, bool fin)
    {
        while (i < p.Length)
        {
            var ch = p[i];
            if (ch == '*')
            {
                while (i < p.Length && p[i] == '*') i++;
                if (i == p.Length) return true;
                for (int k = u; k <= s.Length; k++) if (Glob(p, i, s, k, fin)) return true;
                return false;
            }
            if (ch == '^')
            {
                if (u == s.Length) { i++; continue; }          // ^ también vale como final
                if (!Separador(s[u])) return false;
                i++; u++; continue;
            }
            if (u >= s.Length || s[u] != ch) return false;
            i++; u++;
        }
        return !fin || u == s.Length;
    }

    // ===================== Cosméticos =====================
    /// <summary>CSS para ocultar elementos en un sitio concreto (lo genérico va en ScriptGenerico).</summary>
    public string CssSitio(string host)
    {
        if (!Activo || SitioPermitido(host)) return "";
        var n = c; var sels = new List<string>();
        foreach (var h in CadenaHost(host.ToLowerInvariant()))
        {
            if (n.CssDominio.TryGetValue(h, out var l)) sels.AddRange(l);
            var b = h.LastIndexOf('.'); if (b > 0 && n.CssDominio.TryGetValue(h[..b], out var l2)) sels.AddRange(l2);   // ejemplo.*
        }
        var exc = new HashSet<string>(CadenaHost(host).SelectMany(h => n.CssExcepcion.TryGetValue(h, out var l) ? l : Enumerable.Empty<string>()));
        sels.RemoveAll(exc.Contains);
        if (sels.Count == 0) return "";
        var sb = new StringBuilder();
        foreach (var grupo in sels.Distinct().Chunk(200)) sb.Append(string.Join(",", grupo)).Append("{display:none!important}\n");
        return sb.ToString();
    }

    /// <summary>Script que se ejecuta al crear cada documento (también en iframes): oculta lo genérico y quita anuncios de YouTube.</summary>
    static string CrearScript(Conjunto n)
    {
        var excep = n.CssExcepcion.Where(kv => kv.Key.Length > 0).ToDictionary(kv => kv.Key, kv => kv.Value.Where(n.CssGenerico.Contains).ToList()).Where(kv => kv.Value.Count > 0).ToDictionary(kv => kv.Key, kv => kv.Value);
        var datos = JsonSerializer.Serialize(new { g = n.CssGenerico, x = excep, s = n.SinGenerico.ToArray() });
        return "(()=>{try{if(window.__mojaBloq)return;window.__mojaBloq=1;const D=" + datos + @";
const h=location.hostname.toLowerCase();const cad=[];let k=h;while(k){cad.push(k);const i=k.indexOf('.');if(i<0)break;k=k.slice(i+1);}
if(cad.some(c=>D.s.includes(c)))return;
const fuera=new Set();cad.forEach(c=>(D.x[c]||[]).forEach(s=>fuera.add(s)));
const sels=D.g.filter(s=>!fuera.has(s));const partes=[];
for(let i=0;i<sels.length;i+=300){partes.push(sels.slice(i,i+300).join(',')+'{display:none!important}');}
const css=partes.join('\n');
const poner=()=>{try{const st=document.createElement('style');st.id='moja-bloq';st.textContent=css;(document.head||document.documentElement).appendChild(st);}catch(e){}};
if(document.documentElement)poner();else document.addEventListener('readystatechange',poner,{once:true});
if(/(^|\.)youtube\.com$/.test(h)){const lim=o=>{try{if(o&&typeof o==='object'){for(const c of['adPlacements','playerAds','adSlots','adBreakHeartbeatParams']){if(c in o)delete o[c];}if(o.playerResponse)lim(o.playerResponse);}}catch(e){}return o;};
const jp=JSON.parse;JSON.parse=function(){return lim(jp.apply(this,arguments));};
const rj=Response.prototype.json;Response.prototype.json=function(){return rj.apply(this,arguments).then(lim);};
let ip;try{Object.defineProperty(window,'ytInitialPlayerResponse',{configurable:true,get(){return ip;},set(v){ip=lim(v);}});}catch(e){}
setInterval(()=>{const v=document.querySelector('.ad-showing video');if(v&&isFinite(v.duration)){v.currentTime=v.duration;}const b=document.querySelector('.ytp-ad-skip-button,.ytp-ad-skip-button-modern,.ytp-skip-ad-button');if(b)b.click();},500);}
}catch(e){}})();";
    }

}
