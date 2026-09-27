using System.Collections;
using System.Xml;
using ProDesigner.Core;
using ProDesigner.Design;
using ProDesigner.Xaml;
using Xunit;

namespace ProDesigner.Tests;

public class IncrementalSyntaxTests
{
    private const string Source = "<Grid xmlns='https://github.com/avaloniaui' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>\r\n<!-- untouched -->\r\n<Border Width='120' Height=''><TextBlock x:Name='Title' Text='Hello &amp; welcome'/></Border><Button Content=\"Next\"/></Grid>";
    private static void Equivalent(XamlSyntaxTree actual)
    {
        var expected = XamlSyntaxTree.Parse(actual.Source);
        Assert.Equal(expected.Elements.Count, actual.Elements.Count);
        for (var i = 0; i < expected.Elements.Count; i++)
        {
            var a = expected.Elements[i]; var b = actual.Elements[i];
            Assert.Equal(a.Id, b.Id); Assert.Equal(a.Name, b.Name); Assert.Equal(a.Span, b.Span);
            Assert.Equal(a.NameSpan, b.NameSpan); Assert.Equal(a.OpenSpan, b.OpenSpan); Assert.Equal(a.CloseSpan, b.CloseSpan);
            Assert.Equal(a.SelfClosing, b.SelfClosing); Assert.Equal(a.Attributes, b.Attributes);
            Assert.Equal(a.Parent?.Id, b.Parent?.Id);
        }
    }
    [Fact] public void MultipleLengthChangingEditsMatchAFullParseWithoutMutatingTheOldTree()
    {
        var old = XamlSyntaxTree.Parse(Source); var oldAttributes = old.Elements.SelectMany(n => n.Attributes).ToArray();
        var update = old.ApplyEdits([XamlEdits.SetAttribute(old, old.Elements[1], "Width", "12345"),
            XamlEdits.SetAttribute(old, old.Elements[1], "Height", "81"), XamlEdits.SetAttribute(old, old.Elements[2], "Text", "A\r\nB\tC & <> '")]);
        Assert.Equal(SyntaxUpdateKind.AttributeValues, update.Kind); Equivalent(update.Tree);
        Assert.Equal(Source, old.Source); Assert.Equal(oldAttributes, old.Elements.SelectMany(n => n.Attributes));
        Assert.Equal(old.Elements.Select(n => n.Identity), update.Tree.Elements.Select(n => n.Identity));
    }
    [Theory] [InlineData("")] [InlineData("X")] [InlineData("A&amp;B")] [InlineData("A&#x1F680;B")] [InlineData("&#13;&#10;&#9;")]
    public void EmptyAndEscapedValuesRemainSpanEquivalent(string raw)
    {
        var old = XamlSyntaxTree.Parse(Source); var attribute = old.Elements[1].Attributes.Single(a => a.Name == "Height");
        var update = old.ApplyEdits([new(attribute.ValueSpan, raw)]); Equivalent(update.Tree);
    }
    [Fact] public void TypingInsideAnAttributeUsesTheFastPathAndPreservesIdentity()
    {
        var session = new DesignerSession(Source); session.Select("0/0/0"); var identity = session.Primary!.Identity;
        session.SetSource(Source.Replace("Hello", "Hello there"), mergeTyping: true);
        Assert.Equal(SyntaxUpdateKind.AttributeValues, session.LastSyntaxUpdate); Assert.Equal(identity, session.Primary!.Identity);
        Equivalent(session.Tree); session.Undo(); Assert.Equal(Source, session.Source); session.Redo(); Equivalent(session.Tree);
    }
    [Theory] [InlineData("xmlns")] [InlineData("xml:space")]
    public void NamespaceAndXmlDirectiveEditsUseTheFullValidationPath(string name)
    {
        var old = XamlSyntaxTree.Parse("<Grid xmlns='https://github.com/avaloniaui' xml:space='default'/>");
        var update = old.ApplyEdits([XamlEdits.SetAttribute(old, old.Root, name, name == "xmlns" ? "using:Other" : "preserve")]);
        Assert.Equal(SyntaxUpdateKind.FullParse, update.Kind); Equivalent(update.Tree);
    }
    [Theory] [InlineData("bad' Other='injected")] [InlineData("bad'/><Other Value='")]
    public void QuoteBreakingValuesCannotBypassStructuralParsing(string raw)
    {
        var old = XamlSyntaxTree.Parse("<Grid Width='1'/>"); var edit = new TextEdit(old.Root.Attributes[0].ValueSpan, raw);
        try { var update = old.ApplyEdits([edit]); Assert.Equal(SyntaxUpdateKind.FullParse, update.Kind); Equivalent(update.Tree); }
        catch (XmlException) { Assert.Throws<XmlException>(() => XamlSyntaxTree.Parse(EditApplication.Apply(old.Source, [edit]))); }
    }
    [Fact] public void PublishedSyntaxCollectionsCannotBeMutated()
    {
        var tree = XamlSyntaxTree.Parse(Source);
        Assert.Throws<NotSupportedException>(() => ((IList)tree.Elements).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList)tree.Root.Children).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList)tree.Root.Attributes).Clear());
    }
    [Fact] public void InsertingAndMovingNamedNodesKeepsTheSelectedIdentity()
    {
        var session = new DesignerSession("<Grid xmlns='https://github.com/avaloniaui'><Canvas><Button Name='Save'/></Canvas><StackPanel/></Grid>");
        session.Select("0/0/0"); var identity = session.Primary!.Identity;
        session.Apply("Insert before", [new(new(session.Primary.Span.Start, 0), "<TextBlock/>")]);
        Assert.Equal(identity, session.Primary!.Identity); Assert.Equal("0/0/1", session.Primary.Id);
        session.Apply("Reparent", XamlNames.Reparent(session.Tree, session.Primary, session.Tree.Root.Children[1]));
        Assert.Equal(identity, session.Primary!.Identity); Assert.Equal("StackPanel", session.Primary.Parent!.Name);
        Equivalent(session.Tree);
    }
    [Fact] public void RandomScalarEditsRemainEquivalentAcrossHundredsOfRevisions()
    {
        var random = new Random(517); var tree = XamlSyntaxTree.Parse(Source);
        for (var i = 0; i < 250; i++)
        {
            var node = tree.Elements[random.Next(1, tree.Elements.Count)]; var attribute = node.Attributes[random.Next(node.Attributes.Count)];
            var raw = XamlEdits.Escape(new string((char)('a' + i % 26), random.Next(0, 70)) + (i % 3 == 0 ? "&\n'\t🚀" : ""), attribute.Quote);
            tree = tree.ApplyEdits([new(attribute.ValueSpan, raw)]).Tree; Equivalent(tree);
        }
    }
}
