using ProDesigner.Core;
using ProDesigner.DesignSystems;
using ProDesigner.Xaml;
using Xunit;

namespace ProDesigner.Tests;

public class ComponentTests
{
    private const string Template = "<Border xmlns='https://github.com/avaloniaui' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' x:Name='Root' Width='120'><!-- keep this --><TextBlock x:Name='Title' Text='Hello' /></Border>";
    private static ComponentDefinition Definition() => new("card", "Card", 1, Template,
        [new("Featured", [new("$root", "Width", "200"), new("Title", "Text", "Featured")])]);
    [Fact] public void VariantAndInstanceOverridesHaveDeterministicPrecedence()
    {
        var result = ComponentEngine.Render(Definition(), "Card1", "Featured", [new("Title", "Text", "Custom")]);
        var tree = XamlSyntaxTree.Parse(result);
        Assert.Equal("200", tree.Root.Get("Width")); Assert.Equal("Custom", tree.Root.Children.Single().Get("Text"));
        Assert.Equal("Card1", XamlNames.Name(tree.Root)); Assert.Contains("<!-- keep this -->", result); Assert.Contains("Width='200'", result);
    }
    [Fact] public void InstancesDoNotShareNames()
    {
        var first = XamlSyntaxTree.Parse(ComponentEngine.Render(Definition(), "Card1"));
        var second = XamlSyntaxTree.Parse(ComponentEngine.Render(Definition(), "Card2"));
        Assert.Empty(first.Elements.Select(XamlNames.Name).Intersect(second.Elements.Select(XamlNames.Name)));
    }
    [Fact] public void BindingNamesAreRemappedWithoutChangingLiteralText()
    {
        var definition = Definition() with { Xaml = "<Grid xmlns='https://github.com/avaloniaui' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'><TextBox x:Name='Input' Text='Input'/><TextBlock Text='{Binding Text, ElementName=Input}'/></Grid>", Variants = [] };
        var tree = XamlSyntaxTree.Parse(ComponentEngine.Render(definition, "Form1"));
        var name = XamlNames.Name(tree.Root.Children[0]); Assert.Equal("Input", tree.Root.Children[0].Get("Text"));
        Assert.Contains("ElementName=" + name, tree.Root.Children[1].Get("Text"));
    }
    [Fact] public void ComponentUpdateHasAnExpectedSourcePrecondition()
    {
        var definition = Definition(); var instance = ComponentEngine.CreateInstance(definition, "view", "Card1");
        var document = XamlSyntaxTree.Parse("<Grid xmlns='https://github.com/avaloniaui'>" + instance.Baseline + "</Grid>");
        var revised = ComponentEngine.Revise(definition, Template.Replace("Hello", "Updated"));
        var change = ComponentEngine.PrepareUpdate(revised, instance, document);
        var source = EditApplication.Apply(document.Source, [change.Edit]); Assert.Contains("Updated", source); Assert.Equal(2, change.UpdatedInstance.AppliedRevision);
    }
    [Fact] public void HandEditsAreNotSilentlyOverwritten()
    {
        var definition = Definition(); var instance = ComponentEngine.CreateInstance(definition, "view", "Card1");
        var document = XamlSyntaxTree.Parse("<Grid xmlns='https://github.com/avaloniaui'>" + instance.Baseline.Replace("Hello", "Hand edit") + "</Grid>");
        Assert.Throws<ComponentConflictException>(() => ComponentEngine.PrepareUpdate(definition, instance, document));
    }
    [Fact] public void MissingVariantsAndInvalidTargetsAreRejected()
    {
        Assert.Throws<InvalidOperationException>(() => ComponentEngine.Render(Definition(), "Card1", "Missing"));
        Assert.Throws<InvalidOperationException>(() => ComponentEngine.Render(Definition(), "Card1", overrides: [new("Missing", "Text", "X")]));
    }
    [Theory] [InlineData("Name")] [InlineData("x:Name")] [InlineData("xmlns")]
    public void OverridesCannotChangeIdentity(string property) => Assert.Throws<InvalidOperationException>(() => ComponentEngine.Render(Definition(), "Card1", overrides: [new("$root", property, "Other")]));
    [Fact] public void ConflictingDuplicateNamesAreRejected() => Assert.Throws<InvalidDataException>(() => ComponentEngine.Render(Definition() with { Xaml = Template.Replace("x:Name='Title'", "x:Name='Root'") }, "Card1"));
    [Fact] public void CapturedFragmentsAreNamespaceComplete()
    {
        var tree = XamlSyntaxTree.Parse("<Grid xmlns='https://github.com/avaloniaui' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'><Button x:Name='Save'/></Grid>");
        var component = ComponentEngine.Capture("Save", tree, tree.Root.Children.Single());
        Assert.Equal("Save1", XamlNames.Name(XamlSyntaxTree.Parse(ComponentEngine.Render(component, "Save1")).Root));
    }
}
