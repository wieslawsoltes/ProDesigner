using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using ProDesigner.Authoring;
using ProDesigner.Core;
using ProDesigner.Design;
using ProDesigner.Preview;
using ProDesigner.Runtime;
using ProDesigner.Xaml;
using Xunit;

namespace ProDesigner.Tests;
public class AuthoringTests
{
    [Fact] public void UpdatingStyleRetainsItsAnimationsAndUnrelatedSetters()
    {
        var tree=XamlSyntaxTree.Parse("<UserControl><UserControl.Styles><Style Selector='Button.primary'><Setter Property='Opacity' Value='0.5'/><!-- retain --><Style.Animations/></Style></UserControl.Styles></UserControl>");
        var source=EditApplication.Apply(tree.Source,XamlAuthoring.UpsertStyle(tree,"Button.primary",new Dictionary<string,string>{{"Background","Purple"}}));
        Assert.Contains("Value='0.5'",source);Assert.Contains("<!-- retain -->",source);Assert.Contains("<Style.Animations/>",source);Assert.Contains("Background",source);
    }
    [Fact] public void ResourceUpdateDoesNotReplaceTheWholeDictionary()
    {
        var tree=XamlSyntaxTree.Parse("<UserControl xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'><UserControl.Resources><SolidColorBrush x:Key='Other' Color='Red'/><!-- keep --><SolidColorBrush x:Key='Accent' Color='Blue'/></UserControl.Resources></UserControl>");
        var source=EditApplication.Apply(tree.Source,XamlAuthoring.UpsertResource(tree,"Accent","SolidColorBrush",new Dictionary<string,string>{{"Color","Green"}}));
        Assert.Contains("x:Key='Other' Color='Red'",source);Assert.Contains("<!-- keep -->",source);Assert.Equal(1,XamlSyntaxTree.Parse(source).Elements.Count(n=>XamlAuthoring.Key(n)=="Accent"));
    }
    [AvaloniaFact] public void GradientAuthoringCreatesARealAvaloniaGradient()
    {
        var tree=XamlSyntaxTree.Parse("<Border xmlns='https://github.com/avaloniaui' Background='Blue'/>");
        var source=EditApplication.Apply(tree.Source,XamlAuthoring.SetPropertyObject(tree,tree.Root,"Background",new GradientModel(0,0,1,1,[new(0,"Red"),new(1,"Blue")]).ToXaml()));
        using var runtime=new RuntimePreviewEngine().Load(new(source,Trusted:true));
        Assert.IsType<LinearGradientBrush>(((Border)runtime.Root).Background);
        var safe=new PreviewBuilder().Build(XamlSyntaxTree.Parse(source),PreviewProfile.Defaults[0]);Assert.Empty(safe.Diagnostics);Assert.IsType<LinearGradientBrush>(((Border)safe.Controls["0"]).Background);
    }
    [AvaloniaFact] public void AuthoredControlThemeLoadsInAvalonia()
    {
        var tree=XamlSyntaxTree.Parse("<UserControl xmlns='https://github.com/avaloniaui'><Button Theme='{StaticResource Primary}'/></UserControl>");
        var source=EditApplication.Apply(tree.Source,XamlAuthoring.UpsertTheme(tree,"Primary","Button",new Dictionary<string,string>{{"Background","Purple"},{"CornerRadius","8"}}));
        using var preview=new RuntimePreviewEngine().Load(new(source,Trusted:true));Assert.IsType<UserControl>(preview.Root);
    }
    [Theory] [InlineData("M0,0 L10,20 Z")] [InlineData("M0,0 c10,20 30,40 50,60 q10,20 30,40 z")] [InlineData("m10,20 30,40 50,60")]
    public void VectorGeometryRoundTripsSupportedCommands(string source)
    { var path=VectorPathModel.Parse(source);Assert.Equal(path.ToData(),VectorPathModel.Parse(path.ToData()).ToData()); }
    [Fact] public void VectorPointEditingChangesOnlyItsSegment()
    { var path=VectorPathModel.Parse("M0,0 C10,20 30,40 50,60");path.SetPoint(1,0,new(15,25));Assert.Equal("M 0,0 C 15,25 30,40 50,60",path.ToData()); }
    [Fact] public void UnknownPathCommandsAreNeverSilentlyDiscarded() => Assert.Throws<FormatException>(()=>VectorPathModel.Parse("M0,0 R10,10 20,20"));
    [Fact] public void InvalidGradientIsRejected() => Assert.Throws<ArgumentException>(()=>new GradientModel(0,0,1,1,[new(-1,"Red"),new(1,"Blue")]).ToXaml());
    [AvaloniaFact] public void ReparentedControlRemainsValidRuntimeXaml()
    {
        var tree = XamlSyntaxTree.Parse("<Grid xmlns='https://github.com/avaloniaui' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'><Canvas><Button x:Name='MoveMe'/></Canvas><StackPanel/></Grid>");
        var source = EditApplication.Apply(tree.Source, XamlNames.Reparent(tree, tree.Elements[2], tree.Root.Children[1]));
        using var preview = new RuntimePreviewEngine().Load(new(source, Trusted: true));
        Assert.IsType<Button>(((StackPanel)((Grid)preview.Root).Children[1]).Children.Single());
    }
    [Fact] public void ReparentIntoTemplateRequiresExplicitNamescopeMigration()
    {
        var tree = XamlSyntaxTree.Parse("<Grid><Button/><ControlTemplate/></Grid>");
        Assert.Throws<InvalidOperationException>(() => XamlNames.Reparent(tree, tree.Root.Children[0], tree.Root.Children[1]));
    }
}
