using System.Text.RegularExpressions;
using ProDesigner.Core;

namespace ProDesigner.Xaml;

public sealed record ImportedFragment(string Source, IReadOnlyDictionary<string, string> Namespaces, IReadOnlyList<TextEdit> RootEdits);
/// <summary>Hoists imported namespace declarations to the document root for Avalonia's compiler without serializing XML.</summary>
public static class FragmentImporter
{
    public static ImportedFragment Prepare(XamlSyntaxTree document, XamlElement target, string fragment)
    {
        var declarations = new Dictionary<string, string>(XamlNames.Namespaces(document.Root));
        var wrapper = "<Fragment " + string.Join(" ", XamlNames.Namespaces(target).Select(p => $"{p.Key}=\"{XamlEdits.Escape(p.Value)}\"")) + ">";
        var tree = XamlSyntaxTree.Parse(wrapper + fragment + "</Fragment>");
        var changes = new List<TextEdit>(); var rootEdits = new List<TextEdit>();
        string Map(string prefix, string? uri)
        {
            if (uri is null || prefix == "xml") return prefix;
            var key = prefix.Length == 0 ? "xmlns" : "xmlns:" + prefix;
            if (declarations.GetValueOrDefault(key) == uri) return prefix;
            var existing = declarations.FirstOrDefault(p => p.Value == uri && (prefix.Length == 0 || p.Key != "xmlns"));
            if (existing.Key is not null) return existing.Key == "xmlns" ? "" : existing.Key[6..];
            var name = prefix.Length == 0 ? "ns" : prefix; var index = 2;
            while (declarations.ContainsKey("xmlns:" + name)) name = (prefix.Length == 0 ? "ns" : prefix) + index++;
            key = "xmlns:" + name; declarations[key] = uri;
            rootEdits.Add(XamlEdits.SetAttribute(document, document.Root, key, uri)); return name;
        }
        static string Qualify(string prefix, string local) => prefix.Length == 0 ? local : prefix + ":" + local;
        foreach (var node in tree.Elements.Skip(1))
        {
            var split = node.Name.IndexOf(':'); var prefix = split < 0 ? "" : node.Name[..split];
            var mapped = Map(prefix, XamlNames.Namespace(node, prefix)); var type = Qualify(mapped, node.LocalName);
            if (type != node.Name)
            {
                changes.Add(new(new(node.NameSpan.Start - wrapper.Length, node.NameSpan.Length), type));
                if (!node.SelfClosing) changes.Add(new(new(node.CloseSpan.Start + 2 - wrapper.Length, node.Name.Length), type));
            }
            foreach (var attribute in node.Attributes)
            {
                if (attribute.Name == "xmlns" || attribute.Name.StartsWith("xmlns:", StringComparison.Ordinal))
                { changes.Add(new(new(attribute.Span.Start - wrapper.Length, attribute.Span.Length), "")); continue; }
                var colon = attribute.Name.IndexOf(':');
                if (colon >= 0)
                {
                    var original = attribute.Name[..colon]; var renamed = Map(original, XamlNames.Namespace(node, original));
                    if (original != renamed) changes.Add(new(new(attribute.Span.Start - wrapper.Length, attribute.Name.Length), Qualify(renamed, attribute.Name[(colon + 1)..])));
                }
                // Qualified tokens in values may refer to extension/types. Refuse conflicting aliases rather than rewriting literal fallback text.
                foreach (var pair in XamlNames.Namespaces(node).Where(p => p.Key.StartsWith("xmlns:", StringComparison.Ordinal)))
                {
                    var original = pair.Key[6..]; if (!attribute.Value.Contains(original + ":", StringComparison.Ordinal)) continue;
                    var renamed = Map(original, pair.Value);
                    if (renamed != original) throw new InvalidOperationException($"Namespace alias '{original}' conflicts with the destination and occurs in an attribute value. Rename the alias explicitly before importing.");
                }
            }
        }
        return new(EditApplication.Apply(fragment, changes), declarations, rootEdits);
    }
    public static IReadOnlyList<TextEdit> Append(XamlSyntaxTree document, XamlElement target, string fragment)
    {
        var imported = Prepare(document, target, fragment);
        return imported.RootEdits.Concat([XamlEdits.AppendChild(document, target, imported.Source, imported.Namespaces)]).ToArray();
    }
}
