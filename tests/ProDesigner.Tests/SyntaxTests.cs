using System.Xml;
using ProDesigner.Core;
using ProDesigner.Design;
using ProDesigner.Xaml;
using ProDesigner.XamlX;
using Xunit;

namespace ProDesigner.Tests;
public class SyntaxTests
{
    private const string Source = "<UserControl xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">\r\n  <!-- keep me -->\r\n  <Canvas><Button x:Name='Save' Content='A &amp; B' Width='80' /></Canvas>\r\n</UserControl>";
    [Fact] public void ParseRetainsEveryCharacter()
    {
        var tree = XamlSyntaxTree.Parse(Source); Assert.Equal(Source, tree.Source); Assert.Equal("\r\n", tree.NewLine);
        Assert.Equal(3, tree.Elements.Count); Assert.Equal("A & B", tree.Elements[2].Get("Content"));
    }
    [Fact] public void PropertyEditPreservesCommentsQuotesAndTrivia()
    {
        var tree = XamlSyntaxTree.Parse(Source); var button = tree.Elements[2];
        var result = EditApplication.Apply(Source, [XamlEdits.SetAttribute(tree, button, "Width", "120")]);
        Assert.Equal(Source.Replace("Width='80'", "Width='120'"), result);
    }
    [Theory]
    [InlineData("<Button Content=\"x > y\" />")]
    [InlineData("<Button Content='x &quot; y' />")]
    [InlineData("<Button><Button.Content><![CDATA[x<y]]></Button.Content></Button>")]
    [InlineData("<?xml version=\"1.0\"?><Button><!-- <broken> --></Button>")]
    public void LexerHandlesXmlLexicalContexts(string source) => Assert.Equal(source, XamlSyntaxTree.Parse(source).Source);
    [Fact] public void RejectsDtdAndExternalEntities() => Assert.Throws<XmlException>(() => XamlSyntaxTree.Parse("<!DOCTYPE root [<!ENTITY x SYSTEM 'file:///etc/passwd'>]><root>&x;</root>"));
    [Fact] public void RejectsExcessiveDepth() => Assert.Throws<XmlException>(() => XamlSyntaxTree.Parse(string.Concat(Enumerable.Repeat("<a>", 258)) + string.Concat(Enumerable.Repeat("</a>", 258))));
    [Fact] public void RejectsMismatchedElements() => Assert.Throws<XmlException>(() => XamlSyntaxTree.Parse("<Grid><Button /></Border>"));
    [Fact] public void SourceSpansSelectInnermostNode()
    {
        var tree = XamlSyntaxTree.Parse(Source); Assert.Equal("Button", tree.At(Source.IndexOf("Content", StringComparison.Ordinal))!.Name);
    }
    [Fact] public void AddsAndRemovesAttachedProperties()
    {
        var tree = XamlSyntaxTree.Parse(Source);
        var updated = EditApplication.Apply(Source, [XamlEdits.SetAttribute(tree, tree.Elements[2], "Canvas.Left", "48")]);
        tree = XamlSyntaxTree.Parse(updated); Assert.Equal("48", tree.Elements[2].Get("Canvas.Left"));
        updated = EditApplication.Apply(updated, [XamlEdits.SetAttribute(tree, tree.Elements[2], "Canvas.Left", null)]);
        Assert.Null(XamlSyntaxTree.Parse(updated).Elements[2].Get("Canvas.Left"));
    }
    [Fact] public void EscapesValuesWithoutChangingBindings()
    {
        var tree = XamlSyntaxTree.Parse(Source); var value = "{Binding Path=Title, FallbackValue='Hello & welcome'}";
        var source = EditApplication.Apply(Source, [XamlEdits.SetAttribute(tree, tree.Elements[2], "Content", value)]);
        Assert.Equal(value, XamlSyntaxTree.Parse(source).Elements[2].Get("Content"));
    }
    [Fact] public void EscapedWhitespaceRoundTrips()
    {
        var tree = XamlSyntaxTree.Parse("<TextBlock Text=\"a&#10;b&#9;c\"/>"); Assert.Equal("a\nb\tc", tree.Root.Get("Text"));
    }
    [Fact] public void AppendExpandsSelfClosingContainer()
    {
        var tree = XamlSyntaxTree.Parse("<Grid />"); var source = EditApplication.Apply(tree.Source, [XamlEdits.AppendChild(tree, tree.Root, "<Button Content=\"Go\" />")]);
        Assert.Equal("Button", XamlSyntaxTree.Parse(source).Root.Children.Single().Name); Assert.EndsWith("</Grid>", source);
    }
    [Fact] public void DuplicateCreatesDistinctNames()
    {
        var tree = XamlSyntaxTree.Parse(Source); var source = EditApplication.Apply(Source, [XamlEdits.Duplicate(tree, tree.Elements[2])]);
        Assert.Contains("SaveCopy", source); Assert.Equal(4, XamlSyntaxTree.Parse(source).Elements.Count);
    }
    [Fact] public void RenameTypeChangesBothTags()
    {
        var tree = XamlSyntaxTree.Parse("<Grid><StackPanel></StackPanel></Grid>"); var source = EditApplication.Apply(tree.Source, XamlEdits.RenameType(tree, tree.Elements[1], "Canvas"));
        Assert.Equal("<Grid><Canvas></Canvas></Grid>", source);
    }
    [Fact] public void ReparentRejectsCycles()
    {
        var tree = XamlSyntaxTree.Parse("<Grid><Canvas><Button /></Canvas></Grid>"); Assert.Throws<InvalidOperationException>(() => XamlEdits.Reparent(tree, tree.Elements[1], tree.Elements[2]));
    }
    [Fact] public void ReparentIsOneWellFormedTransaction()
    {
        var tree = XamlSyntaxTree.Parse("<Grid><Canvas><Button /></Canvas><StackPanel /></Grid>");
        var source = EditApplication.Apply(tree.Source, XamlEdits.Reparent(tree, tree.Elements[2], tree.Elements[3]));
        Assert.Single(XamlSyntaxTree.Parse(source).Root.Children[1].Children);
    }
    [Fact] public void RootCannotBeDeleted() { var tree = XamlSyntaxTree.Parse("<Grid/>"); Assert.Throws<InvalidOperationException>(() => XamlEdits.Delete(tree, tree.Root)); }
    [Fact] public void OverlappingEditsAreRejected() => Assert.Throws<InvalidOperationException>(() => EditApplication.Apply("abcd", [new(new(0, 3), "x"), new(new(2, 1), "y")]));
    [Fact] public void ExpectedTextProtectsAgainstStaleSpans() => Assert.Throws<InvalidOperationException>(() => EditApplication.Apply("abcd", [new(new(0, 1), "x", "z")]));
    [Fact] public void MultipleInsertionsAtOneOffsetAreDeterministic() => Assert.Equal("aXYb", EditApplication.Apply("ab", [new(new(1, 0), "X"), new(new(1, 0), "Y")]));
    [Fact] public void UndoRedoRoundTripsTheExactSource()
    {
        var session = new DesignerSession(Source); session.Select("0/0/0"); session.SetProperty("Width", "140");
        Assert.True(session.IsDirty); session.Undo(); Assert.Equal(Source, session.Source); Assert.False(session.IsDirty);
        session.Redo(); Assert.Equal("140", session.Primary!.Get("Width"));
    }
    [Fact] public void InvalidTypingKeepsTheLastValidTree()
    {
        var session = new DesignerSession(Source); var tree = session.Tree; session.SetSource("<Grid>");
        Assert.False(session.IsValid); Assert.Same(tree, session.Tree); Assert.NotEmpty(session.Diagnostics);
        Assert.Throws<InvalidOperationException>(() => session.Apply("edit", [])); session.Undo(); Assert.True(session.IsValid);
    }
    [Fact] public void VisualTransactionIsValidatedBeforePublication()
    {
        var session = new DesignerSession(Source); Assert.Throws<XmlException>(() => session.Apply("break", [new(new(0, Source.Length), "<a>")]));
        Assert.Equal(Source, session.Source); Assert.False(session.CanUndo);
    }
    [Fact] public void RevisionGuardRejectsOldGestures()
    {
        var session = new DesignerSession(Source); var version = session.Version; session.SetSource(Source + "\n");
        Assert.Throws<InvalidOperationException>(() => session.Apply("stale", [], version));
    }
    [Fact] public void XamlXActuallyParsesMarkupExtensions()
    {
        var service = new XamlXSemanticService(); Assert.Empty(service.Validate("<TextBlock xmlns=\"https://github.com/avaloniaui\" Text=\"{Binding Title}\"/>"));
        Assert.NotEmpty(service.Validate("<TextBlock xmlns=\"https://github.com/avaloniaui\" Text=\"{Binding\"/>"));
    }
}
