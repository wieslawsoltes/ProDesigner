using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using ProDesigner.Roslyn;
using Xunit;

namespace ProDesigner.Tests;

public class CrossLanguageRenameTests
{
    private static (AdhocWorkspace Workspace, ProjectId Project) Create(string source, string? other = null)
    {
        var workspace = new AdhocWorkspace(); var id = ProjectId.CreateNewId();
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Concat([typeof(Avalonia.Controls.Control).Assembly.Location, typeof(Avalonia.AvaloniaObject).Assembly.Location]).Distinct().Select(p => MetadataReference.CreateFromFile(p));
        var solution = workspace.CurrentSolution.AddProject(id, "App", "App", LanguageNames.CSharp)
            .WithProjectCompilationOptions(id, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)).AddMetadataReferences(id, references);
        solution = solution.AddDocument(DocumentId.CreateNewId(id), "View.cs", SourceText.From(source), filePath: Path.GetFullPath("View.cs"));
        if (other is not null) solution = solution.AddDocument(DocumentId.CreateNewId(id), "Other.cs", SourceText.From(other), filePath: Path.GetFullPath("Other.cs"));
        Assert.True(workspace.TryApplyChanges(solution)); return (workspace, id);
    }
    [Fact]
    public async Task TypeRenameUpdatesCSharpAndQualifiedXamlButNotLiteralsOrComments()
    {
        var (workspace, id) = Create("namespace Demo; public partial class View : Avalonia.Controls.UserControl {}", "// View stays in this comment\nnamespace Demo; class Consumer { View field = new View(); string Text = \"View\"; }");
        using var disposable = workspace;
        var xaml = "<local:View xmlns='https://github.com/avaloniaui' xmlns:local='using:Demo' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' x:Class='Demo.View'><!-- View --><TextBlock Text='View'/></local:View>";
        var plan = await new CrossLanguageRenameService().PrepareAsync(workspace.CurrentSolution, id, "Demo.View", null, "Dashboard", [new(id, "View.axaml", xaml)], TestContext.Current.CancellationToken);
        Assert.True(plan.CanApply); Assert.Equal(3, plan.Files.Length);
        var markup = plan.Files.Single(f => f.Path == "View.axaml").After;
        Assert.Contains("<local:Dashboard", markup); Assert.Contains("</local:Dashboard>", markup); Assert.Contains("x:Class='Demo.Dashboard'", markup); Assert.Contains("<!-- View -->", markup); Assert.Contains("Text='View'", markup);
        var csharp = plan.Files.Single(f => f.Path.EndsWith("Other.cs", StringComparison.Ordinal)).After;
        Assert.Contains("Dashboard field = new Dashboard()", csharp); Assert.Contains("// View stays", csharp); Assert.Contains("\"View\"", csharp);
    }
    [Fact]
    public async Task EventHandlerRenameUpdatesOnlyActualCodeBehindEventReferences()
    {
        var (workspace, id) = Create("namespace Demo; public class View : Avalonia.Controls.UserControl { private void Save(object sender, Avalonia.Interactivity.RoutedEventArgs e) {} private void Other() { Save(this, null); } }"); using var disposable = workspace;
        var xaml = "<UserControl xmlns='https://github.com/avaloniaui' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' x:Class='Demo.View'><Button Click='Save' Content='Save'/></UserControl>";
        var plan = await new CrossLanguageRenameService().PrepareAsync(workspace.CurrentSolution,id,"Demo.View","Save","Commit",[new(id,"View.axaml",xaml)],TestContext.Current.CancellationToken);
        Assert.True(plan.CanApply); Assert.Contains("Click='Commit' Content='Save'",plan.Files.Single(f=>f.Path=="View.axaml").After);
        Assert.Contains("Commit(this, null)",plan.Files.Single(f=>f.Path.EndsWith("View.cs",StringComparison.Ordinal)).After);
    }
    [Fact]
    public async Task CustomPropertyRenameUsesTheElementTypeAndPropertyElementOwner()
    {
        var (workspace,id)=Create("namespace Demo; public class Card : Avalonia.Controls.Border { public string Caption { get;set; } } public class Other : Avalonia.Controls.Border { public string Caption {get;set;} }");using var disposable=workspace;
        var xaml="<Grid xmlns='https://github.com/avaloniaui' xmlns:l='using:Demo'><l:Card Caption='One'/><l:Other Caption='Other'/><l:Card><l:Card.Caption>Two</l:Card.Caption></l:Card></Grid>";
        var plan=await new CrossLanguageRenameService().PrepareAsync(workspace.CurrentSolution,id,"Demo.Card","Caption","Heading",[new(id,"View.axaml",xaml)],TestContext.Current.CancellationToken);
        Assert.True(plan.CanApply);var result=plan.Files.Single(f=>f.Path=="View.axaml").After;
        Assert.Contains("<l:Card Heading='One'",result);Assert.Contains("<l:Other Caption='Other'",result);Assert.Contains("<l:Card.Heading>Two</l:Card.Heading>",result);
    }
    [Fact]
    public async Task ConflictingAndInvalidRenamesAreRejectedBeforeFilesAreWritten()
    {
        var (workspace,id)=Create("namespace Demo; public class View {} public class Existing {}");using var disposable=workspace;
        await Assert.ThrowsAsync<InvalidOperationException>(()=>new CrossLanguageRenameService().PrepareAsync(workspace.CurrentSolution,id,"Demo.View",null,"Existing",[],TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(()=>new CrossLanguageRenameService().PrepareAsync(workspace.CurrentSolution,id,"Demo.View",null,"not valid",[],TestContext.Current.CancellationToken));
    }
    [Fact]
    public async Task BindingPathsAreFlaggedRatherThanBlindlyReplaced()
    {
        var (workspace,id)=Create("namespace Demo; public class Model { public string Title {get;set;} }");using var disposable=workspace;
        var plan=await new CrossLanguageRenameService().PrepareAsync(workspace.CurrentSolution,id,"Demo.Model","Title","Heading",[new(id,"View.axaml","<TextBlock xmlns='https://github.com/avaloniaui' Text='{Binding Title}'/>")],TestContext.Current.CancellationToken);
        Assert.Contains(plan.Diagnostics,d=>d.Code=="RENAME101");Assert.DoesNotContain(plan.Files,f=>f.Path=="View.axaml");
    }
}
