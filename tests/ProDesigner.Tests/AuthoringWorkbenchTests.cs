using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using ProDesigner.Animation;
using ProDesigner.Persistence;
using ProDesigner.Workbench;
using Xunit;

namespace ProDesigner.Tests;
public class AuthoringWorkbenchTests
{
    [AvaloniaFact] public void WorkspaceRoundTripRestoresTabsSourceCodeAndTimeline()
    {
        using var workbench=new DesignerWorkbench();workbench.SelectByName("RevenueCard");workbench.SetProperty("Width","280");workbench.Documents[0].CodeBehind="class Draft {}";
        var json=workbench.ExportWorkspace();workbench.Execute("new");workbench.ImportWorkspace(json);
        Assert.Equal(3,workbench.Documents.Count);Assert.Equal("280",workbench.Session.Primary!.Get("Width"));Assert.Equal("class Draft {}",workbench.Documents[0].CodeBehind);Assert.Single(workbench.Documents[0].Animation.Tracks);
    }
    [AvaloniaFact] public void AuthoringToolsAndVectorEditorCanBeOpened()
    {
        using var workbench=new DesignerWorkbench();var window=new Window{Width=1600,Height=1000,Content=workbench};window.Show();
        workbench.Execute("authoring");workbench.Execute("new");workbench.InsertControl("Path");workbench.Execute("vector");Assert.Contains("<Path",workbench.Session.Source);window.Close();
    }
    [AvaloniaFact] public void NewControlInsertionWorksWithoutAnExistingXPrefix()
    {
        using var workbench=new DesignerWorkbench();workbench.OpenDocument("View.axaml","<UserControl xmlns='https://github.com/avaloniaui'><Canvas/></UserControl>");workbench.InsertControl("Button");Assert.True(workbench.Session.IsValid);Assert.Contains("Button1",workbench.Session.Source);
    }
    [AvaloniaFact] public void ClipboardPasteKeepsInternalBindingReferencesValid()
    {
        using var workbench=new DesignerWorkbench();workbench.OpenDocument("View.axaml","<UserControl xmlns='https://github.com/avaloniaui' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'><Canvas><StackPanel x:Name='Group'><TextBox x:Name='Input'/><TextBlock Text='{Binding ElementName=Input}'/></StackPanel></Canvas></UserControl>");
        workbench.SelectByName("Group");workbench.Execute("copy");workbench.SelectByName("Canvas");workbench.Execute("paste");
        Assert.Contains("ElementName=InputCopy",workbench.Session.Source);Assert.True(workbench.Session.IsValid);
    }
}
