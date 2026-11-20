using Ares.Editor.App.Editing;
using Ares.Editor.App.Platform;
using Ares.Editor.Core.Paths;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Photino.Blazor;

namespace Ares.Editor.App;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // The window's files are wwwroot (Photino.Blazor's default) plus the map geometry chunks, served under the same
        // app:// origin so the page can fetch() them as binary.
        using PhysicalFileProvider wwwroot = new(Path.Combine(AppContext.BaseDirectory, "wwwroot"));
        GeometryFiles files = new(wwwroot);
        PhotinoBlazorAppBuilder builder = PhotinoBlazorAppBuilder.CreateDefault(files, args);
        builder.Services.AddSingleton(new EditorPaths());
        builder.Services.AddSingleton<EditorSession>();
        builder.Services.AddSingleton(files);
        builder.Services.AddSingleton<GeometrySession>();
        builder.Services.AddSingleton<SceneOptions>();
        builder.Services.AddSingleton<FolderPicker>();
        builder.RootComponents.Add<App>("app");

        PhotinoBlazorApp app = builder.Build();
        // Verbosity 0 stops Photino tracing every call (each web message with its render batch); its failures still
        // surface as exceptions.
        app.MainWindow
            .SetLogVerbosity(0)
            .SetTitle("ARES-Editor")
            .SetSize(1280, 800)
            .SetMinSize(1024, 640)
            .Center();
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            app.MainWindow.ShowMessage("Error", e.ExceptionObject.ToString());
        app.Run();
    }
}
