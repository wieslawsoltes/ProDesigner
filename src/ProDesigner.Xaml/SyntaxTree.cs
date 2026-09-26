using System.Net;
using System.Xml;
using ProDesigner.Core;

namespace ProDesigner.Xaml;

public sealed record XamlAttribute(string Name, string Value, SourceSpan Span, SourceSpan ValueSpan, char Quote);
public sealed class XamlElement
{
    public required string Name { get; init; }
    public required string Id { get; init; }
    public required SourceSpan NameSpan { get; init; }
    public SourceSpan Span { get; internal set; }
    public SourceSpan OpenSpan { get; internal set; }
    public SourceSpan CloseSpan { get; internal set; }
    public bool SelfClosing { get; internal set; }
    public XamlElement? Parent { get; internal set; }
    public List<XamlElement> Children { get; } = [];
    public List<XamlAttribute> Attributes { get; } = [];
    public string LocalName => Name[(Name.LastIndexOf(':') + 1)..];
    public bool IsProperty => LocalName.Contains('.');
    public string? Get(string name) => Attributes.FirstOrDefault(a => a.Name == name)?.Value;
    public string DisplayName => Get("x:Name") ?? Get("Name") ?? LocalName;
    public IEnumerable<XamlElement> DescendantsAndSelf()
    {
        yield return this;
        foreach (var child in Children)
            foreach (var descendant in child.DescendantsAndSelf()) yield return descendant;
    }
}

/// <summary>A source-preserving concrete syntax tree. Semantic analysis is supplied independently by XamlX.</summary>
public sealed class XamlSyntaxTree
{
    public const int MaximumLength = 8 * 1024 * 1024;
    public string Source { get; }
    public XamlElement Root { get; }
    public IReadOnlyList<XamlElement> Elements { get; }
    public string NewLine => Source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
    private XamlSyntaxTree(string source, XamlElement root)
    {
        Source = source; Root = root; Elements = root.DescendantsAndSelf().ToArray();
    }
    public XamlElement? Find(string? id) => id is null ? null : Elements.FirstOrDefault(e => e.Id == id);
    public XamlElement? At(int offset) => Elements.LastOrDefault(e => e.Span.Contains(offset));
    public static XamlSyntaxTree Parse(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Length > MaximumLength) throw new XmlException("Document exceeds the 8 MiB editing limit.");
        using (var reader = XmlReader.Create(new StringReader(source), new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaximumLength,
            ConformanceLevel = ConformanceLevel.Document
        }))
        {
            while (reader.Read()) if (reader.Depth > 256) throw new XmlException("Maximum XAML nesting depth is 256.");
        }
        var stack = new Stack<XamlElement>();
        XamlElement? root = null;
        var i = 0;
        while (i < source.Length)
        {
            if (source[i] != '<') { i++; continue; }
            if (source.AsSpan(i).StartsWith("<!--")) { i = source.IndexOf("-->", i + 4, StringComparison.Ordinal) + 3; continue; }
            if (source.AsSpan(i).StartsWith("<![CDATA[")) { i = source.IndexOf("]]>", i + 9, StringComparison.Ordinal) + 3; continue; }
            if (source.AsSpan(i).StartsWith("<?")) { i = source.IndexOf("?>", i + 2, StringComparison.Ordinal) + 2; continue; }
            var start = i++;
            if (source[i] == '/')
            {
                var end = source.IndexOf('>', i) + 1;
                var element = stack.Pop();
                element.CloseSpan = new(start, end - start);
                element.Span = new(element.Span.Start, end - element.Span.Start);
                i = end;
                continue;
            }
            var nameStart = i;
            while (i < source.Length && !char.IsWhiteSpace(source[i]) && source[i] is not '/' and not '>') i++;
            var parent = stack.Count == 0 ? null : stack.Peek();
            var node = new XamlElement
            {
                Name = source[nameStart..i], NameSpan = new(nameStart, i - nameStart),
                Id = parent is null ? "0" : $"{parent.Id}/{parent.Children.Count}", Parent = parent, Span = new(start, 0)
            };
            if (parent is null) root = node; else parent.Children.Add(node);
            while (i < source.Length)
            {
                while (char.IsWhiteSpace(source[i])) i++;
                if (source[i] is '/' or '>') break;
                var attrStart = i;
                while (!char.IsWhiteSpace(source[i]) && source[i] != '=') i++;
                var name = source[attrStart..i];
                while (char.IsWhiteSpace(source[i])) i++;
                i++;
                while (char.IsWhiteSpace(source[i])) i++;
                var quote = source[i++];
                var valueStart = i;
                while (source[i] != quote) i++;
                var raw = source[valueStart..i];
                i++;
                node.Attributes.Add(new(name, WebUtility.HtmlDecode(raw.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ').Replace('\t', ' ')),
                    new(attrStart, i - attrStart), new(valueStart, raw.Length), quote));
            }
            node.SelfClosing = source[i] == '/';
            if (node.SelfClosing) i++;
            i++;
            node.OpenSpan = new(start, i - start);
            if (node.SelfClosing) { node.Span = node.OpenSpan; node.CloseSpan = new(i - 2, 2); }
            else stack.Push(node);
        }
        return new(source, root ?? throw new XmlException("A root element is required."));
    }
}
