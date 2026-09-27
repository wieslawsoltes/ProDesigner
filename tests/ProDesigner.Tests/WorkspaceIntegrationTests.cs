using ProDesigner.Core;
using ProDesigner.Persistence;
using ProDesigner.Workspaces;
using Xunit;

namespace ProDesigner.Tests;

public class WorkspaceIntegrationTests
{
    [Fact]
    public async Task MsBuildLoadsAndResolvesARealProjectReferenceGraph()
    {
        var cancellation = TestContext.Current.CancellationToken;
        var directory = CreateDirectory("workspace");
        Directory.CreateDirectory(Path.Combine(directory, "Dependency"));
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "NuGet.Config"), "<configuration><packageSources><clear/></packageSources></configuration>", cancellation);
            var project = Path.Combine(directory, "Root.csproj");
            await File.WriteAllTextAsync(project, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><NuGetAudit>false</NuGetAudit></PropertyGroup><ItemGroup><Compile Remove=\"Dependency/**/*.cs\"/><ProjectReference Include=\"Dependency/Dependency.csproj\"/></ItemGroup></Project>", cancellation);
            await File.WriteAllTextAsync(Path.Combine(directory, "Root.cs"), "public class Root { public int Value => Dependency.Value; }", cancellation);
            await File.WriteAllTextAsync(Path.Combine(directory, "Dependency", "Dependency.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><NuGetAudit>false</NuGetAudit></PropertyGroup></Project>", cancellation);
            await File.WriteAllTextAsync(Path.Combine(directory, "Dependency", "Dependency.cs"), "public class Dependency { public static int Value => 42; }", cancellation);
            var build = await new DotNetProjectBuilder().BuildAsync(new(project, true), cancellation);
            Assert.True(build.Success, build.Log);
            WorkspaceBootstrap.Initialize();
            using var workspace = WorkspaceBootstrap.Create();
            var projects = await workspace.OpenAsync(project, true, cancellation);
            Assert.Equal(2, projects.Count);
            var diagnostics = await workspace.GetDiagnosticsAsync(cancellation);
            Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task RealProjectRenamePreservesUnsavedOverlaysAndCanBeJournaledAndReverted()
    {
        var cancellation = TestContext.Current.CancellationToken;
        var directory = CreateDirectory("rename");
        try
        {
            var project = Path.Combine(directory, "Sample.csproj");
            var view = Path.Combine(directory, "View.cs");
            var consumer = Path.Combine(directory, "Consumer.cs");
            var markup = Path.Combine(directory, "View.axaml");
            const string code = "namespace Demo; public class View { public int Value => 42; }";
            const string reference = "namespace Demo; public class Consumer { public View Current { get; } = new View(); }";
            const string xaml = "<local:View xmlns:local='using:Demo' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' x:Class='Demo.View' />";
            await File.WriteAllTextAsync(Path.Combine(directory, "NuGet.Config"), "<configuration><packageSources><clear/></packageSources></configuration>", cancellation);
            await File.WriteAllTextAsync(project, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><NuGetAudit>false</NuGetAudit></PropertyGroup></Project>", cancellation);
            await File.WriteAllTextAsync(view, code, cancellation);
            await File.WriteAllTextAsync(consumer, reference, cancellation);
            await File.WriteAllTextAsync(markup, xaml, cancellation);
            var build = await new DotNetProjectBuilder().BuildAsync(new(project, true), cancellation);
            Assert.True(build.Success, build.Log);

            WorkspaceBootstrap.Initialize();
            using var workspace = WorkspaceBootstrap.Create();
            await workspace.OpenAsync(project, true, cancellation);
            var service = Assert.IsAssignableFrom<IProjectRefactoringService>(workspace);
            var plan = await service.PrepareRenameAsync(new(project, "Demo.View", null, "Dashboard", [new(view, code + "\n// unsaved comment")]), cancellation);
            Assert.True(plan.CanApply, string.Join("\n", plan.Diagnostics.Select(d => d.Message)));
            Assert.Equal(3, plan.Files.Length);
            var viewChange = plan.Files.Single(f => f.Path == view);
            Assert.Equal(code, viewChange.DiskBefore);
            Assert.Contains("unsaved comment", viewChange.Before);
            Assert.Contains("unsaved comment", viewChange.After);
            Assert.Contains("class Dashboard", viewChange.After);
            Assert.Contains("local:Dashboard", plan.Files.Single(f => f.Path == markup).After);
            Assert.Equal(code, await File.ReadAllTextAsync(view, cancellation));

            var store = new JournaledFileTransaction(Path.Combine(directory, "journal"));
            var receipt = await store.ApplyAsync(plan.Files.Select(f => new FileTextChange(f.Path, f.DiskBefore, f.After)).ToArray(), cancellation);
            await service.RefreshFilesAsync(plan.Files.Select(f => new OpenSourceFile(f.Path, f.After)).ToArray(), cancellation);
            Assert.DoesNotContain(await workspace.GetDiagnosticsAsync(cancellation), d => d.Severity == DiagnosticSeverity.Error);
            Assert.Contains("Dashboard Current", await File.ReadAllTextAsync(consumer, cancellation));
            await store.RevertAsync(receipt, plan.Files.Select(f => f.Path).ToArray(), cancellation);
            Assert.Equal(code, await File.ReadAllTextAsync(view, cancellation));
            Assert.Equal(reference, await File.ReadAllTextAsync(consumer, cancellation));
            Assert.Equal(xaml, await File.ReadAllTextAsync(markup, cancellation));
        }
        finally { Directory.Delete(directory, true); }
    }

    private static string CreateDirectory(string purpose)
    {
        var path = Path.Combine(Path.GetTempPath(), "prodesigner-" + purpose + "-" + Guid.NewGuid().ToString("N"));
        if (OperatingSystem.IsMacOS() && path.StartsWith("/var/", StringComparison.Ordinal)) path = "/private" + path;
        Directory.CreateDirectory(path);
        return path;
    }
}
