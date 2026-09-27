using ProDesigner.Core;
using ProDesigner.Workspaces;
using Xunit;

namespace ProDesigner.Tests;
public class BuildServiceTests
{
    [Fact]
    public async Task TrustedSdkBuildResolvesTheActualAssemblyPath()
    {
        var directory=Path.Combine(Path.GetTempPath(),"prodesigner-build-"+Guid.NewGuid());Directory.CreateDirectory(directory);
        try
        {
            var path=Path.Combine(directory,"Fixture.csproj");
            await File.WriteAllTextAsync(path,"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><NuGetAudit>false</NuGetAudit></PropertyGroup></Project>",TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(Path.Combine(directory,"NuGet.Config"),"<configuration><packageSources><clear/></packageSources></configuration>",TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(Path.Combine(directory,"Sample.cs"),"public class Sample { public int Value => 42; }",TestContext.Current.CancellationToken);
            var result=await new DotNetProjectBuilder().BuildAsync(new(path,true),TestContext.Current.CancellationToken);
            Assert.True(result.Success,result.Log);Assert.True(File.Exists(result.AssemblyPath));Assert.EndsWith("Fixture.dll",result.AssemblyPath);
        }
        finally { Directory.Delete(directory,true); }
    }
}
