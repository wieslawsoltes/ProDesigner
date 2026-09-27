using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using Avalonia;
using Avalonia.Browser;
using ProDesigner.Workbench;

[assembly: SupportedOSPlatform("browser")]
namespace ProDesigner.Browser;
public static partial class Program
{
    public static Task Main(string[] args)
    {
        DesignerApplication.RecoveryFactory = () => new BrowserRecoveryStore();
        return AppBuilder.Configure<DesignerApplication>().StartBrowserAppAsync("out");
    }
    [JSExport] public static string ExportWorkspace() => DesignerApplication.Workbench!.ExportWorkspace();
    [JSExport] public static void ImportWorkspace(string json) => DesignerApplication.Workbench!.ImportWorkspace(json);
    [JSExport]
    public static string Snapshot()
    {
        var workbench = DesignerApplication.Workbench;
        if (workbench is null) return "{\"ready\":false}";
        // Use anonymous-object reflection only in the untrimmed browser test bridge? Avoid it: explicit JSON writer.
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject(); writer.WriteBoolean("ready", true); writer.WriteString("document", workbench.ActiveDocumentName);
            writer.WriteNumber("version", workbench.Session.Version); writer.WriteNumber("nodes", workbench.Session.Tree.Elements.Count);
            writer.WriteBoolean("valid", workbench.Session.IsValid); writer.WriteString("source", workbench.Session.Source);
            writer.WriteString("release", typeof(DesignerWorkbench).Assembly.GetName().Version?.ToString());
            writer.WriteString("syntaxUpdate", workbench.Session.LastSyntaxUpdate.ToString());
            writer.WriteString("previewUpdate", workbench.Surface.LastRefresh.ToString());
            writer.WriteNumber("fullPreviewBuilds", workbench.Surface.FullBuildCount);
            writer.WriteNumber("previewDeltas", workbench.Surface.PropertyDeltaCount);
            writer.WriteNumber("documents", workbench.Documents.Count); writer.WriteBoolean("canUndo", workbench.Session.CanUndo);
            writer.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }
    [JSExport]
    public static string Bounds(string name)
    {
        var rect = DesignerApplication.Workbench?.Surface.GetElementBounds(name);
        if (rect is not { } value) return "null";
        return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{{\"x\":{value.X},\"y\":{value.Y},\"width\":{value.Width},\"height\":{value.Height}}}");
    }
    [JSExport]
    public static void Command(string command) => DesignerApplication.Workbench?.Execute(command);
    [JSExport]
    public static void Select(string name) => DesignerApplication.Workbench?.SelectByName(name);
    [JSExport]
    public static void SetProperty(string name, string value) => DesignerApplication.Workbench?.SetProperty(name, value);
    [JSExport]
    public static void Insert(string name) => DesignerApplication.Workbench?.InsertControl(name);
    [JSExport]
    public static void SetSource(string source) => DesignerApplication.Workbench?.Session.SetSource(source);
}
