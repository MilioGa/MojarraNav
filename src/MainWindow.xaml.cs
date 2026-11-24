using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace MojarraNav;

public partial class MainWindow : Window
{
    readonly Datos datos;
    readonly string[] urlsIniciales;
    CoreWebView2Environment? entorno;
    readonly List<Pestana> pestanas = new();
    Pestana? activa;
    readonly Stack<string> cerradas = new();
    Puente? puente;
    ClaudeChat? chat;
    readonly DispatcherTimer temporizadorAviso = new() { Interval = TimeSpan.FromSeconds(2.6) };

    // Compartido entre todas las ventanas
    static Datos? datosApp;
    static CoreWebView2Environment? entornoApp;
    public static readonly List<MainWindow> Ventanas = new();
    /// <summary>Ventana de incógnito: no guarda historial, sesión ni cookies, y no tiene panel de Claude.</summary>
    public bool Privada { get; }
    readonly bool secundaria;

    public MainWindow(string[] urls, bool privada = false)
    {
        urlsIniciales = urls; Privada = privada; secundaria = Ventanas.Count > 0;
        if (datosApp == null) { datosApp = new Datos(Program.DirDatos); Recursos.CopiarClaudeMd(datosApp.Dir); }
        datos = datosApp;
        Ventanas.Add(this); Closed += (_, _) => Ventanas.Remove(this);
        InitializeComponent();
        Title = Marca.Nombre;
        RestaurarVentana();
        if (secundaria && WindowStartupLocation == WindowStartupLocation.Manual) { Left += 30; Top += 30; WindowState = WindowState.Normal; }
        if (privada) PrepararIncognito();

        // Ventana
        BMin.Click += (_, _) => WindowState = WindowState.Minimized;
        BMax.Click += (_, _) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        BCerrarVentana.Click += (_, _) => Close();
        StateChanged += (_, _) => AjustarMaximizada();
        Tira.MouseLeftButtonDown += (_, e) => { if (e.ClickCount == 2 && e.OriginalSource == Tira) e.Handled = true; };
        ContTira.MouseLeftButtonDown += (_, e) => { if (e.ClickCount == 2 && (e.OriginalSource == ContTira || e.OriginalSource == PanelPestanas)) { _ = NuevaPestana(); e.Handled = true; } };
        Tira.SizeChanged += (_, _) => AnchoPestanas();
        temporizadorAviso.Tick += (_, _) => { PopAviso.IsOpen = false; temporizadorAviso.Stop(); };

        // Barra
        BNueva.Click += (_, _) => _ = NuevaPestana();
        BAtras.Click += (_, _) => { if (activa?.Core?.CanGoBack == true) activa.Core.GoBack(); };
        BAdelante.Click += (_, _) => { if (activa?.Core?.CanGoForward == true) activa.Core.GoForward(); };
        BRecargar.Click += (_, _) => { if (activa?.Core == null) return; if (activa.Cargando) activa.Core.Stop(); else activa.Core.Reload(); };
        BInicio.Click += (_, _) => Navegar(datos.Ajustes.Inicio);
        BFav.Click += (_, _) => AlternarFavorito();
        BMenu.Click += (_, _) => MenuPrincipal();
        BClaude.Click += (_, _) => AlternarClaude();
        IndZoom.MouseLeftButtonUp += (_, _) => Zoom(0);
        PermSi.Click += (_, _) => ResponderPermiso(true);
        PermNo.Click += (_, _) => ResponderPermiso(false);
        ConfigurarDireccion();
        IniciarListas();
        if (!privada) ConfigurarClaude();

        PreviewKeyDown += Atajos;
        Loaded += async (_, _) => { AjustarMaximizada(); await Iniciar(); };
        Closing += (_, _) => AlCerrar();
    }

    // ===================== Arranque =====================
    async Task Iniciar()
    {
        try
        {
            // Las páginas internas moja:// se registran al crear el motor (la lista solo se puede pasar en el constructor)
            var esquemas = new List<CoreWebView2CustomSchemeRegistration> { new(Marca.Protocolo) { TreatAsSecure = true, HasAuthorityComponent = true } };
            var opciones = new CoreWebView2EnvironmentOptions(null, System.Globalization.CultureInfo.CurrentUICulture.Name, null, false, esquemas);
            var perfil = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Marca.Id, "Perfil");
            entornoApp ??= await CoreWebView2Environment.CreateAsync(null, perfil, opciones);
            entorno = entornoApp;
        }
        catch (WebView2RuntimeNotFoundException)
        {
            if (MessageBox.Show(this, $"{Marca.Nombre} usa el motor de Microsoft Edge (WebView2), que no está instalado en este equipo.\n\n¿Abrir la página de descarga de Microsoft?",
                Marca.Nombre, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                AbrirExterno("https://go.microsoft.com/fwlink/p/?LinkId=2124703");
            Close(); return;
        }
        catch (Exception e) { Program.Registrar(e); MessageBox.Show(this, "No se pudo iniciar el motor del navegador:\n" + e.Message, Marca.Nombre); Close(); return; }

        IniciarBloqueador();

        if (Privada) { _ = NuevaPestana(urlsIniciales.Length > 0 ? datos.AUrl(urlsIniciales[0]) : null); return; }
        // Claude: puente local + servidor MCP (este mismo .exe con --mcp)
        puente = new Puente((n, a) => Dispatcher.InvokeAsync(() => Herramienta(n, a)).Task.Unwrap());
        var exe = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, Marca.Id + ".exe");
        chat = new ClaudeChat(datos.Dir, exe, puente.Tubo, puente.Token, Contexto, ev => Dispatcher.BeginInvoke(() => EventoChat(ev)));

        // Pestañas iniciales: las de la última vez y las que vengan al abrir un enlace
        var previas = datos.Ajustes.RestaurarPestanas && !secundaria ? datos.Sesion.Where(u => !string.IsNullOrWhiteSpace(u)).Take(30).ToList() : new();
        for (int i = 0; i < previas.Count; i++) _ = NuevaPestana(previas[i], activar: i == 0);
        foreach (var u in urlsIniciales) _ = NuevaPestana(datos.AUrl(u) ?? u);
        if (pestanas.Count == 0) _ = NuevaPestana();
    }

