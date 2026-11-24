using System.IO;
using System.Reflection;

namespace MojarraNav;

/// <summary>Archivos incluidos dentro del .exe: páginas internas (moja://) y CLAUDE.md.</summary>
public static class Recursos
{
    static readonly Assembly asm = typeof(Recursos).Assembly;
    static readonly Dictionary<string, string> Tipos = new()
    {
        [".html"] = "text/html; charset=utf-8", [".js"] = "text/javascript; charset=utf-8", [".css"] = "text/css; charset=utf-8",
        [".png"] = "image/png", [".svg"] = "image/svg+xml", [".json"] = "application/json; charset=utf-8",
    };
    static readonly string[] Paginas = { "inicio", "historial", "favoritos", "descargas", "ajustes", "error" };

    public static Stream? Abrir(string nombre) => asm.GetManifestResourceStream(nombre);

    /// <summary>moja://inicio/ → Paginas/inicio.html · moja://inicio/pagina.css → Paginas/pagina.css</summary>
    public static (Stream? datos, string tipo) Pagina(Uri uri)
    {
        var ruta = uri.AbsolutePath.Trim('/');
        string? archivo = ruta.Length == 0 ? (Paginas.Contains(uri.Host) ? uri.Host + ".html" : null) : ruta;
        if (archivo == null || archivo.Contains("..") || archivo.Contains('/') || archivo.Contains('\\')) return (null, "");
        var s = Abrir("Paginas/" + archivo);
        return (s, Tipos.GetValueOrDefault(Path.GetExtension(archivo), "application/octet-stream"));
    }

    public static void CopiarClaudeMd(string dir)
    {
        try { using var s = Abrir("CLAUDE.md"); if (s == null) return; using var f = File.Create(Path.Combine(dir, "CLAUDE.md")); s.CopyTo(f); } catch { }
    }
}
