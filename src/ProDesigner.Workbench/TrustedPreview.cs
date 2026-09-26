using Avalonia.Controls;

namespace ProDesigner.Workbench;

public sealed record TrustedPreview(Control Root, Action? Release = null) : IDisposable
{
    public void Dispose() => Release?.Invoke();
}
