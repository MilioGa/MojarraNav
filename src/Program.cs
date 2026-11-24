using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Windows;

namespace MojarraNav;

public static class Program
{
    public static string DirDatos = "";
    static string TuboInstancia => $"{Marca.Id}-abrir-{Environment.UserName}";

    [STAThread]
    public static int Main(string[] args)
    {
        // Modo servidor MCP: lo lanza Claude Code desde el panel de Claude
        if (args.Contains("--mcp")) return McpServidor.Ejecutar();

        DirDatos = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), Marca.Carpeta);
        Directory.CreateDirectory(DirDatos);

        // Una sola ventana: si ya está abierto, le pasa las direcciones y termina
        using var mutex = new Mutex(true, $"{Marca.Id}-{Environment.UserName}", out var primera);
        if (!primera) { EnviarAInstancia(args); return 0; }

        var app = new App();
        app.InitializeComponent();
        app.DispatcherUnhandledException += (_, e) => { Registrar(e.Exception); e.Handled = true; };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Registrar(e.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, e) => { Registrar(e.Exception); e.SetObserved(); };
        var ventana = new MainWindow(args.Where(a => !a.StartsWith("--")).ToArray());
        EscucharInstancias(ventana);
        return app.Run(ventana);
    }

    /// <summary>Un error inesperado no debe cerrar el navegador: se registra y se sigue.</summary>
    public static void Registrar(object? e)
    {
        try { File.AppendAllText(Path.Combine(DirDatos, "errores.log"), $"[{DateTime.Now:s}] {e}\n"); } catch { }
    }

    static void EnviarAInstancia(string[] args)
    {
        try
        {
            using var c = new NamedPipeClientStream(".", TuboInstancia, PipeDirection.Out);
            c.Connect(3000);
            using var w = new StreamWriter(c, new UTF8Encoding(false));
            w.Write(string.Join("\n", args.Where(a => !a.StartsWith("--"))));
        }
        catch { }
    }

    static void EscucharInstancias(MainWindow v)
    {
        new Thread(() =>
        {
            while (true)
            {
                try
                {
                    using var s = new NamedPipeServerStream(TuboInstancia, PipeDirection.In, 1);
                    s.WaitForConnection();
                    var texto = new StreamReader(s, Encoding.UTF8).ReadToEnd();
                    var urls = texto.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    v.Dispatcher.BeginInvoke(() => v.AbrirDesdeOtraInstancia(urls));
                }
                catch { Thread.Sleep(500); }
            }
        }) { IsBackground = true }.Start();
    }
}
