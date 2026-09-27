using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using ProDesigner.Core;
using ProDesigner.Runtime;
using ProDesigner.TestFixture;
using ProDesigner.Workspaces;
using Xunit;

namespace ProDesigner.Tests;
public class RuntimeTests
{
    [AvaloniaFact]
    public void RuntimePreviewUsesRealAvaloniaXamlAndCustomProjectControls()
    {
        const string source = "<UserControl xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:local=\"using:ProDesigner.TestFixture\" x:Class=\"ProDesigner.TestFixture.PreviewView\"><local:ProjectCard Caption=\"Resolved from project metadata\" Width=\"123\" /></UserControl>";
        using var preview = new RuntimePreviewEngine().Load(new(source, Trusted: true), typeof(PreviewView).Assembly.Location);
        Assert.Equal("ProDesigner.TestFixture.PreviewView", preview.Root.GetType().FullName);
        var child = Assert.IsAssignableFrom<Control>(((UserControl)preview.Root).Content);
        Assert.Equal("ProDesigner.TestFixture.ProjectCard", child.GetType().FullName); Assert.Equal(123,child.Width);
        Assert.Equal("Resolved from project metadata",child.GetType().GetProperty("Caption")!.GetValue(child));
    }
    [Fact] public void UntrustedRuntimeNeverLoadsCode() => Assert.Throws<UnauthorizedAccessException>(() => new RuntimePreviewEngine().Load(new("<UserControl/>")));
    [Fact] public async Task UntrustedBuildNeverStartsAProcess() => await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new DotNetProjectBuilder().BuildAsync(new("missing.csproj", false), TestContext.Current.CancellationToken));
}
