using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace MojarraNav;

/// <summary>Una pestaña: su vista web (motor de Edge) y lo que muestra la tira.</summary>
public class Pestana
{
    static int siguiente = 1;
    public int Id { get; } = siguiente++;
    public WebView2 Vista { get; }
    public CoreWebView2? Core => Vista.CoreWebView2;
    public string Titulo { get; set; } = "Nueva pestaña";
    public string Url { get; set; } = "";
    public ImageSource? Favicon { get; set; }
    public bool Cargando { get; set; }
    public string Navegando { get; set; } = "";   // URL que se está abriendo (para no bloquearla)
    public int Bloqueados { get; set; }             // anuncios/rastreadores bloqueados en la página actual
    public bool Error { get; set; }
    public Task Lista { get; set; } = Task.CompletedTask;

    // Elementos de la tira
    public Border Elemento { get; } = new();
    public Image ImgFavicon { get; } = new() { Width = 16, Height = 16 };
    public TextBlock IcoFavicon { get; } = new();
    public TextBlock TxtTitulo { get; } = new();
    public Button BAudio { get; } = new();
    public Button BCerrar { get; } = new();

    public Pestana(WebView2 vista) { Vista = vista; }

    public bool EsInterna => Url.StartsWith(Marca.Protocolo + "://", StringComparison.OrdinalIgnoreCase);
    public static string Origen(string url) { try { var u = new Uri(url); return u.GetLeftPart(UriPartial.Authority); } catch { return url; } }
}
