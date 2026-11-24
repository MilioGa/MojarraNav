namespace MojarraNav;

/// <summary>Nombre y datos de marca en un solo lugar.</summary>
public static class Marca
{
    public const string Nombre = "MojarraNav";
    public const string Id = "MojarraNav";
    public const string Carpeta = "MojarraNav";   // Documentos\MojarraNav (favoritos, historial, chat…)
    public const string Protocolo = "moja";       // páginas internas: moja://inicio, moja://historial…
    public const string Inicio = Protocolo + "://inicio/";
    public static string Version => typeof(Marca).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
}
