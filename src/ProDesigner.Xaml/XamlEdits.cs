using System.Text.RegularExpressions;
using System.Xml;
using ProDesigner.Core;

namespace ProDesigner.Xaml;

public static class XamlEdits
{
    public static TextEdit SetAttribute(XamlSyntaxTree tree, XamlElement element, string name, string? value)
    {
        XmlConvert.VerifyName(name);
        var current = element.Attributes.FirstOrDefault(a => a.Name == name);
        if (current is not null)
        {
            if (value is null) return new(current.Span, "", tree.Source.Substring(current.Span.Start, current.Span.Length));
            return new(current.ValueSpan, Escape(value, current.Quote), tree.Source.Substring(current.ValueSpan.Start, current.ValueSpan.Length));
        }
        if (value is null) return new(new(element.NameSpan.End, 0), "");
        var offset = element.OpenSpan.End - (element.SelfClosing ? 2 : 1);
        return new(new(offset, 0), $" {name}=\"{Escape(value)}\"");
    }
    public static string Escape(string value, char quote = '"') => value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(quote.ToString(), quote == '"' ? "&quot;" : "&apos;").Replace("\r", "&#13;").Replace("\n", "&#10;").Replace("\t", "&#9;");
    public static TextEdit Delete(XamlSyntaxTree tree, XamlElement element)
    {
        if (element.Parent is null) throw new InvalidOperationException("The document root cannot be deleted.");
        return new(element.Span, "", tree.Source.Substring(element.Span.Start, element.Span.Length));
    }
    public static TextEdit AppendChild(XamlSyntaxTree tree, XamlElement parent, string fragment)
    {
        // Parse in the root namespace context before inserting. No DTDs or external entities are permitted.
        var aliases = string.Join(" ", tree.Root.Attributes.Where(a => a.Name == "xmlns" || a.Name.StartsWith("xmlns:", StringComparison.Ordinal)).Select(a => $"{a.Name}=\"{Escape(a.Value)}\""));
        XamlSyntaxTree.Parse($"<Fragment {aliases}>{fragment}</Fragment>");
        var indentation = Indent(tree.Source, parent.Span.Start);
        var nl = tree.NewLine;
        var child = nl + indentation + "  " + fragment.Replace("\r\n", "\n").Replace("\n", nl + indentation + "  ") + nl + indentation;
        return parent.SelfClosing
            ? new(new(parent.OpenSpan.End - 2, 2), ">" + child + $"</{parent.Name}>", "/>")
            : new(new(parent.CloseSpan.Start, 0), child);
    }
    public static IReadOnlyList<TextEdit> RenameType(XamlSyntaxTree tree, XamlElement element, string type)
    {
        XmlConvert.VerifyName(type);
        var edits = new List<TextEdit> { new(element.NameSpan, type, element.Name) };
        if (!element.SelfClosing) edits.Add(new(new(element.CloseSpan.Start + 2, element.Name.Length), type, element.Name));
        return edits;
    }
    public static TextEdit Duplicate(XamlSyntaxTree tree, XamlElement element)
    {
        if (element.Parent is null) throw new InvalidOperationException("The root cannot be duplicated.");
        var fragment = tree.Source.Substring(element.Span.Start, element.Span.Length);
        var taken = tree.Elements.Select(e => e.Get("x:Name") ?? e.Get("Name")).Where(x => x is not null).ToHashSet();
        var localEdits = new List<TextEdit>();
        foreach (var node in element.DescendantsAndSelf())
        {
            var name = node.Attributes.FirstOrDefault(a => a.Name is "x:Name" or "Name");
            if (name is null) continue;
            var suffix = 2;
            var unique = name.Value + "Copy";
            while (!taken.Add(unique)) unique = name.Value + "Copy" + suffix++;
            localEdits.Add(new(new(name.ValueSpan.Start - element.Span.Start, name.ValueSpan.Length), Escape(unique, name.Quote)));
        }
        fragment = EditApplication.Apply(fragment, localEdits);
        return new(new(element.Span.End, 0), tree.NewLine + Indent(tree.Source, element.Span.Start) + fragment);
    }
    public static IReadOnlyList<TextEdit> Reparent(XamlSyntaxTree tree, XamlElement element, XamlElement target)
    {
        if (element.Parent is null || target == element || element.DescendantsAndSelf().Contains(target))
            throw new InvalidOperationException("Reparenting would create a cycle or move the document root.");
        return [Delete(tree, element), AppendChild(tree, target, tree.Source.Substring(element.Span.Start, element.Span.Length))];
    }
    public static string Indent(string source, int position)
    {
        var start = source.LastIndexOf('\n', Math.Max(0, position - 1));
        start = start < 0 ? 0 : start + 1;
        var i = start;
        while (i < position && source[i] is ' ' or '\t') i++;
        return source[start..i];
    }
}
