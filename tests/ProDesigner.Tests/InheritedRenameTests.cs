using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using ProDesigner.Core;
using ProDesigner.Roslyn;
using Xunit;

namespace ProDesigner.Tests;

public class InheritedRenameTests
{
    private static async Task<RefactoringPlan> Rename(string source, string xaml, string typeName = "Demo.Base", string? member = "Caption", string name = "Heading")
    {
        using var workspace = new AdhocWorkspace(); var id = ProjectId.CreateNewId();
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Concat([typeof(Avalonia.Controls.Control).Assembly.Location, typeof(Avalonia.AvaloniaObject).Assembly.Location]).Distinct().Select(p => MetadataReference.CreateFromFile(p));
        var solution = workspace.CurrentSolution.AddProject(id, "App", "App", LanguageNames.CSharp)
            .WithProjectCompilationOptions(id, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)).AddMetadataReferences(id, references)
            .AddDocument(DocumentId.CreateNewId(id), "Types.cs", SourceText.From(source), filePath: Path.GetFullPath("Types.cs"));
        Assert.True(workspace.TryApplyChanges(solution));
        return await new CrossLanguageRenameService().PrepareAsync(workspace.CurrentSolution, id, typeName, member, name,
            [new(id, "View.axaml", xaml)], TestContext.Current.CancellationToken);
    }
    [Fact]
    public async Task HiddenDerivedPropertiesAreNotRenamedWithTheirBaseProperty()
    {
        const string code = "namespace Demo; public class Base : Avalonia.Controls.Border { public string Caption {get;set;} } public class Hidden : Base { public new string Caption {get;set;} } public class Inherited : Base {}";
        const string xaml = "<Grid xmlns='https://github.com/avaloniaui' xmlns:l='using:Demo'><l:Base Caption='A'/><l:Hidden Caption='B'/><l:Inherited Caption='C'/><l:Hidden><l:Hidden.Caption>D</l:Hidden.Caption></l:Hidden></Grid>";
        var plan = await Rename(code, xaml); Assert.True(plan.CanApply);
        var result = plan.Files.Single(f => f.Path == "View.axaml").After;
        Assert.Contains("<l:Base Heading='A'", result); Assert.Contains("<l:Inherited Heading='C'", result);
        Assert.Contains("<l:Hidden Caption='B'", result); Assert.Contains("<l:Hidden.Caption>D</l:Hidden.Caption>", result);
    }
    [Fact]
    public async Task TrueOverridesRemainLinkedToTheRenamedBaseMember()
    {
        const string code = "namespace Demo; public class Base : Avalonia.Controls.Border { public virtual string Caption {get;set;} } public class Derived : Base { public override string Caption {get;set;} }";
        var plan = await Rename(code, "<l:Derived xmlns:l='using:Demo' Caption='A'/>"); Assert.True(plan.CanApply);
        Assert.Contains("Heading='A'", plan.Files.Single(f => f.Path == "View.axaml").After);
        Assert.Contains("override string Heading", plan.Files.Single(f => f.Path.EndsWith("Types.cs", StringComparison.Ordinal)).After);
    }
    [Fact]
    public async Task RenamingToADerivedMemberThatWouldChangeXamlBindingIsBlocked()
    {
        const string code = "namespace Demo; public class Base : Avalonia.Controls.Border { public string Caption {get;set;} } public class Derived : Base { public string Heading {get;set;} }";
        var plan = await Rename(code, "<l:Derived xmlns:l='using:Demo' Caption='A'/>");
        Assert.False(plan.CanApply); Assert.Contains(plan.Diagnostics, d => d.Code == "RENAME005");
    }
    [Fact]
    public async Task UnsupportedSelectorsRemainUnchangedAndReceiveReviewDiagnostics()
    {
        var plan = await Rename("namespace Demo; public class Widget : Avalonia.Controls.Border {}",
            "<Style xmlns='https://github.com/avaloniaui' xmlns:l='using:Demo' Selector='l|Widget:selected'/>", "Demo.Widget", null, "PanelWidget");
        Assert.Contains(plan.Diagnostics, d => d.Code == "RENAME103"); Assert.DoesNotContain(plan.Files, f => f.Path == "View.axaml");
    }
}
