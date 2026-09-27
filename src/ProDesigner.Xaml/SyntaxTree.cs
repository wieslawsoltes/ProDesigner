using System.Net;
using System.Xml;
using ProDesigner.Core;

namespace ProDesigner.Xaml;

public sealed record XamlAttribute(string Name, string Value, SourceSpan Span, SourceSpan ValueSpan, char Quote);
public sealed class XamlElement
{
    private static long _nextIdentity;
    private readonly List<XamlElement> _children = [];
    private readonly List<XamlAttribute> _attributes = [];
    public XamlElement() { Children = _children.AsReadOnly(); Attributes = _attributes.AsReadOnly(); }
    /// <summary>Process-local logical identity retained by incremental edits and unambiguous structural reconciliation.</summary>
    public long Identity { get; internal set; } = Interlocked.Increment(ref _nextIdentity);
    internal void AddChild(XamlElement child) => _children.Add(child);
    internal void AddAttribute(XamlAttribute attribute) => _attributes.Add(attribute);
    public required string Name { get; init; }
    public required string Id { get; init; }
    public required SourceSpan NameSpan { get; init; }
    public SourceSpan Span { get; internal set; }
    public SourceSpan OpenSpan { get; internal set; }
    public SourceSpan CloseSpan { get; internal set; }
    public bool SelfClosing { get; internal set; }
    public XamlElement? Parent { get; internal set; }
    public IReadOnlyList<XamlElement> Children { get; }
    public IReadOnlyList<XamlAttribute> Attributes { get; }
    public string LocalName => Name[(Name.LastIndexOf(':') + 1)..];
    public bool IsProperty => LocalName.Contains('.');
    public string? Get(string name) => Attributes.FirstOrDefault(a => a.Name == name)?.Value;
    public string DisplayName => XamlNames.Name(this) ?? LocalName;
    public IEnumerable<XamlElement> DescendantsAndSelf()
    {
        yield return this;
        foreach (var child in Children)
            foreach (var descendant in child.DescendantsAndSelf()) yield return descendant;
    }
}

/// <summary>A source-preserving concrete syntax tree. Semantic analysis is supplied independently by XamlX.</summary>
public sealed partial class XamlSyntaxTree
{
    public const int MaximumLength = 8 * 1024 * 1024;
    private readonly Dictionary<string, XamlElement> _index;
    private readonly Dictionary<long, XamlElement> _identities;
    public string Source { get; }
    public XamlElement Root { get; }
    public IReadOnlyList<XamlElement> Elements { get; }
    public string NewLine => Source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
    private XamlSyntaxTree(string source, XamlElement root)
    {
        Source = source; Root = root; Elements = Array.AsReadOnly(root.DescendantsAndSelf().ToArray());
        _index = Elements.ToDictionary(e => e.Id);
        _identities = Elements.ToDictionary(e => e.Identity);
    }
    public XamlElement? FindIdentity(long identity) => _identities.GetValueOrDefault(identity);
    public XamlElement? Find(string? id) => id is null ? null : _index.GetValueOrDefault(id);
    public XamlElement? At(int offset)
    {
        var low = 0; var high = Elements.Count - 1; XamlElement? candidate = null;
        while (low <= high) { var mid = low + (high - low) / 2; if (Elements[mid].Span.Start <= offset) { candidate = Elements[mid]; low = mid + 1; } else high = mid - 1; }
        while (candidate is not null && !candidate.Span.Contains(offset)) candidate = candidate.Parent;
        return candidate;
    }
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
            if (parent is null) root = node; else parent.AddChild(node);
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
                node.AddAttribute(new(name, WebUtility.HtmlDecode(raw.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ').Replace('\t', ' ')),
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
