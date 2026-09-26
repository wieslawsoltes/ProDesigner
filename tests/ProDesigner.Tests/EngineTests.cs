using System.Xml.Linq;
using ProDesigner.Animation;
using ProDesigner.Design;
using ProDesigner.Roslyn;
using ProDesigner.Workspaces;
using ProDesigner.Xaml;
using Xunit;

namespace ProDesigner.Tests;
public class EngineTests
{
    [Theory] [InlineData(3, 0)] [InlineData(5, 8)] [InlineData(-5, -8)] [InlineData(16, 16)]
    public void SnappingIsDeterministic(double value, double expected) => Assert.Equal(expected, LayoutEngine.Snap(value));
    [Fact] public void SmartGuidesSnapEdges()
    {
        var result = LayoutEngine.SnapToObjects(new(98, 12, 40, 20), [new(100, 80, 60, 40)], 6, 0);
        Assert.Equal(100, result.X); Assert.Contains(100d, result.VerticalGuides);
    }
    [Fact] public void AutoLayoutIsNotSilentlyConvertedToAbsolutePositioning()
    {
        var tree = XamlSyntaxTree.Parse("<StackPanel><Button /></StackPanel>"); Assert.Throws<InvalidOperationException>(() => LayoutEngine.Move(tree, tree.Elements[1], 10, 20));
    }
    [Fact] public void CubicBezierInvertsTheXCoordinate()
    {
        Assert.InRange(Easing.CubicBezier(.5, .42, 0, .58, 1), .49999, .50001);
        Assert.InRange(Easing.CubicBezier(0, .42, 0, .58, 1), 0, .00001);
    }
    [Fact] public void TimelineReplacesDuplicateTimeAndSorts()
    {
        var track = new AnimationTrack("Card", "Opacity"); track.SetKey(1, 1); track.SetKey(0, 0); track.SetKey(1, .8);
        Assert.Equal(2, track.Keys.Count); Assert.Equal(0, track.Keys[0].Time); Assert.Equal(.8, track.Evaluate(1)); Assert.Equal(.4, track.Evaluate(.5), 8);
    }
    [Fact] public void TimelineRejectsInvalidValues()
    {
        var track = new AnimationTrack("Card", "Opacity"); Assert.Throws<ArgumentOutOfRangeException>(() => track.SetKey(-1, 1)); Assert.Throws<ArgumentOutOfRangeException>(() => track.SetKey(0, double.NaN));
    }
    [Fact] public void KeyframeRemovalWorks()
    {
        var track = new AnimationTrack("Card", "Opacity"); track.SetKey(0, 1); track.RemoveKey(0); Assert.Empty(track.Keys);
    }
    [Fact] public void AnimationExportProducesRealAvaloniaStyleMarkup()
    {
        var clip = new AnimationClip { DurationSeconds = 1.5 }; var track = clip.GetTrack("Card", "Opacity"); track.SetKey(0, 0); track.SetKey(1, 1);
        var xml = XElement.Parse(clip.ToAvaloniaStyles()); Assert.Equal("Style", xml.Name.LocalName); Assert.Equal("#Card", xml.Attribute("Selector")!.Value);
        Assert.Equal("00:00:01.5000000", xml.Descendants("Animation").Single().Attribute("Duration")!.Value); Assert.Equal(3, xml.Descendants("KeyFrame").Count());
    }
    [Fact] public void RoslynGenerationPreservesExistingCommentsAndIsIdempotent()
    {
        const string source = "// preserve this\nnamespace Demo;\npublic partial class View { }";
        var service = new RoslynCodeService(); var result = service.EnsureEventHandler(source, "View", "OnClick");
        Assert.StartsWith("// preserve this", result); Assert.Contains("OnClick", result); Assert.Empty(service.Validate(result)); Assert.Equal(result, service.EnsureEventHandler(result, "View", "OnClick"));
    }
    [Fact] public void RoslynRejectsInvalidHandlerIdentifiers() => Assert.Throws<ArgumentException>(() => new RoslynCodeService().EnsureEventHandler("class View {}", "View", "bad name"));
    [Fact] public void RoslynReportsSyntaxLocations() => Assert.NotEmpty(new RoslynCodeService().Validate("class {"));
    [Fact] public async Task UntrustedWorkspaceDoesNotEvaluateProjects()
    {
        using var workspace = new SolutionWorkspace(); await Assert.ThrowsAsync<UnauthorizedAccessException>(() => workspace.OpenAsync("not-a-file.csproj", false, TestContext.Current.CancellationToken));
    }
    [Fact] public void ProjectInspectionFindsSourcesReferencesAndFrameworks()
    {
        var directory = Path.Combine(Path.GetTempPath(), "prodesigner-tests-" + Guid.NewGuid()); Directory.CreateDirectory(directory);
        try
        {
            var project = Path.Combine(directory, "Demo.csproj");
            File.WriteAllText(project, "<Project><PropertyGroup><TargetFrameworks>net10.0;net8.0</TargetFrameworks></PropertyGroup><ItemGroup><PackageReference Include=\"Avalonia\" Version=\"12.1.3\"/><ProjectReference Include=\"Other.csproj\"/></ItemGroup></Project>");
            File.WriteAllText(Path.Combine(directory, "View.axaml"), "<UserControl/>"); Directory.CreateDirectory(Path.Combine(directory, "obj")); File.WriteAllText(Path.Combine(directory, "obj", "Hidden.cs"), "");
            var result = SolutionWorkspace.InspectProject(project); Assert.Equal(2, result.Frameworks.Count); Assert.Single(result.Packages); Assert.Single(result.ProjectReferences); Assert.Single(result.Files);
        }
        finally { Directory.Delete(directory, true); }
    }
}
