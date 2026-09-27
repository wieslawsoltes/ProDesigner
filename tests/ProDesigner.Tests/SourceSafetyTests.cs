using ProDesigner.Core;
using ProDesigner.Design;
using ProDesigner.Xaml;
using Xunit;

namespace ProDesigner.Tests;
public class SourceSafetyTests
{
    private const string Ns = "xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"";
    [Theory]
    [InlineData("{Binding ElementName=Card}", "{Binding ElementName=Renamed}")]
    [InlineData("{Binding ElementName='Card'}", "{Binding ElementName='Renamed'}")]
    [InlineData("{Binding Path=#Card.Width}", "{Binding Path=#Renamed.Width}")]
    [InlineData("{x:Reference Card}", "{x:Reference Renamed}")]
    [InlineData("{Binding FallbackValue='ElementName=Card', ElementName=Card}", "{Binding FallbackValue='ElementName=Card', ElementName=Renamed}")]
    [InlineData("{Binding Source={x:Reference Card}, Path=Width}", "{Binding Source={x:Reference Renamed}, Path=Width}")]
    [InlineData("{Custom ElementName=Card}", "{Custom ElementName=Card}")]
    [InlineData("{}Card", "{}Card")]
    [InlineData("Card", "Card")]
    public void ReferenceEditingUnderstandsTokensAndLiteralValues(string before, string after) => Assert.Equal(after, MarkupReferences.Rename(before,"Card","Renamed"));
    [Fact] public void RenamePreservesCommentsAndTemplateNamescopes()
    {
        var source=$"<Grid {Ns}><!-- Card remains --><Button x:Name='Card'/><TextBlock Text='{{Binding ElementName=Card}}'/><ControlTemplate><Button x:Name='Card'/><TextBlock Text='{{Binding ElementName=Card}}'/></ControlTemplate></Grid>";
        var tree=XamlSyntaxTree.Parse(source);var result=EditApplication.Apply(source,XamlNames.Rename(tree,tree.Root.Children[0],"Renamed"));
        Assert.Contains("<!-- Card remains -->",result);Assert.Contains("x:Name='Renamed'",result);Assert.Contains("<Button x:Name='Card'/>",result);
        Assert.Equal(1,result.Split("ElementName=Renamed").Length-1);Assert.Equal(1,result.Split("ElementName=Card").Length-1);
    }
    [Fact] public void DuplicateRemapsReferencesAcrossSelectedRoots()
    {
        var tree=XamlSyntaxTree.Parse($"<Canvas {Ns}><Button x:Name='A'/><TextBlock x:Name='B' Text='{{Binding ElementName=A}}'/></Canvas>");
        var updated=XamlSyntaxTree.Parse(EditApplication.Apply(tree.Source,XamlNames.Duplicate(tree,tree.Root.Children)));
        Assert.Contains(updated.Elements,n=>n.Get("x:Name")=="BCopy"&&n.Get("Text")=="{Binding ElementName=ACopy}");
    }
    [Fact] public void RenameRejectsScopeCollisions()
    {
        var tree=XamlSyntaxTree.Parse($"<Grid {Ns}><Button x:Name='A'/><Button x:Name='B'/></Grid>");
        Assert.Throws<InvalidOperationException>(()=>XamlNames.Rename(tree,tree.Root.Children[0],"B"));
    }
    [Fact] public void LanguageNamespaceAliasIsNotHardCoded()
    {
        var tree=XamlSyntaxTree.Parse("<Grid xmlns:q='http://schemas.microsoft.com/winfx/2006/xaml'><Button q:Name='Save'/></Grid>");
        Assert.Equal("Save",tree.Root.Children[0].DisplayName);Assert.Contains("q:Name='Commit'",EditApplication.Apply(tree.Source,XamlNames.Rename(tree,tree.Root.Children[0],"Commit")));
    }
    [Fact] public void ReparentPreservesInheritedNamespaceDeclarations()
    {
        var tree=XamlSyntaxTree.Parse($"<Grid {Ns}><Canvas xmlns:local='using:Demo'><local:Card/></Canvas><Canvas/></Grid>");
        var updated=XamlSyntaxTree.Parse(EditApplication.Apply(tree.Source,XamlEdits.Reparent(tree,tree.Elements[2],tree.Root.Children[1])));
        Assert.Equal("using:Demo",XamlNames.Namespace(updated.Root.Children[1].Children[0],"local"));
    }
    [Fact] public void ReparentRefusesImplicitTemplateScopeMigration()
    {
        var tree=XamlSyntaxTree.Parse($"<Grid {Ns}><ControlTemplate><Canvas><Button/></Canvas></ControlTemplate><Canvas/></Grid>");
        Assert.Throws<InvalidOperationException>(()=>XamlEdits.Reparent(tree,tree.Elements[3],tree.Root.Children[1]));
    }
    [Fact] public void SelectionSurvivesInsertionBeforeItsStructuralPath()
    {
        var session=new DesignerSession("<Canvas><Button/><TextBlock/></Canvas>");session.Select("0/1");
        session.Apply("Insert sibling",[new(new(session.Tree.Root.OpenSpan.End,0),"<Border/>")]);
        Assert.Equal("TextBlock",session.Primary!.Name);Assert.Equal("0/2",session.Primary.Id);
    }
    [Fact] public void IndexedLookupFindsParentAfterPreviousChildClosed()
    {
        var tree=XamlSyntaxTree.Parse("<Grid><Button/>   </Grid>");Assert.Same(tree.Root,tree.At(16));Assert.Null(tree.At(tree.Source.Length));
    }
}
