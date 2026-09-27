using System.Text.RegularExpressions;
using System.Xml;
using ProDesigner.Core;

namespace ProDesigner.Xaml;

/// <summary>Namespace and namescope operations that never serialize the user's document.</summary>
public static partial class XamlNames
{
    public const string LanguageNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";
    public const string AvaloniaNamespace = "https://github.com/avaloniaui";
    public static string? Namespace(XamlElement element, string prefix = "")
    {
        for (var current = element; current is not null; current = current.Parent)
            if (current.Get(prefix.Length == 0 ? "xmlns" : "xmlns:" + prefix) is { } value) return value;
        return null;
    }
    public static string? Name(XamlElement node) => node.Attributes.FirstOrDefault(a => IsName(node, a.Name))?.Value;
    public static bool IsName(XamlElement node, string attribute)
    {
        if (attribute == "Name") return true;
        var colon = attribute.IndexOf(':');
        return colon > 0 && attribute[(colon + 1)..] == "Name" && Namespace(node, attribute[..colon]) == LanguageNamespace;
    }
    public static XamlElement Scope(XamlElement node)
    {
        // The template object belongs to its parent's scope; its instantiated content starts a new scope.
        for (var parent = node.Parent; parent is not null; parent = parent.Parent)
            if (parent.LocalName is "ControlTemplate" or "DataTemplate" or "TreeDataTemplate" or "ItemsPanelTemplate") return parent;
        while (node.Parent is not null) node = node.Parent;
        return node;
    }
    public static string ScopeIdentity(XamlElement node)
    {
        var scope = Scope(node);
        return scope.Parent is null ? "/" : ScopeIdentity(scope) + "/" + scope.Name + ":" + (scope.Get("x:Key") ?? scope.Parent?.DisplayName ?? scope.Id);
    }
    public static IReadOnlyDictionary<string, string> Namespaces(XamlElement node)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var current = node; current is not null; current = current.Parent)
            foreach (var attribute in current.Attributes.Where(a => a.Name == "xmlns" || a.Name.StartsWith("xmlns:", StringComparison.Ordinal)))
                result.TryAdd(attribute.Name, attribute.Value);
        return result;
    }
    public static string ExportFragment(XamlSyntaxTree tree, XamlElement node)
    {
        var fragment = tree.Source.Substring(node.Span.Start, node.Span.Length);
        var declarations = string.Concat(Namespaces(node).Where(p => node.Get(p.Key) is null).Select(p => $" {p.Key}=\"{XamlEdits.Escape(p.Value)}\""));
        return fragment.Insert(node.NameSpan.End - node.Span.Start, declarations);
    }
    public static IReadOnlyList<TextEdit> Rename(XamlSyntaxTree tree, XamlElement node, string newName)
    {
        XmlConvert.VerifyNCName(newName);
        var oldName = Name(node) ?? throw new InvalidOperationException("The selected control has no name.");
        var scope = Scope(node);
        if (tree.Elements.Any(n => n != node && Scope(n) == scope && Name(n) == newName)) throw new InvalidOperationException($"'{newName}' already exists in this namescope.");
        var edits = new List<TextEdit>();
        var nameAttribute = node.Attributes.First(a => IsName(node, a.Name));
        edits.Add(XamlEdits.SetAttribute(tree, node, nameAttribute.Name, newName));
        foreach (var element in tree.Elements.Where(n => Scope(n) == scope))
            foreach (var attribute in element.Attributes.Where(a => !IsName(element, a.Name)))
            {
                var value = RewriteReference(attribute.Name, attribute.Value, oldName, newName);
                if (value != attribute.Value) edits.Add(XamlEdits.SetAttribute(tree, element, attribute.Name, value));
            }
        return edits;
    }
    public static string RewriteReference(string property, string value, string oldName, string newName)
    {
        if (property is "TargetName" or "SourceName") return value == oldName ? newName : value;
        if (property == "Selector") return Regex.Replace(value, @"(?<=#)[\p{L}_][\p{L}\p{N}_]*", m => m.Value == oldName ? newName : m.Value);
        return MarkupReferences.Rename(value, oldName, newName);
    }

    public static IReadOnlyList<TextEdit> Duplicate(XamlSyntaxTree tree, IEnumerable<XamlElement> roots)
    {
        var selected = roots.ToArray();
        if (selected.Any(n => n.Parent is null)) throw new InvalidOperationException("The root cannot be duplicated.");
        if (selected.Any(n => selected.Any(p => p != n && p.DescendantsAndSelf().Contains(n)))) throw new InvalidOperationException("Select disjoint subtree roots.");
        var included = selected.SelectMany(n => n.DescendantsAndSelf()).ToHashSet();
        var taken = tree.Elements.Select(n => (Scope(n), Name(n))).Where(p => p.Item2 is not null).ToHashSet();
        var renames = new Dictionary<(XamlElement Scope, string Name), string>();
        foreach (var node in included)
        {
            var name = Name(node); if (name is null) continue;
            var scope = Scope(node); var copy = name + "Copy"; var suffix = 2;
            while (!taken.Add((scope, copy))) copy = name + "Copy" + suffix++;
            if (!renames.TryAdd((scope, name), copy)) throw new InvalidOperationException($"Duplicate name '{name}' exists in the source namescope.");
        }
        var result = new List<TextEdit>();
        foreach (var root in selected)
        {
            var edits = new List<TextEdit>();
            foreach (var node in root.DescendantsAndSelf())
                foreach (var attribute in node.Attributes)
                {
                    var value = attribute.Value;
                    foreach (var pair in renames.Where(p => p.Key.Scope == Scope(node)))
                        value = IsName(node, attribute.Name) && value == pair.Key.Name ? pair.Value : RewriteReference(attribute.Name, value, pair.Key.Name, pair.Value);
                    if (value != attribute.Value) edits.Add(new(new(attribute.ValueSpan.Start - root.Span.Start, attribute.ValueSpan.Length), XamlEdits.Escape(value, attribute.Quote)));
                }
            var fragment = EditApplication.Apply(tree.Source.Substring(root.Span.Start, root.Span.Length), edits);
            result.Add(new(new(root.Span.End, 0), tree.NewLine + XamlEdits.Indent(tree.Source, root.Span.Start) + fragment));
        }
        return result;
    }
    public static IReadOnlyList<TextEdit> Reparent(XamlSyntaxTree tree, XamlElement element, XamlElement target)
    {
        if (element.Parent is null || element.DescendantsAndSelf().Contains(target)) throw new InvalidOperationException("Reparenting would create a cycle or move the root.");
        var destinationScope = target.LocalName is "ControlTemplate" or "DataTemplate" or "TreeDataTemplate" or "ItemsPanelTemplate" ? target : Scope(target);
        if (Scope(element) != destinationScope)
            throw new InvalidOperationException("Moving across template namescopes requires explicit reference migration. The source was not changed.");
        return new[] { XamlEdits.Delete(tree, element) }.Concat(FragmentImporter.Append(tree, target, ExportFragment(tree, element))).ToArray();
    }
}
