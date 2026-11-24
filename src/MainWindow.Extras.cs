using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Web.WebView2.Core;

namespace MojarraNav;

// Bloqueador de anuncios, listas rápidas (descargas, historial, favoritos) y animación de pestañas
public partial class MainWindow
{
    static Bloqueador? bloqueadorApp;
    Bloqueador? bloqueador;

    // ===================== Ventanas nuevas e incógnito =====================
    void NuevaVentana(bool privada, string? url = null)
    {
        var v = new MainWindow(url != null ? new[] { url } : Array.Empty<string>(), privada);
        v.Show(); v.Activate();
    }

    void PrepararIncognito()
    {
        var oscuro = new SolidColorBrush(Color.FromRgb(0x1A, 0x14, 0x2E));
        Tira.Background = oscuro; Background = oscuro;
        Barra.Background = new SolidColorBrush(Color.FromRgb(0x22, 0x1B, 0x3A));
        BClaude.Visibility = Visibility.Collapsed;
        PanelClaude.Visibility = Visibility.Collapsed; Divisor.Visibility = Visibility.Collapsed;
        ColDivisor.Width = new GridLength(0); ColClaude.Width = new GridLength(0); ColClaude.MinWidth = 0;
        BHistorial.Visibility = Visibility.Collapsed;
        // Distintivo "Incógnito" junto a los botones de la ventana
        var marca = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x40, 0x7C, 0x6C, 0xFF)), CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 3, 10, 3),
            Margin = new Thickness(0, 8, 8, 0), VerticalAlignment = VerticalAlignment.Top, ToolTip = "No se guardan el historial, las cookies ni lo que escribas en formularios al cerrar esta ventana",
        };
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(new TextBlock { Text = "\uED1A", FontFamily = (FontFamily)FindResource("Iconos"), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
        sp.Children.Add(new TextBlock { Text = "Incógnito", FontWeight = FontWeights.SemiBold });
        marca.Child = sp;
        BotonesVentana.Children.Insert(0, marca);
    }

    // ===================== Bloqueador =====================
    void IniciarBloqueador()
    {
        var nuevo = bloqueadorApp == null;
        if (nuevo)
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Marca.Id, "Filtros");
            bloqueadorApp = new Bloqueador(dir) { Activo = datos.Ajustes.Bloqueador };
            foreach (var s in datos.Ajustes.SitiosSinBloqueo) bloqueadorApp.SitiosPermitidos.Add(s);
        }
        bloqueador = bloqueadorApp!;
        bloqueador.Cambio += () => Dispatcher.BeginInvoke(() => { PintarEscudo(); ActualizarScriptsBloqueo(); });
        Closed += (_, _) => { };
        if (nuevo) _ = bloqueador.IniciarAsync();

        BEscudo.Click += (_, _) => { PintarEscudo(); PopEscudo.IsOpen = true; };
        EscSitio.Click += (_, _) =>
        {
            var host = HostActivo(); if (host.Length == 0) return;
            datos.AlternarSitioSinBloqueo(host);
            bloqueador.SitiosPermitidos.Clear(); foreach (var s in datos.Ajustes.SitiosSinBloqueo) bloqueador.SitiosPermitidos.Add(s);
            activa?.Core?.Reload(); PintarEscudo();
        };
        EscTodo.Click += (_, _) => { CambiarBloqueador(EscTodo.IsChecked == true); activa?.Core?.Reload(); };
        EscActualizar.Click += (_, _) => { EscEstado.Text = "Descargando listas…"; _ = bloqueador.IniciarAsync(true); };
    }

    void CambiarBloqueador(bool on)
    {
        datos.CambiarAjustes(new System.Text.Json.Nodes.JsonObject { ["bloqueador"] = on });
        if (bloqueador != null) bloqueador.Activo = on;
        foreach (var p in pestanas) try { if (p.Core != null) p.Core.Profile.PreferredTrackingPreventionLevel = on ? CoreWebView2TrackingPreventionLevel.Strict : CoreWebView2TrackingPreventionLevel.Balanced; } catch { }
        ActualizarScriptsBloqueo(); PintarEscudo();
    }

    string HostActivo() { try { return activa != null && activa.Url.StartsWith("http") ? new Uri(activa.Url).Host.ToLowerInvariant() : ""; } catch { return ""; } }

    /// <summary>Prepara una pestaña: filtra todas las peticiones y oculta anuncios con CSS.</summary>
    void EngancharBloqueador(Pestana t)
    {
        var c = t.Core!;
        try { c.Profile.PreferredTrackingPreventionLevel = datos.Ajustes.Bloqueador ? CoreWebView2TrackingPreventionLevel.Strict : CoreWebView2TrackingPreventionLevel.Balanced; } catch { }
        try { c.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All, CoreWebView2WebResourceRequestSourceKinds.All); }
        catch { c.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All); }
        c.NavigationStarting += (_, e) => { t.Navegando = e.Uri; t.Bloqueados = 0; if (t == activa) PintarEscudo(); };
        c.FrameNavigationStarting += (_, e) =>
        {
            // iframes de anuncios
            if (bloqueador != null && bloqueador.Bloquear(e.Uri, t.Url, "subdocument")) { e.Cancel = true; t.Bloqueados++; if (t == activa) PintarEscudo(); }
        };
        c.ContentLoading += async (_, _) =>
        {
            if (bloqueador == null || !bloqueador.Activo) return;
            string host; try { host = new Uri(c.Source).Host; } catch { return; }
            var css = bloqueador.CssSitio(host); if (css.Length == 0) return;
            try { await c.ExecuteScriptAsync($"(()=>{{const s=document.createElement('style');s.textContent={System.Text.Json.JsonSerializer.Serialize(css)};(document.head||document.documentElement).appendChild(s);}})()"); } catch { }
        };
        _ = PonerScriptBloqueo(t);
    }

    /// <summary>¿Hay que bloquear esta petición? (se llama para todo lo que no es moja://)</summary>
    void FiltrarPeticion(Pestana t, CoreWebView2WebResourceRequestedEventArgs e)
    {
        if (bloqueador == null || !bloqueador.Activo) return;
        var uri = e.Request.Uri;
        // La página principal nunca se bloquea (ni la que se está abriendo)
        if (e.ResourceContext == CoreWebView2WebResourceContext.Document && (uri == t.Navegando || uri == t.Url)) return;
        var pagina = t.Url.StartsWith("http") ? t.Url : t.Navegando;
        var tipo = e.ResourceContext switch
        {
            CoreWebView2WebResourceContext.Image => "image", CoreWebView2WebResourceContext.Script => "script",
            CoreWebView2WebResourceContext.Stylesheet => "stylesheet", CoreWebView2WebResourceContext.XmlHttpRequest or CoreWebView2WebResourceContext.Fetch => "xhr",
            CoreWebView2WebResourceContext.Document => "subdocument", CoreWebView2WebResourceContext.Media => "media", CoreWebView2WebResourceContext.Font => "font",
            CoreWebView2WebResourceContext.Websocket => "websocket", CoreWebView2WebResourceContext.Ping => "ping", _ => "other",
        };
        if (!bloqueador.Bloquear(uri, pagina, tipo)) return;
        e.Response = entorno!.CreateWebResourceResponse(null, 403, "Bloqueado", "");
        t.Bloqueados++;
        if (t == activa) PintarEscudoDiferido();
    }
    bool escudoPendiente;
    void PintarEscudoDiferido()
    {
        if (escudoPendiente) return; escudoPendiente = true;
        Dispatcher.BeginInvoke(() => { escudoPendiente = false; PintarEscudo(); }, System.Windows.Threading.DispatcherPriority.Background);
    }

    void PintarEscudo()
    {
        if (bloqueador == null) return;
        var host = HostActivo(); var a = activa;
        var sitioOn = bloqueador.Activo && !bloqueador.SitioPermitido(host);
        var n = a?.Bloqueados ?? 0;
        BEscudo.Foreground = (Brush)FindResource(sitioOn ? "Acento2" : "Tenue");
        BEscudo.Opacity = host.Length == 0 ? 0.5 : 1;
        BEscudo.ToolTip = !bloqueador.Activo ? "Bloqueador de anuncios desactivado" : !sitioOn ? $"Bloqueador desactivado en {host}" : $"Bloqueador de anuncios: {n} bloqueados en esta página";
        InsigniaEscudo.Visibility = sitioOn && n > 0 ? Visibility.Visible : Visibility.Collapsed;
        NumEscudo.Text = n > 99 ? "99+" : n.ToString();
        EscHost.Text = host.Length > 0 ? host : "Página interna";
        EscNum.Text = sitioOn ? n.ToString("N0") : "—";
        EscNumTxt.Text = !bloqueador.Activo ? "El bloqueador está desactivado" : !sitioOn ? "Desactivado en este sitio" : "anuncios y rastreadores bloqueados en esta página";
        EscSitio.IsChecked = sitioOn; EscSitio.IsEnabled = bloqueador.Activo && host.Length > 0;
        EscTodo.IsChecked = bloqueador.Activo;
        EscEstado.Text = bloqueador.Estado + (bloqueador.Actualizado is DateTime f ? $" · {f:d MMM HH:mm}" : "");
    }

    // El CSS genérico va en un script que corre al crear cada documento (incluidos iframes)
    readonly Dictionary<Pestana, string> idsScript = new();
    async Task PonerScriptBloqueo(Pestana t)
    {
        if (t.Core == null || bloqueador == null) return;
        try
        {
            if (idsScript.TryGetValue(t, out var viejo)) { t.Core.RemoveScriptToExecuteOnDocumentCreated(viejo); idsScript.Remove(t); }
            if (!bloqueador.Activo || bloqueador.ScriptGenerico.Length == 0) return;
            var host = ""; try { host = new Uri(t.Url).Host; } catch { }
            var script = "if(!" + System.Text.Json.JsonSerializer.Serialize(bloqueador.SitiosPermitidos.ToArray()) + ".some(s=>location.hostname===s||location.hostname.endsWith('.'+s)))" + bloqueador.ScriptGenerico;
            idsScript[t] = await t.Core.AddScriptToExecuteOnDocumentCreatedAsync(script);
        }
        catch { }
    }
    void ActualizarScriptsBloqueo() { foreach (var p in pestanas) _ = PonerScriptBloqueo(p); }

    // ===================== Listas rápidas =====================
    void IniciarListas()
    {
        BDescargas.Click += (_, _) => MostrarLista("descargas", BDescargas);
        BHistorial.Click += (_, _) => MostrarLista("historial", BHistorial);
        BFavoritos.Click += (_, _) => MostrarLista("favoritos", BFavoritos);
    }

    void MostrarLista(string que, FrameworkElement boton)
    {
        PopListaItems.Children.Clear();
        PopLista.PlacementTarget = boton;
        PopLista.HorizontalOffset = -(360 - boton.ActualWidth);
        var pagina = $"{Marca.Protocolo}://{que}/";
        PopListaMas.Content = que switch { "descargas" => "Mostrar todas las descargas", "historial" => "Mostrar todo el historial", _ => "Mostrar todos los favoritos" };
        PopListaMas.Click -= ClickMas; masDestino = pagina; PopListaMas.Click += ClickMas;
        const int max = 8;
        switch (que)
        {
            case "descargas":
                PopListaTitulo.Text = "Descargas recientes";
                foreach (var d in descargas.Take(max))
                {
                    var sub = d.Estado switch
                    {
                        "completed" => Tamano(d.Total > 0 ? d.Total : d.Recibido) + " · Completada",
                        "cancelled" => "Cancelada",
                        "interrupted" => "Falló",
                        _ => d.Total > 0 ? $"{Tamano(d.Recibido)} de {Tamano(d.Total)} · {d.Recibido * 100 / Math.Max(1, d.Total)} %" : $"{Tamano(d.Recibido)} · Descargando…",
                    };
                    var dd = d;
                    PopListaItems.Children.Add(Fila(IconoArchivo(d.Nombre), d.Nombre, sub, () => { if (dd.Estado == "completed") AbrirExterno(dd.Ruta); },
                        d.Estado == "completed" ? ("", "Mostrar en la carpeta", () => System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{dd.Ruta}\"")) :
                        d.Estado == "progressing" ? ("", "Cancelar", () => dd.Operacion?.Cancel()) : null));
                }
                if (descargas.Count == 0) PopListaItems.Children.Add(Vacio("Todavía no has descargado nada"));
                break;
            case "historial":
                PopListaTitulo.Text = "Historial reciente";
                foreach (var h in datos.BuscarHistorial(null, max)) { var u = h.Url; PopListaItems.Children.Add(Fila("", h.Titulo, Dominio(u) + " · " + Hace(h.T), () => Navegar(u))); }
                if (datos.Historial.Count == 0) PopListaItems.Children.Add(Vacio("El historial está vacío"));
                break;
            default:
                PopListaTitulo.Text = "Favoritos";
                var favs = datos.Favoritos.Take(max).ToList();
                foreach (var f in favs) { var u = f.Url; PopListaItems.Children.Add(Fila("", f.Titulo, Dominio(u), () => Navegar(u))); }
                if (favs.Count == 0) PopListaItems.Children.Add(Vacio("Guarda páginas con la estrella o Ctrl+D"));
                break;
        }
        PopLista.IsOpen = true;
    }
    string masDestino = "";
    void ClickMas(object s, RoutedEventArgs e) { PopLista.IsOpen = false; _ = NuevaPestana(masDestino); }

    FrameworkElement Fila(string icono, string titulo, string sub, Action abrir, (string Ico, string Tip, Action Accion)? extra = null)
    {
        var g = new Grid { Margin = new Thickness(0) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var ico = new TextBlock { Text = icono, FontFamily = (FontFamily)FindResource("Iconos"), FontSize = 15, Foreground = (Brush)FindResource("Acento2"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 12, 0) };
        var txt = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        txt.Children.Add(new TextBlock { Text = string.IsNullOrWhiteSpace(titulo) ? "(sin título)" : titulo, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = (Brush)FindResource("Texto") });
        txt.Children.Add(new TextBlock { Text = sub, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = (Brush)FindResource("Tenue"), Margin = new Thickness(0, 1, 0, 0) });
        Grid.SetColumn(txt, 1);
        g.Children.Add(ico); g.Children.Add(txt);
        if (extra is var (xi, xt, xa))
        {
            var b = new Button { Style = (Style)FindResource("BtnIconoChico"), Content = xi, ToolTip = xt, Margin = new Thickness(6, 0, 0, 0) };
            b.Click += (_, ev) => { ev.Handled = true; xa(); PopLista.IsOpen = false; };
            Grid.SetColumn(b, 2); g.Children.Add(b);
        }
        var borde = new Border { Child = g, Padding = new Thickness(8, 7, 8, 7), CornerRadius = new CornerRadius(8), Background = Brushes.Transparent, Cursor = Cursors.Hand };
        borde.MouseEnter += (_, _) => borde.Background = (Brush)FindResource("Panel3");
        borde.MouseLeave += (_, _) => borde.Background = Brushes.Transparent;
        borde.MouseLeftButtonUp += (_, ev) => { if (ev.OriginalSource is DependencyObject d && EsBoton(d)) return; PopLista.IsOpen = false; abrir(); };
        return borde;
    }
    FrameworkElement Vacio(string t) => new TextBlock { Text = t, Foreground = (Brush)FindResource("Tenue"), Margin = new Thickness(8, 6, 8, 10) };
    static string Dominio(string u) { try { return new Uri(u).Host.Replace("www.", ""); } catch { return u; } }
    static string Tamano(long b) => b >= 1L << 30 ? $"{b / (double)(1L << 30):0.0} GB" : b >= 1 << 20 ? $"{b / (double)(1 << 20):0.0} MB" : b >= 1024 ? $"{b / 1024} KB" : $"{b} B";
    static string IconoArchivo(string n) => Path.GetExtension(n).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp" or ".svg" => "",
        ".mp4" or ".mkv" or ".webm" or ".mov" or ".avi" => "",
        ".mp3" or ".wav" or ".flac" or ".ogg" or ".m4a" => "",
        ".zip" or ".rar" or ".7z" => "",
        ".exe" or ".msi" => "",
        _ => "",
    };
    static string Hace(long t)
    {
        var d = DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeMilliseconds(t);
        if (d.TotalMinutes < 1) return "ahora";
        if (d.TotalHours < 1) return $"hace {(int)d.TotalMinutes} min";
        if (d.TotalDays < 1) return $"hace {(int)d.TotalHours} h";
        if (d.TotalDays < 2) return "ayer";
        return DateTimeOffset.FromUnixTimeMilliseconds(t).LocalDateTime.ToString("d MMM");
    }

    // ===================== Arrastrar pestañas con animación =====================
    static readonly IEasingFunction Suave = new CubicEase { EasingMode = EasingMode.EaseOut };
    static TranslateTransform Desplazamiento(FrameworkElement e)
    {
        if (e.RenderTransform is TranslateTransform tt && !tt.IsFrozen) return tt;
        var n = new TranslateTransform(); e.RenderTransform = n; return n;
    }
    static void Animar(FrameworkElement e, double x, int ms = 160)
    {
        var tt = Desplazamiento(e);
        tt.BeginAnimation(TranslateTransform.XProperty, ms <= 0 ? null : new DoubleAnimation(x, TimeSpan.FromMilliseconds(ms)) { EasingFunction = Suave });
        if (ms <= 0) tt.X = x;
    }

    void ArrastrarPestanas(Pestana t)
    {
        var b = t.Elemento;
        Point? inicio = null; bool arrastrando = false; int origen = 0, destino = 0; double ancho = 1;
        b.MouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject d && EsBoton(d)) return;
            presionandoPestana = true;
            Activar(t); inicio = e.GetPosition(PanelPestanas); b.CaptureMouse(); e.Handled = true;
        };
        b.MouseMove += (_, e) =>
        {
            if (inicio == null || e.LeftButton != MouseButtonState.Pressed) return;
            var x = e.GetPosition(PanelPestanas).X;
            if (!arrastrando)
            {
                if (Math.Abs(x - inicio.Value.X) < 6) return;
                arrastrando = true; origen = destino = pestanas.IndexOf(t); ancho = b.ActualWidth + b.Margin.Right;
                Panel.SetZIndex(b, 10); b.Opacity = 0.92;
            }
            var izq = origen * ancho; var dx = Math.Clamp(x - inicio.Value.X, -izq, (pestanas.Count - 1 - origen) * ancho);
            Desplazamiento(b).BeginAnimation(TranslateTransform.XProperty, null); Desplazamiento(b).X = dx;
            var nuevo = Math.Clamp((int)Math.Round((izq + dx) / ancho), 0, pestanas.Count - 1);
            if (nuevo == destino) return;
            destino = nuevo;
            for (int k = 0; k < pestanas.Count; k++)
            {
                var p = pestanas[k]; if (p == t) continue;
                var mover = k > origen && k <= destino ? -ancho : k < origen && k >= destino ? ancho : 0;
                Animar(p.Elemento, mover);
            }
        };
        void Soltar()
        {
            var estaba = arrastrando; arrastrando = false; inicio = null;
            if (b.IsMouseCaptured) b.ReleaseMouseCapture();
            if (presionandoPestana) { presionandoPestana = false; EnfocarActiva(); }
            if (!estaba) return;
            var dx = Desplazamiento(b).X;
            Panel.SetZIndex(b, 0); b.Opacity = 1;
            foreach (var p in pestanas) Animar(p.Elemento, 0, 0);
            if (destino != origen) { MoverPestana(t, destino); }
            // la pestaña "cae" en su sitio desde donde se soltó
            var resto = dx - (destino - origen) * ancho;
            Animar(b, resto, 0); Animar(b, 0, 140);
        }
        b.MouseLeftButtonUp += (_, _) => Soltar();
        b.LostMouseCapture += (_, _) => { if (arrastrando || presionandoPestana) Soltar(); };
    }
}
