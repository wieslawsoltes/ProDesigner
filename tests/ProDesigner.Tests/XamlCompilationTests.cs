using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ProDesigner.Roslyn;
using Xunit;

namespace ProDesigner.Tests;
public class XamlCompilationTests
{
    private static Compilation Compilation()
    {
        var paths=((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Concat([typeof(Avalonia.Controls.Control).Assembly.Location,typeof(Avalonia.AvaloniaObject).Assembly.Location]).Distinct();
        return CSharpCompilation.Create("BindingFixture",[CSharpSyntaxTree.ParseText("namespace Demo; public class Card : Avalonia.Controls.Border { public string Caption {get;set;} = \"\"; } public class View : Avalonia.Controls.UserControl { private void OnSave(object sender, Avalonia.Interactivity.RoutedEventArgs e) {} }")],paths.Select(p=>MetadataReference.CreateFromFile(p)),new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
    [Fact] public void ResolvesCustomProjectPropertiesAndInheritedAvaloniaProperties()
    {
        var result=new XamlCompilationService().Analyze("<local:Card xmlns=\"https://github.com/avaloniaui\" xmlns:local=\"using:Demo\" Caption=\"Hello\" Width=\"100\" Grid.Row=\"1\"/>",Compilation());
        Assert.Empty(result.Diagnostics);Assert.Equal("Demo.Card",result.Elements.Single().Type);Assert.Contains(result.Elements.Single().Members,m=>m.Name=="Caption");Assert.Contains(result.Elements.Single().Members,m=>m.Name=="Width");
    }
    [Fact] public void UnknownPropertyProducesSourceSpannedDiagnostic()
    {
        var source="<Button xmlns=\"https://github.com/avaloniaui\" NotAProperty=\"123\"/>";
        var diagnostic=Assert.Single(new XamlCompilationService().Analyze(source,Compilation()).Diagnostics);
        Assert.Equal("XAML102",diagnostic.Code);Assert.Equal("NotAProperty=\"123\"",source.Substring(diagnostic.Offset,diagnostic.Length));
    }
    [Fact] public void EventsAreCheckedAgainstCodeBehindSymbols()
    {
        var source="<UserControl xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" x:Class=\"Demo.View\"><Button Click=\"OnSave\"/><Button Click=\"MissingHandler\"/></UserControl>";
        var result=new XamlCompilationService().Analyze(source,Compilation());Assert.Contains(result.Diagnostics,d=>d.Code=="XAML104"&&d.Message.Contains("MissingHandler"));Assert.DoesNotContain(result.Diagnostics,d=>d.Message.Contains("OnSave"));
    }
}
