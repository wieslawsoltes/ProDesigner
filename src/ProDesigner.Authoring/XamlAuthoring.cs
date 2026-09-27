using System.Xml;
using ProDesigner.Core;
using ProDesigner.Design;
using ProDesigner.Xaml;

namespace ProDesigner.Authoring;

/// <summary>Source-preserving resource, style, theme and property-element authoring.</summary>
public static class XamlAuthoring
{
    public static IReadOnlyList<TextEdit> SetPropertyObject(XamlSyntaxTree tree, XamlElement owner, string property, string? fragment)
    {
        XmlConvert.VerifyNCName(property);
        var name = owner.Name + "." + property;
        var current = owner.Children.FirstOrDefault(n => n.Name == name);
        var edits = new List<TextEdit>();
        if (owner.Get(property) is not null) edits.Add(XamlEdits.SetAttribute(tree, owner, property, null));
        if (fragment is null)
        {
            if (current is not null) edits.Add(XamlEdits.Delete(tree, current));
        }
        else
        {
            var replacement = $"<{name}>{fragment}</{name}>";
            if (current is not null) edits.Add(new(current.Span, replacement));
            else edits.Add(XamlEdits.AppendChild(tree, owner, replacement));
        }
        return edits;
    }
    public static IReadOnlyList<TextEdit> UpsertResource(XamlSyntaxTree tree, string key, string type, IReadOnlyDictionary<string, string> properties)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("A resource key is required.", nameof(key));
        XmlConvert.VerifyName(type);
        foreach (var name in properties.Keys) XmlConvert.VerifyName(name);
        var fragment = $"<{type} xmlns:x=\"{XamlNames.LanguageNamespace}\" x:Key=\"{XamlEdits.Escape(key)}\"" + Attributes(properties) + " />";
        return UpsertKeyed(tree, key, fragment);
    }
    public static IReadOnlyList<TextEdit> UpsertTheme(XamlSyntaxTree tree, string key, string targetType, IReadOnlyDictionary<string, string> setters)
    {
        XmlConvert.VerifyName(targetType);
        var fragment = $"<ControlTheme xmlns:x=\"{XamlNames.LanguageNamespace}\" x:Key=\"{XamlEdits.Escape(key)}\" TargetType=\"{targetType}\">" + Setters(setters) + "</ControlTheme>";
        return UpsertKeyed(tree, key, fragment);
    }
    public static IReadOnlyList<TextEdit> UpsertKeyed(XamlSyntaxTree tree, string key, string fragment)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("A resource key is required.", nameof(key));
        var property = tree.Root.Children.FirstOrDefault(n => n.LocalName.EndsWith(".Resources", StringComparison.Ordinal));
        var container = property?.Children.FirstOrDefault(n => n.LocalName == "ResourceDictionary") ?? property;
        var current = container?.Children.FirstOrDefault(n => Key(n) == key);
        var imported = FragmentImporter.Prepare(tree, container ?? tree.Root, fragment);
        if (current is not null) return imported.RootEdits.Concat([new TextEdit(current.Span, imported.Source)]).ToArray();
        var content = container is null ? $"<{tree.Root.Name}.Resources>\n{imported.Source}\n</{tree.Root.Name}.Resources>" : imported.Source;
        return imported.RootEdits.Concat([XamlEdits.AppendChild(tree, container ?? tree.Root, content, imported.Namespaces)]).ToArray();
    }
    public static string? Key(XamlElement node) => node.Attributes.FirstOrDefault(a => a.Name.Contains(':') && a.Name.EndsWith(":Key", StringComparison.Ordinal) && XamlNames.Namespace(node, a.Name[..a.Name.IndexOf(':')]) == XamlNames.LanguageNamespace)?.Value;
    public static IReadOnlyList<TextEdit> UpsertStyle(XamlSyntaxTree tree, string selector, IReadOnlyDictionary<string, string> setters)
    {
        if (string.IsNullOrWhiteSpace(selector)) throw new ArgumentException("A style selector is required.", nameof(selector));
        var property = tree.Root.Children.FirstOrDefault(n => n.LocalName.EndsWith(".Styles", StringComparison.Ordinal));
        var current = property?.Children.FirstOrDefault(n => n.LocalName == "Style" && n.Get("Selector") == selector);
        if (current is not null)
        {
            // Preserve nested styles, animations and setters not mentioned by the caller.
            var edits = new List<TextEdit>(); var missing = new Dictionary<string, string>();
            foreach (var pair in setters)
            {
                var setter = current.Children.FirstOrDefault(n => n.LocalName == "Setter" && n.Get("Property") == pair.Key);
                if (setter is null) missing.Add(pair.Key, pair.Value);
                else edits.Add(XamlEdits.SetAttribute(tree, setter, "Value", pair.Value));
            }
            if (missing.Count > 0) edits.Add(XamlEdits.AppendChild(tree, current, Setters(missing)));
            return edits;
        }
        var fragment = $"<Style Selector=\"{XamlEdits.Escape(selector)}\">\n{Setters(setters)}\n</Style>";
        return [XamlEdits.AppendChild(tree, property ?? tree.Root, property is null ? $"<{tree.Root.Name}.Styles>\n{fragment}\n</{tree.Root.Name}.Styles>" : fragment)];
    }
    public static string Attributes(IReadOnlyDictionary<string, string> values) => string.Concat(values.Select(p => $" {p.Key}=\"{XamlEdits.Escape(p.Value)}\""));
    public static string Setters(IReadOnlyDictionary<string, string> values) => string.Join("\n", values.Select(p => $"<Setter Property=\"{XamlEdits.Escape(p.Key)}\" Value=\"{XamlEdits.Escape(p.Value)}\" />"));
}
