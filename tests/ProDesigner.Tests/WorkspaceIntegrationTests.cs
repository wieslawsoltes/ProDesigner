using ProDesigner.Core;
using ProDesigner.Workspaces;
using Xunit;

namespace ProDesigner.Tests;
public class WorkspaceIntegrationTests
{
    [Fact]
    public async Task MsBuildLoadsAndResolvesARealProjectReferenceGraph()
    {
        var cancellation=TestContext.Current.CancellationToken;
        var directory=Path.Combine(Path.GetTempPath(),"prodesigner-workspace-"+Guid.NewGuid());Directory.CreateDirectory(Path.Combine(directory,"Dependency"));
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory,"NuGet.Config"),"<configuration><packageSources><clear/></packageSources></configuration>",cancellation);
            var project=Path.Combine(directory,"Root.csproj");
            await File.WriteAllTextAsync(project,"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><NuGetAudit>false</NuGetAudit></PropertyGroup><ItemGroup><Compile Remove=\"Dependency/**/*.cs\"/><ProjectReference Include=\"Dependency/Dependency.csproj\"/></ItemGroup></Project>",cancellation);
            await File.WriteAllTextAsync(Path.Combine(directory,"Root.cs"),"public class Root { public int Value => Dependency.Value; }",cancellation);
            await File.WriteAllTextAsync(Path.Combine(directory,"Dependency","Dependency.csproj"),"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><NuGetAudit>false</NuGetAudit></PropertyGroup></Project>",cancellation);
            await File.WriteAllTextAsync(Path.Combine(directory,"Dependency","Dependency.cs"),"public class Dependency { public static int Value => 42; }",cancellation);
            var build=await new DotNetProjectBuilder().BuildAsync(new(project,true),cancellation);Assert.True(build.Success,build.Log);
            WorkspaceBootstrap.Initialize();using var workspace=WorkspaceBootstrap.Create();
            var projects=await workspace.OpenAsync(project,true,cancellation);Assert.Equal(2,projects.Count);
            var diagnostics=await workspace.GetDiagnosticsAsync(cancellation);Assert.DoesNotContain(diagnostics,d=>d.Severity==DiagnosticSeverity.Error);
        }
        finally { Directory.Delete(directory,true); }
    }
}