    public void AbrirDesdeOtraInstancia(string[] urls)
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        if (urls.Length == 0) _ = NuevaPestana();
        foreach (var u in urls) _ = NuevaPestana(datos.AUrl(u) ?? u);
    }

    void AlCerrar()
    {
        if (Privada) return;
        if (!secundaria) GuardarSesion();
        datos.Ajustes.AnchoClaude = ColClaude.ActualWidth > 0 ? ColClaude.ActualWidth : datos.Ajustes.AnchoClaude;
        datos.GuardarAjustes();
        GuardarVentana();
        datos.Vaciar();
        chat?.Detener(); chat?.Limpiar();
    }

    // ===================== Ventana =====================
    void AjustarMaximizada()
    {
        // Con la barra de título propia, una ventana maximizada se sale unos píxeles de la pantalla
        Raiz.Margin = WindowState == WindowState.Maximized && !pantallaCompleta ? new Thickness(7) : new Thickness(0);
        BMax.Content = WindowState == WindowState.Maximized ? "" : "";
        BMax.ToolTip = WindowState == WindowState.Maximized ? "Restaurar" : "Maximizar";
    }
    string ArchivoVentana => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Marca.Id, "ventana.txt");
    void RestaurarVentana()
    {
        try
        {
            var p = File.ReadAllText(ArchivoVentana).Split(';').Select(double.Parse).ToArray();
            if (p[2] >= MinWidth && p[3] >= MinHeight && p[0] > SystemParameters.VirtualScreenLeft - 50 && p[1] > SystemParameters.VirtualScreenTop - 50)
            { WindowStartupLocation = WindowStartupLocation.Manual; Left = p[0]; Top = p[1]; Width = p[2]; Height = p[3]; }
            if (p.Length > 4 && p[4] == 1) WindowState = WindowState.Maximized;
        }
        catch { WindowState = WindowState.Maximized; }
        // Que nunca quede más grande que la pantalla (al restaurar desde maximizada)
        var area = SystemParameters.WorkArea;
        Width = Math.Min(Width, area.Width); Height = Math.Min(Height, area.Height);
        if (WindowStartupLocation == WindowStartupLocation.Manual)
        { Left = Math.Clamp(Left, area.Left, Math.Max(area.Left, area.Right - Width)); Top = Math.Clamp(Top, area.Top, Math.Max(area.Top, area.Bottom - Height)); }
    }
    void GuardarVentana()
    {
        try
        {
            var r = RestoreBounds; Directory.CreateDirectory(Path.GetDirectoryName(ArchivoVentana)!);
            File.WriteAllText(ArchivoVentana, string.Join(';', new[] { r.Left, r.Top, r.Width, r.Height, WindowState == WindowState.Maximized ? 1 : 0 }.Select(x => x.ToString(System.Globalization.CultureInfo.InvariantCulture))));
        }
        catch { }
    }

    bool pantallaCompleta; Rect antesCompleta; WindowState estadoAntes;
    void PantallaCompleta(bool on)
    {
        if (on == pantallaCompleta) return;
        pantallaCompleta = on;
        if (on)
        {
            estadoAntes = WindowState; antesCompleta = new Rect(Left, Top, Width, Height);
            FilaTira.Height = new GridLength(0); FilaBarra.Height = new GridLength(0); Permiso.Visibility = Visibility.Collapsed;
            ColDivisor.Width = new GridLength(0); ColClaude.Width = new GridLength(0);
            WindowState = WindowState.Normal; Raiz.Margin = new Thickness(0);
            Left = 0; Top = 0; Width = SystemParameters.PrimaryScreenWidth; Height = SystemParameters.PrimaryScreenHeight; Topmost = true;
        }
        else
        {
            Topmost = false; FilaTira.Height = new GridLength(40); FilaBarra.Height = new GridLength(44);
            Left = antesCompleta.Left; Top = antesCompleta.Top; Width = antesCompleta.Width; Height = antesCompleta.Height;
            WindowState = estadoAntes; MostrarClaude(datos.Ajustes.PanelClaude, false); AjustarMaximizada(); PintarBarra();
        }
    }

    void Aviso(string texto)
    {
        TxtAviso.Text = texto; PopAviso.IsOpen = false;
        PopAviso.HorizontalOffset = Math.Max(0, (Raiz.ActualWidth - 260) / 2); PopAviso.VerticalOffset = 92;
        PopAviso.IsOpen = true; temporizadorAviso.Stop(); temporizadorAviso.Start();
    }

    // ===================== Pestañas =====================
    public async Task<Pestana?> NuevaPestana(string? url = null, bool activar = true, Pestana? despuesDe = null, CoreWebView2NewWindowRequestedEventArgs? ventanaNueva = null)
    {
        if (entorno == null) return null;
        url ??= datos.Ajustes.Inicio;
        var wv = new WebView2 { Visibility = Visibility.Hidden, DefaultBackgroundColor = System.Drawing.Color.White };
        var t = new Pestana(wv) { Url = url };
        var ref_ = despuesDe ?? activa;
        var i = ref_ != null ? pestanas.IndexOf(ref_) + 1 : pestanas.Count;
        pestanas.Insert(i > 0 ? i : pestanas.Count, t);
        Vistas.Children.Add(wv);
        CrearElementoPestana(t);
        if (activar || activa == null) Activar(t); else PintarPestanas();
        var lista = new TaskCompletionSource();
        t.Lista = lista.Task;
        try
        {
            if (Privada)
            {
                var o = entorno.CreateCoreWebView2ControllerOptions(); o.IsInPrivateModeEnabled = true; o.ProfileName = "incognito";
                await wv.EnsureCoreWebView2Async(entorno, o);
            }
            else await wv.EnsureCoreWebView2Async(entorno);
            Enganchar(t);
            if (ventanaNueva != null) { ventanaNueva.NewWindow = wv.CoreWebView2; ventanaNueva.Handled = true; }
            else Cargar(t, url);
        }
        catch (Exception e) { Program.Registrar(e); }
        lista.TrySetResult();
        if (t == activa) EnfocarActiva();
        return t;
    }

    void Cargar(Pestana t, string url)
    {
        t.Url = url; t.Error = false;
        try { t.Core?.Navigate(url); }
        catch { var u = datos.UrlBusqueda(url); t.Url = u; t.Core?.Navigate(u); }
        PintarBarra();
    }

    void Activar(Pestana? t)
    {
        if (t == null) return;
        activa = t;
        foreach (var p in pestanas) p.Vista.Visibility = p == t ? Visibility.Visible : Visibility.Hidden;
        PintarPestanas(); PintarBarra(); PintarPermiso();
        EnfocarActiva();
    }
    bool presionandoPestana;   // mientras se arrastra una pestaña no se da el foco a la página (le quitaría el ratón)
    void EnfocarActiva()
    {
        if (activa == null || presionandoPestana) return;
        if (activa.Url.StartsWith(Marca.Inicio, StringComparison.OrdinalIgnoreCase)) Dispatcher.BeginInvoke(() => { Direccion.Focus(); Direccion.SelectAll(); }, DispatcherPriority.Input);
        else Dispatcher.BeginInvoke(() => activa?.Vista.Focus(), DispatcherPriority.Input);
    }

    void CerrarPestana(Pestana? t)
    {
        if (t == null) return;
        var i = pestanas.IndexOf(t); if (i < 0) return;
        if (!t.EsInterna && t.Url.Length > 0) cerradas.Push(t.Url);
        foreach (var p in permisosPendientes.Where(p => p.Pestana == t).ToList()) { p.Args.State = CoreWebView2PermissionState.Deny; p.Diferido.Complete(); permisosPendientes.Remove(p); }
        pestanas.RemoveAt(i);
        PanelPestanas.Children.Remove(t.Elemento);
        Vistas.Children.Remove(t.Vista);
        try { t.Vista.Dispose(); } catch { }
        if (pestanas.Count == 0) { activa = null; _ = NuevaPestana(); return; }
        if (activa == t) Activar(pestanas[Math.Min(i, pestanas.Count - 1)]); else PintarPestanas();
        GuardarSesion();
    }

    void MoverPestana(Pestana t, int a)
    {
        var i = pestanas.IndexOf(t); a = Math.Clamp(a, 0, pestanas.Count - 1); if (i == a) return;
        pestanas.RemoveAt(i); pestanas.Insert(a, t); PintarPestanas(); GuardarSesion();
    }

    public Pestana? Navegar(string? texto, bool nueva = false)
    {
        var url = datos.AUrl(texto); if (url == null) return null;
        if (nueva || activa == null) { _ = NuevaPestana(url); return pestanas.LastOrDefault(); }
        Cargar(activa, url); activa.Vista.Focus(); return activa;
    }

    void GuardarSesion() { if (!Privada && !secundaria) datos.GuardarSesion(pestanas.Select(p => p.Url).Where(u => u.Length > 0 && !u.StartsWith("data:") && !u.StartsWith(Marca.Protocolo + "://error")).ToList()); }

    void Enganchar(Pestana t)
    {
        var c = t.Core!;
        var s = c.Settings;
        s.IsStatusBarEnabled = true; s.AreDevToolsEnabled = true; s.IsZoomControlEnabled = true; s.AreDefaultContextMenusEnabled = true;
        s.IsBuiltInErrorPageEnabled = false; s.IsPasswordAutosaveEnabled = true; s.IsGeneralAutofillEnabled = true; s.AreBrowserAcceleratorKeysEnabled = true;
        c.AddWebResourceRequestedFilter(Marca.Protocolo + "://*", CoreWebView2WebResourceContext.All);
        c.WebResourceRequested += (_, e) =>
        {
            if (e.Request.Uri.StartsWith(Marca.Protocolo + "://", StringComparison.OrdinalIgnoreCase)) ServirInterna(e);
            else FiltrarPeticion(t, e);
        };
        EngancharBloqueador(t);
        c.WebMessageReceived += (_, e) => _ = MensajePagina(t, e);
        c.DocumentTitleChanged += (_, _) => { t.Titulo = string.IsNullOrWhiteSpace(c.DocumentTitle) ? t.Url : c.DocumentTitle; if (!Privada) datos.Titular(t.Url, t.Titulo); PintarPestana(t); if (t == activa) Title = $"{t.Titulo} — {Marca.Nombre}"; };
        c.SourceChanged += (_, _) => { if (!t.Error || !c.Source.StartsWith(Marca.Protocolo + "://error")) t.Url = c.Source; if (t == activa) PintarBarra(); GuardarSesion(); };
        c.HistoryChanged += (_, _) => { if (t == activa) PintarBarra(); };
        c.NavigationStarting += (_, e) =>
        {
            // Enlaces a otros programas (mailto:, tel:, zoommtg:…) se pasan a Windows
            if (!System.Text.RegularExpressions.Regex.IsMatch(e.Uri, $"^(https?|{Marca.Protocolo}|about|data|blob|file|view-source|edge):", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            { e.Cancel = true; PreguntarExterno(e.Uri); return; }
            if (!e.Uri.StartsWith(Marca.Protocolo + "://error")) t.Error = false;
            t.Cargando = true; PintarPestana(t); if (t == activa) PintarBarra();
        };
        c.NavigationCompleted += (_, e) =>
        {
            t.Cargando = false;
            if (!e.IsSuccess && e.WebErrorStatus != CoreWebView2WebErrorStatus.OperationCanceled && e.WebErrorStatus != CoreWebView2WebErrorStatus.ConnectionAborted && !t.Error)
            {
                t.Error = true; var fallida = c.Source;
                t.Url = fallida; t.Titulo = "No se pudo abrir la página";
                c.Navigate($"{Marca.Protocolo}://error/?u={Uri.EscapeDataString(fallida)}&c={e.WebErrorStatus}");
            }
            else if (e.IsSuccess && !t.Error && !Privada) datos.Visitar(c.Source, c.DocumentTitle);
            PintarPestana(t); if (t == activa) PintarBarra();
        };
        c.FaviconChanged += async (_, _) =>
        {
            try
            {
                if (string.IsNullOrEmpty(c.FaviconUri)) { t.Favicon = null; PintarPestana(t); return; }
                using var st = await c.GetFaviconAsync(CoreWebView2FaviconImageFormat.Png);
                if (st == null || st.Length == 0) { t.Favicon = null; PintarPestana(t); return; }
                var bmp = new BitmapImage(); bmp.BeginInit(); bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.StreamSource = st; bmp.DecodePixelWidth = 32; bmp.EndInit(); bmp.Freeze();
                t.Favicon = bmp; PintarPestana(t);
            }
            catch { }
        };
        c.NewWindowRequested += (_, e) =>
        {
            // Ventanas emergentes con tamaño (p. ej. iniciar sesión con Google) se abren como ventana; el resto, en pestaña
            if (e.WindowFeatures.HasSize && e.WindowFeatures.Width > 0 && e.WindowFeatures.Width < 1000) return;
            var d = e.GetDeferral();
            _ = NuevaPestana(e.Uri, true, t, e).ContinueWith(_ => d.Complete(), TaskScheduler.FromCurrentSynchronizationContext());
        };
        c.IsDocumentPlayingAudioChanged += (_, _) => PintarPestana(t);
        c.IsMutedChanged += (_, _) => PintarPestana(t);
        c.ContainsFullScreenElementChanged += (_, _) => PantallaCompleta(c.ContainsFullScreenElement);
        c.PermissionRequested += (_, e) => PedirPermiso(t, e);
        c.DownloadStarting += (_, e) => Descarga(e);
        c.ContextMenuRequested += (_, e) => MenuContexto(t, e);
        c.ProcessFailed += (_, e) =>
        {
            if (e.ProcessFailedKind == CoreWebView2ProcessFailedKind.RenderProcessExited || e.ProcessFailedKind == CoreWebView2ProcessFailedKind.RenderProcessUnresponsive)
            { t.Error = true; try { c.Navigate($"{Marca.Protocolo}://error/?u={Uri.EscapeDataString(t.Url)}&c=crash"); } catch { } }
        };
        t.Vista.ZoomFactorChanged += (_, _) => { if (t == activa) PintarBarra(); };
    }

    void PreguntarExterno(string uri)
    {
        var esquema = uri.Split(':')[0];
        if (MessageBox.Show(this, $"Este sitio quiere abrir otro programa para «{esquema}:».\n\n¿Abrirlo?", Marca.Nombre, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            AbrirExterno(uri);
    }
    public static void AbrirExterno(string uri) { try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); } catch { } }

    // ---------- Tira de pestañas ----------
    void CrearElementoPestana(Pestana t)
    {
        var g = new Grid { Margin = new Thickness(10, 0, 6, 0) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        t.IcoFavicon.FontFamily = (FontFamily)FindResource("Iconos"); t.IcoFavicon.FontSize = 13; t.IcoFavicon.VerticalAlignment = VerticalAlignment.Center; t.IcoFavicon.Width = 16;
        t.ImgFavicon.VerticalAlignment = VerticalAlignment.Center;
        RenderOptions.SetBitmapScalingMode(t.ImgFavicon, BitmapScalingMode.HighQuality);
        var ico = new Grid { Margin = new Thickness(0, 0, 8, 0) }; ico.Children.Add(t.IcoFavicon); ico.Children.Add(t.ImgFavicon);
        t.TxtTitulo.VerticalAlignment = VerticalAlignment.Center; t.TxtTitulo.TextTrimming = TextTrimming.CharacterEllipsis;
        Grid.SetColumn(t.TxtTitulo, 1);
        t.BAudio.Style = (Style)FindResource("BtnIconoChico"); t.BAudio.Width = 22; t.BAudio.Height = 22; t.BAudio.Visibility = Visibility.Collapsed;
        t.BAudio.Click += (_, _) => { if (t.Core != null) t.Core.IsMuted = !t.Core.IsMuted; };
        Grid.SetColumn(t.BAudio, 2);
        t.BCerrar.Style = (Style)FindResource("BtnIconoChico"); t.BCerrar.Width = 22; t.BCerrar.Height = 22; t.BCerrar.FontSize = 10; t.BCerrar.Content = "";
        t.BCerrar.ToolTip = "Cerrar (Ctrl+W)"; AutomationPropertiesNombre(t.BCerrar, "Cerrar pestaña");
        t.BCerrar.Click += (_, _) => CerrarPestana(t);
        Grid.SetColumn(t.BCerrar, 3);
        g.Children.Add(ico); g.Children.Add(t.TxtTitulo); g.Children.Add(t.BAudio); g.Children.Add(t.BCerrar);
        var b = t.Elemento;
        b.Child = g; b.Height = 34; b.CornerRadius = new CornerRadius(9, 9, 0, 0); b.Background = Brushes.Transparent; b.Margin = new Thickness(0, 0, 2, 0);
        System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(b, true);
        b.MouseEnter += (_, _) => { if (t != activa) b.Background = (Brush)FindResource("Panel"); };
        b.MouseLeave += (_, _) => { if (t != activa) b.Background = Brushes.Transparent; };
        ArrastrarPestanas(t);
        b.MouseUp += (_, e) => { if (e.ChangedButton == MouseButton.Middle) CerrarPestana(t); };
        b.MouseRightButtonUp += (_, e) => { MenuPestana(t); e.Handled = true; };
        PanelPestanas.Children.Add(b);
        PintarPestana(t);
    }
    static bool EsBoton(DependencyObject d) { while (d != null) { if (d is Button) return true; d = VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d); } return false; }
    static void AutomationPropertiesNombre(DependencyObject d, string n) => System.Windows.Automation.AutomationProperties.SetName(d, n);

    void PintarPestana(Pestana t)
    {
        t.TxtTitulo.Text = string.IsNullOrWhiteSpace(t.Titulo) ? "Nueva pestaña" : t.Titulo;
        t.Elemento.ToolTip = t.TxtTitulo.Text + (t.Url.Length > 0 && !t.EsInterna ? "\n" + t.Url : "");
        if (t.Cargando) { t.ImgFavicon.Visibility = Visibility.Collapsed; t.IcoFavicon.Visibility = Visibility.Visible; t.IcoFavicon.Text = ""; t.IcoFavicon.Foreground = (Brush)FindResource("Acento2"); }
        else if (t.Favicon != null) { t.ImgFavicon.Source = t.Favicon; t.ImgFavicon.Visibility = Visibility.Visible; t.IcoFavicon.Visibility = Visibility.Collapsed; }
        else { t.ImgFavicon.Visibility = Visibility.Collapsed; t.IcoFavicon.Visibility = Visibility.Visible; t.IcoFavicon.Text = t.EsInterna ? "" : ""; t.IcoFavicon.Foreground = (Brush)FindResource("Tenue"); }
        var audio = t.Core != null && (t.Core.IsDocumentPlayingAudio || t.Core.IsMuted);
        t.BAudio.Visibility = audio ? Visibility.Visible : Visibility.Collapsed;
        if (audio) { t.BAudio.Content = t.Core!.IsMuted ? "" : ""; t.BAudio.ToolTip = t.Core.IsMuted ? "Activar sonido" : "Silenciar"; }
        var act = t == activa;
        t.Elemento.Background = act ? (Brush)FindResource("Panel2") : Brushes.Transparent;
        t.TxtTitulo.Foreground = (Brush)FindResource(act ? "Texto" : "Tenue");
        var estrecha = t.Elemento.Width < 72;
        t.TxtTitulo.Visibility = estrecha ? Visibility.Collapsed : Visibility.Visible;
        t.BCerrar.Visibility = estrecha && !act ? Visibility.Collapsed : Visibility.Visible;
    }
    void PintarPestanas()
    {
        for (int i = 0; i < pestanas.Count; i++)
        {
            var e = pestanas[i].Elemento;
            var actual = PanelPestanas.Children.IndexOf(e);
            if (actual != i) { if (actual >= 0) PanelPestanas.Children.RemoveAt(actual); PanelPestanas.Children.Insert(i, e); }
        }
        AnchoPestanas();
        if (activa != null) Title = $"{activa.Titulo} — {Marca.Nombre}";
    }
    void AnchoPestanas()
    {
        if (pestanas.Count == 0) return;
        var disponible = Tira.ActualWidth - 138 - 8 - 40 - 40;
        var ancho = Math.Clamp(disponible / pestanas.Count - 2, 36, 220);
        foreach (var p in pestanas) { p.Elemento.Width = ancho; PintarPestana(p); }
    }

    // ---------- Barra ----------
    void PintarBarra()
    {
        var a = activa;
        BAtras.IsEnabled = a?.Core?.CanGoBack == true; BAdelante.IsEnabled = a?.Core?.CanGoForward == true;
        BRecargar.Content = a?.Cargando == true ? "" : "";
        BRecargar.ToolTip = a?.Cargando == true ? "Detener (Esc)" : "Volver a cargar (F5)";
        var url = a == null || a.Url.StartsWith(Marca.Inicio, StringComparison.OrdinalIgnoreCase) ? "" : a.Url;
        if (!Direccion.IsKeyboardFocused) Direccion.Text = MostrarUrl(url);
        MarcaAgua.Visibility = Direccion.Text.Length == 0 && !Direccion.IsKeyboardFocused ? Visibility.Visible : Visibility.Collapsed;
        PintarEscudo();
        var fav = url.Length > 0 && datos.EsFavorito(a!.Url);
        BFav.Content = fav ? "" : ""; BFav.Foreground = (Brush)FindResource(fav ? "Estrella" : "Texto");
        BFav.ToolTip = fav ? "Quitar de favoritos (Ctrl+D)" : "Añadir a favoritos (Ctrl+D)"; BFav.IsEnabled = url.Length > 0;
        var seguro = url.StartsWith("https:", StringComparison.OrdinalIgnoreCase) || url.StartsWith(Marca.Protocolo + ":", StringComparison.OrdinalIgnoreCase);
        IcoOmni.Text = url.Length == 0 ? "" : seguro ? "" : "";
        IcoOmni.Foreground = (Brush)FindResource(url.Length > 0 && seguro ? "Ok" : "Tenue");
        IcoOmni.ToolTip = url.Length == 0 ? null : seguro ? "Conexión segura" : "Conexión no segura: no escribas contraseñas ni datos de pago";
        var z = a != null ? (int)Math.Round(a.Vista.ZoomFactor * 100) : 100;
        IndZoom.Visibility = z == 100 ? Visibility.Collapsed : Visibility.Visible; TxtZoom.Text = z + " %";
        var activas = descargas.Count(d => d.Estado == "progressing");
        InsigniaDesc.Visibility = activas > 0 ? Visibility.Visible : Visibility.Collapsed; NumDesc.Text = activas.ToString();
    }
    /// <summary>Las direcciones se muestran sin "https://" y legibles (acentos, espacios).</summary>
    static string MostrarUrl(string u)
    {
        if (u.Length == 0) return "";
        try { var s = Uri.UnescapeDataString(u); s = System.Text.RegularExpressions.Regex.Replace(s, "^https://", ""); return System.Text.RegularExpressions.Regex.Replace(s, "^([^/]+)/$", "$1"); }
        catch { return u; }
    }

    void AlternarFavorito()
    {
        if (activa == null || activa.Url.Length == 0 || activa.Url.StartsWith(Marca.Inicio)) return;
        var agregado = datos.AlternarFavorito(activa.Url, activa.Titulo);
        Aviso(agregado ? "Guardado en favoritos" : "Quitado de favoritos"); PintarBarra();
    }

    void Zoom(int dir, Pestana? t = null)
    {
        t ??= activa; if (t == null) return;
        double[] pasos = { 0.25, 0.33, 0.5, 0.67, 0.75, 0.8, 0.9, 1, 1.1, 1.25, 1.5, 1.75, 2, 2.5, 3, 4, 5 };
        var z = t.Vista.ZoomFactor; var k = Array.FindIndex(pasos, p => p >= z - 0.001); if (k < 0) k = pasos.Length - 1;
        t.Vista.ZoomFactor = dir == 0 ? 1 : pasos[Math.Clamp(k + dir, 0, pasos.Length - 1)];
        PintarBarra();
    }

    // ===================== Atajos de teclado =====================
    void Atajos(object sender, KeyEventArgs e)
    {
        var k = e.Key == Key.System ? e.SystemKey : e.Key;
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control); var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift); var alt = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
        bool H(Action a) { a(); e.Handled = true; return true; }
        if (ctrl && !shift && k == Key.T) { H(() => _ = NuevaPestana()); return; }
        if (ctrl && !shift && (k == Key.W || k == Key.F4)) { H(() => CerrarPestana(activa)); return; }
        if (ctrl && shift && k == Key.T) { H(() => { if (cerradas.Count > 0) _ = NuevaPestana(cerradas.Pop()); }); return; }
        if (ctrl && k == Key.Tab || ctrl && (k == Key.PageDown || k == Key.PageUp))
        { H(() => { var n = pestanas.Count; var j = pestanas.IndexOf(activa!); Activar(pestanas[(j + ((shift && k == Key.Tab) || k == Key.PageUp ? n - 1 : 1)) % n]); }); return; }
        if (ctrl && k >= Key.D1 && k <= Key.D9) { H(() => Activar(k == Key.D9 ? pestanas.Last() : pestanas.ElementAtOrDefault(k - Key.D1))); return; }
        if (ctrl && !shift && k == Key.N) { H(() => NuevaVentana(false)); return; }
        if (ctrl && shift && k == Key.N) { H(() => NuevaVentana(true)); return; }
        if (ctrl && !shift && k == Key.L || k == Key.F6 || alt && k == Key.D) { H(() => { Direccion.Focus(); Direccion.SelectAll(); }); return; }
        if (ctrl && (k == Key.E || k == Key.K)) { H(() => { Direccion.Focus(); Direccion.Text = ""; }); return; }
        if (alt && k == Key.Home) { H(() => Navegar(datos.Ajustes.Inicio)); return; }
        if (alt && k == Key.F) { H(MenuPrincipal); return; }
        if (alt && k == Key.Left) { H(() => { if (activa?.Core?.CanGoBack == true) activa.Core.GoBack(); }); return; }
        if (alt && k == Key.Right) { H(() => { if (activa?.Core?.CanGoForward == true) activa.Core.GoForward(); }); return; }
        if (ctrl && !shift && k == Key.D) { H(AlternarFavorito); return; }
        if (ctrl && !shift && k == Key.H) { H(() => _ = NuevaPestana(Marca.Protocolo + "://historial/")); return; }
        if (ctrl && !shift && k == Key.J) { H(() => _ = NuevaPestana(Marca.Protocolo + "://descargas/")); return; }
        if (ctrl && shift && k == Key.O) { H(() => _ = NuevaPestana(Marca.Protocolo + "://favoritos/")); return; }
        if (ctrl && shift && k == Key.L) { H(() => { if (!Privada) AlternarClaude(); }); return; }
        if (k == Key.F11) { H(() => PantallaCompleta(!pantallaCompleta)); return; }
        if (k == Key.Escape && pantallaCompleta && activa?.Core?.ContainsFullScreenElement != true) { H(() => PantallaCompleta(false)); return; }
        // Con el foco en la interfaz (no en la página) se reenvían algunos atajos del navegador
        if (Keyboard.FocusedElement is not WebView2)
        {
            if (k == Key.F5 || ctrl && k == Key.R) { H(() => activa?.Core?.Reload()); return; }
            if (ctrl && (k == Key.OemPlus || k == Key.Add)) { H(() => Zoom(1)); return; }
            if (ctrl && (k == Key.OemMinus || k == Key.Subtract)) { H(() => Zoom(-1)); return; }
            if (ctrl && (k == Key.D0 || k == Key.NumPad0)) { H(() => Zoom(0)); return; }
            if (ctrl && k == Key.F && Keyboard.FocusedElement != ClTexto && Keyboard.FocusedElement != Direccion) { H(BuscarEnPagina); return; }
            if (ctrl && k == Key.P) { H(Imprimir); return; }
            if (k == Key.F12) { H(() => activa?.Core?.OpenDevToolsWindow()); return; }
        }
    }

    void BuscarEnPagina()
    {
        if (activa?.Core == null) return;
        activa.Vista.Focus();
        try { var o = entorno!.CreateFindOptions(); o.FindTerm = ""; _ = activa.Core.Find.StartAsync(o); }
        catch { }
    }
    void Imprimir() { try { activa?.Core?.ShowPrintUI(CoreWebView2PrintDialogKind.Browser); } catch { _ = activa?.Core?.ExecuteScriptAsync("print()"); } }

    // ===================== Menús =====================
    MenuItem Item(string texto, Action accion, string? atajo = null, string? icono = null, bool activo = true)
    {
        var m = new MenuItem { Header = texto, InputGestureText = atajo ?? "", IsEnabled = activo };
        if (icono != null) m.Icon = new TextBlock { Text = icono, FontFamily = (FontFamily)FindResource("Iconos") };
        m.Click += (_, _) => accion(); return m;
    }
    void MenuPrincipal()
    {
        var a = activa; var z = a != null ? (int)Math.Round(a.Vista.ZoomFactor * 100) : 100;
        var zoom = new MenuItem { Header = $"Zoom: {z} %", Icon = new TextBlock { Text = "", FontFamily = (FontFamily)FindResource("Iconos") } };
        zoom.Items.Add(Item("Acercar", () => Zoom(1), "Ctrl++")); zoom.Items.Add(Item("Alejar", () => Zoom(-1), "Ctrl+-")); zoom.Items.Add(Item("Tamaño normal", () => Zoom(0), "Ctrl+0"));
        var web = a != null && !a.EsInterna && a.Url.StartsWith("http");
        var m = new ContextMenu
        {
            Items =
            {
                Item("Pestaña nueva", () => _ = NuevaPestana(), "Ctrl+T", ""),
                Item("Ventana nueva", () => NuevaVentana(false), "Ctrl+N", ""),
                Item("Ventana de incógnito", () => NuevaVentana(true), "Ctrl+Shift+N", ""),
                Item("Reabrir pestaña cerrada", () => { if (cerradas.Count > 0) _ = NuevaPestana(cerradas.Pop()); }, "Ctrl+Shift+T", "", cerradas.Count > 0),
                new Separator(),
                Item("Historial", () => _ = NuevaPestana(Marca.Protocolo + "://historial/"), "Ctrl+H", ""),
                Item("Descargas", () => _ = NuevaPestana(Marca.Protocolo + "://descargas/"), "Ctrl+J", ""),
                Item("Favoritos", () => _ = NuevaPestana(Marca.Protocolo + "://favoritos/"), "Ctrl+Shift+O", ""),
                new Separator(),
                zoom,
                Item("Buscar en la página…", BuscarEnPagina, "Ctrl+F", "", a != null),
                Item("Imprimir…", Imprimir, "Ctrl+P", "", web),
                Item("Pantalla completa", () => PantallaCompleta(true), "F11", ""),
                new Separator(),
                Item("Panel de Claude", () => AlternarClaude(), "Ctrl+Shift+L", ""),
                Item("Abrir carpeta de datos", () => AbrirExterno(datos.Dir), null, ""),
                Item("Herramientas para desarrolladores", () => a?.Core?.OpenDevToolsWindow(), "F12", "", a != null),
                new Separator(),
                Item("Configuración", () => _ = NuevaPestana(Marca.Protocolo + "://ajustes/"), null, ""),
                Item($"Acerca de {Marca.Nombre}", () => _ = NuevaPestana(Marca.Protocolo + "://ajustes/#acerca"), null, ""),
            },
            PlacementTarget = BMenu, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
        };
        m.IsOpen = true;
    }
    void MenuPestana(Pestana t)
    {
        var i = pestanas.IndexOf(t);
        new ContextMenu
        {
            Items =
            {
                Item("Pestaña nueva a la derecha", () => _ = NuevaPestana(null, true, t), null, ""),
                Item("Volver a cargar", () => t.Core?.Reload(), null, ""),
                Item("Duplicar", () => _ = NuevaPestana(t.Url, true, t), null, ""),
                Item(t.Core?.IsMuted == true ? "Activar sonido del sitio" : "Silenciar sitio", () => { if (t.Core != null) t.Core.IsMuted = !t.Core.IsMuted; }, null, t.Core?.IsMuted == true ? "" : ""),
                new Separator(),
                Item("Cerrar", () => CerrarPestana(t), "Ctrl+W", ""),
                Item("Cerrar las demás pestañas", () => pestanas.Where(x => x != t).ToList().ForEach(CerrarPestana), null, null, pestanas.Count > 1),
                Item("Cerrar las pestañas de la derecha", () => pestanas.Skip(i + 1).ToList().ForEach(CerrarPestana), null, null, i < pestanas.Count - 1),
            },
            IsOpen = true,
        };
    }
    /// <summary>Al menú contextual de Edge se le añaden opciones de Claude.</summary>
    void MenuContexto(Pestana t, CoreWebView2ContextMenuRequestedEventArgs e)
    {
        if (entorno == null) return;
        var obj = e.ContextMenuTarget; var lista = e.MenuItems;
        CoreWebView2ContextMenuItem Nuevo(string texto, Action a) { var it = entorno.CreateContextMenuItem(texto, null, CoreWebView2ContextMenuItemKind.Command); it.CustomItemSelected += (_, _) => Dispatcher.BeginInvoke(a); return it; }
        var sep = entorno.CreateContextMenuItem("", null, CoreWebView2ContextMenuItemKind.Separator);
        if (!Privada && obj.HasSelection && !obj.IsEditable && !string.IsNullOrWhiteSpace(obj.SelectionText))
        {
            var s = obj.SelectionText.Trim();
            lista.Insert(0, sep);
            lista.Insert(0, Nuevo("Traducir con Claude", () => EnviarAClaude($"Traduce al español (o al inglés si ya está en español):\n\n{s}")));
            lista.Insert(0, Nuevo("✦ Preguntar a Claude sobre esto", () => PrepararPregunta(s)));
        }
        else if (!Privada && obj.Kind == CoreWebView2ContextMenuTargetKind.Page && !obj.HasLinkUri && !obj.IsEditable)
        {
            lista.Add(sep);
            lista.Add(Nuevo("✦ Resumir esta página con Claude", () => EnviarAClaude("Resume la página que estoy viendo.")));
        }
        if (obj.HasLinkUri)
        {
            var enlace = obj.LinkUri;
            if (!Privada) lista.Insert(0, Nuevo("Abrir enlace en una ventana de incógnito", () => NuevaVentana(true, enlace)));
            lista.Insert(0, Nuevo("Abrir enlace en una ventana nueva", () => NuevaVentana(Privada, enlace)));
            lista.Insert(0, Nuevo("Abrir enlace en una pestaña nueva", () => _ = NuevaPestana(enlace, false, t)));
        }
    }
}
