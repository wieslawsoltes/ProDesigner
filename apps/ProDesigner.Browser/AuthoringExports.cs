using System.Runtime.InteropServices.JavaScript;
using System.Text;
using System.Text.Json;
using ProDesigner.DesignSystems;
using ProDesigner.Prototyping;
using ProDesigner.Workbench;

namespace ProDesigner.Browser;

public static partial class Program
{
    private static DesignerWorkbench Studio => DesignerApplication.Workbench ?? throw new InvalidOperationException("The workbench is not initialized.");
    [JSExport]
    public static string CaptureLinkedComponent(string name) => Studio.CaptureComponent(name).Id;
    [JSExport]
    public static string InsertLinkedComponent(string id) => Studio.InsertComponent(id).Id;
    [JSExport]
    public static void UpdateLinkedComponent(string id, string source)
    {
        var component = Studio.DesignSystem.Components.Single(c => c.Id == id); Studio.UpdateComponent(id, source, component.Variants);
    }
    [JSExport]
    public static void SetLinkedOverride(string instanceId, string target, string property, string value)
    {
        var instance = Studio.DesignSystem.Instances.Single(i => i.Id == instanceId);
        Studio.SetInstanceOverrides(instanceId, instance.Variant, [.. instance.Overrides.Where(v => v.Target != target || v.Property != property), new ComponentOverride(target, property, value)]);
    }
    [JSExport]
    public static void ConfigurePrototype(int sourceIndex, string control, int destinationIndex)
    {
        var source = Studio.Documents[sourceIndex]; var target = Studio.Documents[destinationIndex];
        var id = Studio.DocumentId(source);
        Studio.SetPrototype(new(id, [new("primary", id, control, PrototypeTrigger.Click, PrototypeAction.Navigate, Studio.DocumentId(target))], []));
    }
    [JSExport]
    public static string PrototypeStateJson()
    {
        var runner = Studio.ActivePrototypeRunner; if (runner is null) return "null";
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject(); writer.WriteString("document", runner.CurrentViewName); writer.WriteNumber("backCount", runner.Player.State.BackCount); writer.WriteNumber("overlays", runner.Player.State.Overlays.Length); writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }
    [JSExport]
    public static string PrototypeBounds(string name)
    {
        var rect = Studio.ActivePrototypeRunner?.GetElementBounds(name); if (rect is not { } value) return "null";
        return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{{\"x\":{value.X},\"y\":{value.Y},\"width\":{value.Width},\"height\":{value.Height}}}");
    }
    [JSExport]
    public static void PrototypeBack() => Studio.ActivePrototypeRunner?.Player.Back();
}
