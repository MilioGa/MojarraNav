using System.IO;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;

namespace MojarraNav;

// Panel de Claude (nativo) y barra de direcciones con sugerencias
public partial class MainWindow
{
    static readonly (string v, string n)[] Modelos = { ("", "Modelo predeterminado"), ("fable", "Fable (el más capaz)"), ("opus", "Opus"), ("sonnet", "Sonnet (equilibrado)"), ("haiku", "Haiku (rápido)") };
    static readonly (string v, string n)[] Esfuerzos = { ("", "Esfuerzo auto"), ("low", "Esfuerzo bajo"), ("medium", "Esfuerzo medio"), ("high", "Esfuerzo alto"), ("xhigh", "Esfuerzo muy alto"), ("max", "Esfuerzo máximo") };
    static readonly string[] Ideas = { "Resume esta página", "Explícame esto como si tuviera 12 años", "Busca 3 reseñas de esto y compáralas", "Traduce esta página al español", "Abre en pestañas los 5 enlaces más importantes" };

    readonly List<string> imagenes = new();
    bool ocupado;
    TextBlock? burbujaBot; string textoBot = ""; EntradaChat? entradaBot;
    FrameworkElement? escribiendo;
    DispatcherTimer? guardarChat;

    // ===================== Barra de direcciones =====================
    List<Datos.Sugerencia> sugs = new(); int sel = -1;
    void ConfigurarDireccion()
    {
        Direccion.GotKeyboardFocus += (_, _) => { Dispatcher.BeginInvoke(Direccion.SelectAll, DispatcherPriority.Input); Omni.BorderBrush = (Brush)FindResource("Acento"); MarcaAgua.Visibility = Visibility.Collapsed; };
        Direccion.PreviewMouseLeftButtonDown += (_, e) => { if (!Direccion.IsKeyboardFocusWithin) { Direccion.Focus(); e.Handled = true; } };
        Direccion.LostKeyboardFocus += (_, _) => { Omni.BorderBrush = (Brush)FindResource("Linea"); PopSug.IsOpen = false; PintarBarra(); };
        Direccion.TextChanged += (_, _) => { if (Direccion.IsKeyboardFocused) { MarcaAgua.Visibility = Visibility.Collapsed; Sugerir(); } };
        Direccion.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                var v = sel >= 0 && sel < sugs.Count ? sugs[sel].Url : Direccion.Text;
                if (string.IsNullOrWhiteSpace(v)) return;
                PopSug.IsOpen = false; e.Handled = true;
                Navegar(v, Keyboard.Modifiers.HasFlag(ModifierKeys.Alt));
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                if (PopSug.IsOpen) PopSug.IsOpen = false; else { PintarBarraForzada(); activa?.Vista.Focus(); }
            }
            else if ((e.Key == Key.Down || e.Key == Key.Up) && PopSug.IsOpen && sugs.Count > 0)
            {
                e.Handled = true; sel += e.Key == Key.Down ? 1 : -1;
                if (sel >= sugs.Count) sel = -1; if (sel < -1) sel = sugs.Count - 1;
                MarcarSugerencia();
            }
        };
    }
    void PintarBarraForzada() { var u = activa == null || activa.Url.StartsWith(Marca.Inicio) ? "" : activa.Url; Direccion.Text = MostrarUrl(u); }
    void Sugerir()
    {
        var q = Direccion.Text.Trim();
        if (q.Length == 0) { PopSug.IsOpen = false; sugs.Clear(); return; }
        if (sel >= 0 && sel < sugs.Count && Direccion.Text == sugs[sel].Url) return;     // texto puesto con las flechas
        sugs = datos.Sugerencias(q); sugs.Add(new Datos.Sugerencia(q, $"Buscar \"{q}\"", false, true)); sel = -1;
        ListaSug.Children.Clear();
        for (int i = 0; i < sugs.Count; i++)
        {
            var s = sugs[i]; var k = i;
            var fila = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 7, 10, 7), Background = Brushes.Transparent, Cursor = Cursors.Hand };
            var dp = new DockPanel();
            var ico = new TextBlock { Text = s.Buscar ? "" : s.Fav ? "" : "", FontFamily = (FontFamily)FindResource("Iconos"), FontSize = 13, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)FindResource(s.Fav ? "Estrella" : "Tenue") };
            DockPanel.SetDock(ico, Dock.Left); dp.Children.Add(ico);
            var tit = new TextBlock { Text = s.Titulo, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 420, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(tit, Dock.Left); dp.Children.Add(tit);
            if (!s.Buscar) dp.Children.Add(new TextBlock { Text = "  " + MostrarUrl(s.Url), Foreground = (Brush)FindResource("Acento2"), FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
            fila.Child = dp;
            fila.MouseEnter += (_, _) => { sel = k; MarcarSugerencia(false); };
            fila.MouseLeftButtonDown += (_, e) => { e.Handled = true; PopSug.IsOpen = false; Navegar(s.Url); };
            ListaSug.Children.Add(fila);
        }
        PopSug.IsOpen = true;
    }
    void MarcarSugerencia(bool ponerTexto = true)
    {
        for (int i = 0; i < ListaSug.Children.Count; i++) ((Border)ListaSug.Children[i]).Background = i == sel ? (Brush)FindResource("Panel3") : Brushes.Transparent;
        if (ponerTexto && sel >= 0 && !sugs[sel].Buscar) { Direccion.Text = sugs[sel].Url; Direccion.CaretIndex = Direccion.Text.Length; }
    }

    // ===================== Panel de Claude =====================
    void ConfigurarClaude()
    {
        foreach (var (v, n) in Modelos) ClModelo.Items.Add(new ComboBoxItem { Content = n, Tag = v });
        foreach (var (v, n) in Esfuerzos) ClEsfuerzo.Items.Add(new ComboBoxItem { Content = n, Tag = v });
        ClModelo.SelectedIndex = Math.Max(0, Array.FindIndex(Modelos, m => m.v == datos.Ajustes.Modelo));
        ClEsfuerzo.SelectedIndex = Math.Max(0, Array.FindIndex(Esfuerzos, m => m.v == datos.Ajustes.Esfuerzo));
        ClModelo.SelectionChanged += (_, _) => datos.CambiarAjustes(new JsonObject { ["modelo"] = (string)((ComboBoxItem)ClModelo.SelectedItem).Tag });
        ClEsfuerzo.SelectionChanged += (_, _) => datos.CambiarAjustes(new JsonObject { ["esfuerzo"] = (string)((ComboBoxItem)ClEsfuerzo.SelectedItem).Tag });
        ColClaude.Width = new GridLength(datos.Ajustes.AnchoClaude);
        MostrarClaude(datos.Ajustes.PanelClaude, false);
        Divisor.DragCompleted += (_, _) => datos.CambiarAjustes(new JsonObject { ["anchoClaude"] = ColClaude.ActualWidth });

        ClCerrar.Click += (_, _) => MostrarClaude(false);
        ClNuevo.Click += (_, _) =>
        {
            if (ocupado) { Aviso("Espera a que Claude termine o detenlo"); return; }
            datos.GuardarChat(new Chat()); PintarChat(); Aviso("Conversación nueva");
        };
        ClEnviar.Click += (_, _) => { if (ocupado) chat?.Detener(); else Enviar(); };
        ClTexto.TextChanged += (_, _) => ClMarcaAgua.Visibility = ClTexto.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        ClTexto.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) { e.Handled = true; Enviar(); }
            else if (e.Key == Key.V && Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && Clipboard.ContainsImage()) { e.Handled = true; var img = Clipboard.GetImage(); if (img != null) Adjuntar(img); }
        };
        ClAdjuntar.Click += (_, _) =>
        {
            var d = new Microsoft.Win32.OpenFileDialog { Filter = "Imágenes|*.png;*.jpg;*.jpeg;*.gif;*.webp", Multiselect = true };
            if (d.ShowDialog(this) == true) foreach (var f in d.FileNames) AdjuntarArchivo(f);
        };
        ClCaptura.Click += async (_, _) =>
        {
            if (activa?.Core == null) return;
            try
            {
                using var ms = new MemoryStream();
                await activa.Core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, ms);
                ms.Position = 0; var bmp = BitmapFrame.Create(ms, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                Adjuntar(bmp); Aviso("Captura de la página adjuntada");
            }
            catch { Aviso("No se pudo capturar la página"); }
        };
        ClEntrada.DragOver += (_, e) => { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
        ClEntrada.Drop += (_, e) => { if (e.Data.GetData(DataFormats.FileDrop) is string[] fs) foreach (var f in fs) AdjuntarArchivo(f); };
        PintarChat();
    }

    void AlternarClaude() => MostrarClaude(ColClaude.ActualWidth < 1 || PanelClaude.Visibility != Visibility.Visible);
    void MostrarClaude(bool on, bool guardar = true)
    {
        ColVistas.Width = new GridLength(1, GridUnitType.Star);
        PanelClaude.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        Divisor.Visibility = PanelClaude.Visibility;
        ColDivisor.Width = new GridLength(on ? 5 : 0);
        ColClaude.Width = on ? new GridLength(Math.Clamp(datos.Ajustes.AnchoClaude, 280, 900)) : new GridLength(0);
        ColClaude.MinWidth = on ? 280 : 0;
        BClaude.BorderBrush = on ? (Brush)FindResource("Claude") : new SolidColorBrush(Color.FromArgb(0x73, 0xD9, 0x77, 0x57));
        if (guardar) datos.CambiarAjustes(new JsonObject { ["panelClaude"] = on });
        if (on) Dispatcher.BeginInvoke(() => ClTexto.Focus(), DispatcherPriority.Input);
    }

    void PrepararPregunta(string texto)
    {
        MostrarClaude(true);
        ClTexto.Text = $"Sobre este texto de la página:\n\"{texto}\"\n\n"; ClTexto.CaretIndex = ClTexto.Text.Length; ClTexto.Focus();
    }
    void EnviarAClaude(string texto) { MostrarClaude(true); Enviar(texto); }

    // ---------- Imágenes adjuntas (se reducen a 1568 px, lo que Claude aprovecha) ----------
    void AdjuntarArchivo(string f)
    {
        if (!Regex.IsMatch(f, @"\.(png|jpe?g|gif|webp)$", RegexOptions.IgnoreCase)) { Aviso("Solo imágenes PNG, JPG, GIF o WebP"); return; }
        try { using var s = File.OpenRead(f); Adjuntar(BitmapFrame.Create(s, BitmapCreateOptions.None, BitmapCacheOption.OnLoad)); }
        catch { Aviso("No se pudo leer la imagen"); }
    }
    void Adjuntar(BitmapSource img)
    {
        if (imagenes.Count >= 8) { Aviso("Máximo 8 imágenes por mensaje"); return; }
        var k = Math.Min(1.0, 1568.0 / Math.Max(img.PixelWidth, img.PixelHeight));
        BitmapSource b = k < 1 ? new TransformedBitmap(img, new ScaleTransform(k, k)) : img;
        var enc = new JpegBitmapEncoder { QualityLevel = 88 }; enc.Frames.Add(BitmapFrame.Create(b));
        using var ms = new MemoryStream(); enc.Save(ms);
        imagenes.Add("data:image/jpeg;base64," + Convert.ToBase64String(ms.ToArray()));
        PintarMiniaturas();
    }
    static BitmapImage? DeDataUrl(string d)
    {
        try
        {
            var i = d.IndexOf(','); var bytes = Convert.FromBase64String(d[(i + 1)..]);
            var b = new BitmapImage(); b.BeginInit(); b.CacheOption = BitmapCacheOption.OnLoad; b.StreamSource = new MemoryStream(bytes); b.DecodePixelHeight = 280; b.EndInit(); b.Freeze(); return b;
        }
        catch { return null; }
    }
    void PintarMiniaturas()
    {
        ClMiniaturas.Children.Clear();
        for (int i = 0; i < imagenes.Count; i++)
        {
            var k = i;
            var g = new Grid { Width = 52, Height = 52, Margin = new Thickness(0, 0, 6, 0) };
            g.Children.Add(new Border { CornerRadius = new CornerRadius(6), ClipToBounds = true, BorderBrush = (Brush)FindResource("Linea"), BorderThickness = new Thickness(1),
                Background = new ImageBrush(DeDataUrl(imagenes[i])) { Stretch = Stretch.UniformToFill } });
            var x = new Button { Content = "", Style = (Style)FindResource("BtnIconoChico"), Width = 18, Height = 18, FontSize = 8, Background = new SolidColorBrush(Color.FromArgb(0xBB, 0, 0, 0)),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 1, 0), ToolTip = "Quitar" };
            x.Click += (_, _) => { imagenes.RemoveAt(k); PintarMiniaturas(); };
            g.Children.Add(x); ClMiniaturas.Children.Add(g);
        }
        ClMiniaturas.Visibility = imagenes.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---------- Conversación ----------
    void PintarChat()
    {
        ClLog.Children.Clear(); burbujaBot = null; escribiendo = null;
        if (datos.Chat.Log.Count == 0)
        {
            var p = new StackPanel { MaxWidth = 290, Margin = new Thickness(0, 60, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
            p.Children.Add(new TextBlock { Text = "✦", FontSize = 28, Foreground = (Brush)FindResource("Claude"), HorizontalAlignment = HorizontalAlignment.Center });
            p.Children.Add(new TextBlock { Text = "Claude navega contigo.", FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("Tenue"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 2) });
            p.Children.Add(new TextBlock { Text = "Puede leer la página que estás viendo, buscar, abrir pestañas y guardar favoritos.", TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Foreground = (Brush)FindResource("Tenue") });
            if (chat != null && !chat.Encontrado)
                p.Children.Add(new TextBlock { Text = "Para usarlo, instala la app de Claude (claude.com/download) e inicia sesión.", TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Foreground = (Brush)FindResource("Claude"), Margin = new Thickness(0, 10, 0, 0) });
            foreach (var idea in Ideas)
            {
                var b = new Button { Style = (Style)FindResource("Chip"), Margin = new Thickness(0, 6, 0, 0), Content = new TextBlock { Text = idea, TextWrapping = TextWrapping.Wrap, FontSize = 12 } };
                b.Click += (_, _) => Enviar(idea); p.Children.Add(b);
            }
            ClLog.Children.Add(p);
        }
        else foreach (var e in datos.Chat.Log) ClLog.Children.Add(Entrada(e));
        if (ocupado) MostrarEscribiendo();
        ClScroll.ScrollToEnd();
    }

    FrameworkElement Entrada(EntradaChat e)
    {
        switch (e.R)
        {
            case "user":
            {
                var sp = new StackPanel();
                foreach (var im in e.Imgs ?? new()) if (DeDataUrl(im) is BitmapImage b) sp.Children.Add(new Image { Source = b, MaxHeight = 140, Margin = new Thickness(0, 0, 0, 6), HorizontalAlignment = HorizontalAlignment.Left });
                if (e.T.Length > 0) sp.Children.Add(new TextBlock { Text = e.T, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.White });
                return Burbuja(sp, (Brush)FindResource("Acento"), HorizontalAlignment.Right, new CornerRadius(12, 12, 4, 12));
            }
            case "bot":
            {
                var tb = new TextBlock { TextWrapping = TextWrapping.Wrap }; MarkdownLigero(tb, e.T);
                var b = Burbuja(tb, (Brush)FindResource("Panel2"), HorizontalAlignment.Left, new CornerRadius(12, 12, 12, 4)); b.Tag = tb; return b;
            }
            case "tool":
            {
                var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
                sp.Children.Add(new TextBlock { Text = "✦ ", Foreground = (Brush)FindResource("Claude"), FontSize = 12 });
                sp.Children.Add(new TextBlock { Text = e.T, Foreground = (Brush)FindResource("Tenue"), FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis });
                return sp;
            }
            case "err":
            {
                var tb = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xB3, 0xB3)) }; MarkdownLigero(tb, e.T);
                var b = Burbuja(tb, new SolidColorBrush(Color.FromArgb(0x1F, 0xFF, 0x5A, 0x5A)), HorizontalAlignment.Stretch, new CornerRadius(12));
                b.BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0x5A, 0x5A)); b.BorderThickness = new Thickness(1); return b;
            }
        }
        return new TextBlock { Text = e.T, Foreground = (Brush)FindResource("Tenue"), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center };
    }
    static Border Burbuja(UIElement c, Brush fondo, HorizontalAlignment al, CornerRadius r) =>
        new() { Child = c, Background = fondo, CornerRadius = r, Padding = new Thickness(11, 8, 11, 8), Margin = new Thickness(al == HorizontalAlignment.Right ? 30 : 0, 0, al == HorizontalAlignment.Left ? 30 : 0, 10), HorizontalAlignment = al };

    /// <summary>**negrita**, `código`, [texto](url) y direcciones sueltas como enlaces que se abren en una pestaña.</summary>
    void MarkdownLigero(TextBlock tb, string t)
    {
        tb.Inlines.Clear();
        var re = new Regex(@"\*\*([^*\n]+)\*\*|`([^`\n]+)`|\[([^\]\n]+)\]\((https?://[^\s)]+)\)|(https?://[^\s<)\]]+)");
        int i = 0;
        foreach (Match m in re.Matches(t))
        {
            if (m.Index > i) tb.Inlines.Add(new Run(t[i..m.Index]));
            if (m.Groups[1].Success) tb.Inlines.Add(new Bold(new Run(m.Groups[1].Value)));
            else if (m.Groups[2].Success) tb.Inlines.Add(new Run(m.Groups[2].Value) { FontFamily = new FontFamily("Cascadia Mono, Consolas"), Background = new SolidColorBrush(Color.FromArgb(0x66, 0, 0, 0)), FontSize = 12 });
            else
            {
                var url = m.Groups[4].Success ? m.Groups[4].Value : m.Groups[5].Value; var txt = m.Groups[3].Success ? m.Groups[3].Value : url;
                var h = new Hyperlink(new Run(txt)) { Foreground = (Brush)FindResource("Acento2"), Cursor = Cursors.Hand, ToolTip = url };
                h.Click += (_, _) => _ = NuevaPestana(url);
                tb.Inlines.Add(h);
            }
            i = m.Index + m.Length;
        }
        if (i < t.Length) tb.Inlines.Add(new Run(t[i..]));
    }

    void Empujar(EntradaChat e)
    {
        if (datos.Chat.Log.Count == 0) ClLog.Children.Clear();
        datos.Chat.Log.Add(e);
        if (datos.Chat.Log.Count > 200) datos.Chat.Log.RemoveRange(0, datos.Chat.Log.Count - 200);
        var n = Entrada(e);
        if (escribiendo != null && ClLog.Children.Contains(escribiendo)) ClLog.Children.Insert(ClLog.Children.IndexOf(escribiendo), n); else ClLog.Children.Add(n);
        if (e.R == "bot") burbujaBot = (TextBlock)((Border)n).Tag;
        ClScroll.ScrollToEnd(); GuardarChatPronto();
    }
    void GuardarChatPronto()
    {
        guardarChat ??= new DispatcherTimer(TimeSpan.FromMilliseconds(400), DispatcherPriority.Background, (_, _) => { guardarChat!.Stop(); datos.GuardarChat(datos.Chat); }, Dispatcher);
        guardarChat.Stop(); guardarChat.Start();
    }
    void MostrarEscribiendo()
    {
        if (escribiendo == null)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            for (int i = 0; i < 3; i++)
            {
                var punto = new System.Windows.Shapes.Ellipse { Width = 6, Height = 6, Fill = Brushes.LightGray, Margin = new Thickness(2, 0, 2, 0) };
                var an = new System.Windows.Media.Animation.DoubleAnimation(0.25, 1, TimeSpan.FromMilliseconds(500)) { AutoReverse = true, RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever, BeginTime = TimeSpan.FromMilliseconds(i * 150) };
                punto.BeginAnimation(OpacityProperty, an); sp.Children.Add(punto);
            }
            escribiendo = Burbuja(sp, (Brush)FindResource("Panel2"), HorizontalAlignment.Left, new CornerRadius(12));
        }
        if (!ClLog.Children.Contains(escribiendo)) ClLog.Children.Add(escribiendo);
        ClScroll.ScrollToEnd();
    }
    void Ocupado(bool b)
    {
        ocupado = b;
        ClEnviarIco.Text = b ? "" : ""; ClEnviarTxt.Text = b ? "Detener" : "Enviar";
        ClEstado.Text = b ? "trabajando…" : "";
        if (b) MostrarEscribiendo(); else { if (escribiendo != null) ClLog.Children.Remove(escribiendo); burbujaBot = null; }
    }

    void Enviar(string? directo = null)
    {
        var texto = (directo ?? ClTexto.Text).Trim();
        if ((texto.Length == 0 && imagenes.Count == 0) || ocupado || chat == null) return;
        var imgs = imagenes.ToList();
        Empujar(new EntradaChat { R = "user", T = texto, Imgs = imgs.Count > 0 ? imgs : null });
        if (directo == null) ClTexto.Text = "";
        imagenes.Clear(); PintarMiniaturas(); Ocupado(true);
        var r = chat.Enviar(texto, imgs, datos.Ajustes.Modelo, datos.Ajustes.Esfuerzo, datos.Chat.Session);
        if (r != null)
        {
            Ocupado(false);
            Empujar(new EntradaChat { R = "err", T = r == "no-encontrado"
                ? "No encontré Claude Code en este equipo. Instala la app de Claude desde https://claude.com/download e inicia sesión; luego vuelve a intentarlo."
                : r });
        }
    }

    void EventoChat(JsonObject d)
    {
        switch (d["k"]?.ToString())
        {
            case "start": if (!ocupado) Ocupado(true); break;
            case "session": datos.Chat.Session = d["session"]?.ToString(); GuardarChatPronto(); break;
            case "msg": burbujaBot = null; break;
            case "text":
                if (burbujaBot == null) { textoBot = ""; entradaBot = new EntradaChat { R = "bot", T = "" }; Empujar(entradaBot); }
                textoBot += d["t"]?.ToString();
                entradaBot!.T = textoBot;
                MarkdownLigero(burbujaBot!, textoBot); ClScroll.ScrollToEnd();
                break;
            case "tool": burbujaBot = null; Empujar(new EntradaChat { R = "tool", T = d["d"]?.ToString() ?? "" }); break;
            case "done":
                if (d["session"]?.ToString() is string s && s.Length > 0) datos.Chat.Session = s;
                if (d["error"]?.ToString() is string err && err.Length > 0) Empujar(new EntradaChat { R = "err", T = err });
                Ocupado(false); GuardarChatPronto();
                break;
        }
    }
}
