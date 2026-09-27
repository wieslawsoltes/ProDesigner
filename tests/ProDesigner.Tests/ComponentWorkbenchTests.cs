using Avalonia.Headless.XUnit;
using ProDesigner.DesignSystems;
using ProDesigner.Persistence;
using ProDesigner.Prototyping;
using ProDesigner.Workbench;
using ProDesigner.Xaml;
using Xunit;

namespace ProDesigner.Tests;

public class ComponentWorkbenchTests
{
    private static ComponentDefinition Capture(DesignerWorkbench workbench)
    {
        workbench.SelectByName("RevenueCard"); return workbench.CaptureComponent("Revenue card");
    }
    [AvaloniaFact]
    public void InsertedComponentHasARealSourceLinkAndGroupedUndo()
    {
        using var workbench = new DesignerWorkbench(); var component = Capture(workbench);
        workbench.SelectByName("DashboardCanvas"); var original = workbench.Session.Source;
        var instance = workbench.InsertComponent(component.Id);
        Assert.Single(workbench.DesignSystem.Instances); Assert.Contains(instance.RootName, workbench.Session.Source);
        workbench.UndoStudioTransaction(); Assert.Equal(original, workbench.Session.Source); Assert.Empty(workbench.DesignSystem.Instances);
        workbench.RedoStudioTransaction(); Assert.Single(workbench.DesignSystem.Instances); Assert.Contains(instance.RootName, workbench.Session.Source);
    }
    [AvaloniaFact]
    public void ComponentChangesPropagateAcrossViewsAndUndoAsOneOperation()
    {
        using var workbench = new DesignerWorkbench(); var component = Capture(workbench);
        workbench.SelectByName("DashboardCanvas"); var first = workbench.InsertComponent(component.Id); var firstDocument = workbench.Documents[0];
        workbench.Execute("new"); var second = workbench.InsertComponent(component.Id); var secondDocument = workbench.Documents[^1];
        var firstSource = firstDocument.Session.Source; var secondSource = secondDocument.Session.Source;
        workbench.UpdateComponent(component.Id, component.Xaml.Replace("Total revenue", "Updated revenue"), []);
        Assert.Contains("Updated revenue", firstDocument.Session.Source); Assert.Contains("Updated revenue", secondDocument.Session.Source);
        Assert.All(workbench.DesignSystem.Instances, i => Assert.Equal(2, i.AppliedRevision));
        workbench.UndoStudioTransaction(); Assert.Equal(firstSource, firstDocument.Session.Source); Assert.Equal(secondSource, secondDocument.Session.Source);
        Assert.All(workbench.DesignSystem.Instances, i => Assert.Equal(1, i.AppliedRevision));
        workbench.RedoStudioTransaction(); Assert.Contains("Updated revenue", firstDocument.Session.Source);
    }
    [AvaloniaFact]
    public void ConflictingInstancePreventsChangesToEveryView()
    {
        using var workbench = new DesignerWorkbench(); var component = Capture(workbench);
        workbench.SelectByName("DashboardCanvas"); var instance = workbench.InsertComponent(component.Id); var first = workbench.Documents[0];
        workbench.Execute("new"); workbench.InsertComponent(component.Id); var second = workbench.Documents[^1];
        workbench.SelectByName(workbench.DesignSystem.Instances[^1].RootName); workbench.Session.SetProperty("Width", "333");
        var a = first.Session.Source; var b = second.Session.Source;
        Assert.Throws<ComponentConflictException>(() => workbench.UpdateComponent(component.Id, component.Xaml.Replace("Total revenue", "Changed"), []));
        Assert.Equal(a, first.Session.Source); Assert.Equal(b, second.Session.Source); Assert.Equal(1, workbench.DesignSystem.Components.Single().Revision);
    }
    [AvaloniaFact]
    public void ExplicitOverridesSurviveDefinitionUpdates()
    {
        using var workbench = new DesignerWorkbench(); var component = Capture(workbench); workbench.SelectByName("DashboardCanvas");
        var instance = workbench.InsertComponent(component.Id);
        workbench.SetInstanceOverrides(instance.Id, null, [new("$root", "Width", "321")]);
        workbench.UpdateComponent(component.Id, component.Xaml.Replace("Total revenue", "Updated revenue"), []);
        workbench.SelectByName(instance.RootName); Assert.Equal("321", workbench.Session.Primary!.Get("Width"));
    }
    [AvaloniaFact]
    public void WorkspaceRoundTripKeepsStableLinksPrototypeAndSourceHistory()
    {
        using var workbench = new DesignerWorkbench(); var component = Capture(workbench); workbench.SelectByName("DashboardCanvas");
        var instance = workbench.InsertComponent(component.Id); workbench.SelectByName("Greeting"); workbench.Session.SetProperty("Text", "Persisted greeting");
        var sourceId = workbench.DocumentId(workbench.Documents[0]); var targetId = workbench.DocumentId(workbench.Documents[1]);
        workbench.AddPrototypeLink(new("next", sourceId, "CreateProject", PrototypeTrigger.Click, PrototypeAction.Navigate, targetId));
        var json = workbench.ExportWorkspace(); var state = WorkspaceCodec.Deserialize(json); Assert.Equal(2, state.SchemaVersion);
        using var restored = new DesignerWorkbench(); restored.ImportWorkspace(json);
        Assert.Equal(sourceId, restored.DocumentId(restored.Documents[0])); Assert.Equal(instance.Id, restored.DesignSystem.Instances.Single().Id);
        Assert.Equal(targetId, restored.Prototype.Links.Single().TargetDocument); Assert.True(restored.Session.CanUndo);
        restored.Session.Undo(); Assert.DoesNotContain("Persisted greeting", restored.Session.Source);
        restored.Session.Redo(); Assert.Contains("Persisted greeting", restored.Session.Source);
    }
    [AvaloniaFact]
    public void SourceEditingAfterGroupedUpdateIsNotOverwrittenByGroupedUndo()
    {
        using var workbench = new DesignerWorkbench(); var component = Capture(workbench); workbench.SelectByName("DashboardCanvas");
        workbench.InsertComponent(component.Id); workbench.Session.SetSource(workbench.Session.Source + "\n<!-- later edit -->");
        var source = workbench.Session.Source;
        Assert.Throws<InvalidOperationException>(workbench.UndoStudioTransaction); Assert.Equal(source, workbench.Session.Source);
    }
    [AvaloniaFact]
    public void ToolDialogsAndPrototypeCanBeCreatedWithoutExecutingProjectCode()
    {
        using var workbench = new DesignerWorkbench();
        workbench.Execute("components"); workbench.Execute("prototype"); workbench.Execute("constraints"); workbench.RunPrototype();
        Assert.True(workbench.Session.IsValid);
    }
}
