using Avalonia.VisualTree;

namespace ProDesigner.Workbench;

public sealed partial class DesignerWorkbench
{
    /// <summary>The currently attached prototype runner, available for host automation and integration tests.</summary>
    public PrototypeRunner? ActivePrototypeRunner => _overlay.IsVisible ? _overlay.GetVisualDescendants().OfType<PrototypeRunner>().FirstOrDefault() : null;
}
