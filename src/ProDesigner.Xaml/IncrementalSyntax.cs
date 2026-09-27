using System.Xml;
using ProDesigner.Core;

namespace ProDesigner.Xaml;

public enum SyntaxUpdateKind { Unchanged, AttributeValues, FullParse }
public sealed record XamlSyntaxUpdate(XamlSyntaxTree Tree, SyntaxUpdateKind Kind);

public sealed partial class XamlSyntaxTree
{
    /// <summary>Creates an immutable source snapshot. Existing attribute-value changes avoid rescanning the full XML document.</summary>
    public XamlSyntaxUpdate ApplyEdits(IEnumerable<TextEdit> edits)
    {
        var ordered = edits.OrderBy(e => e.Span.Start).ToArray();
        var source = EditApplication.Apply(Source, ordered);
        if (source == Source) return new(this, SyntaxUpdateKind.Unchanged);
        if (source.Length > MaximumLength) throw new XmlException("Document exceeds the 8 MiB editing limit.");
        if (TryAttributeUpdate(source, ordered, out var incremental)) return new(incremental!, SyntaxUpdateKind.AttributeValues);
        var parsed = Parse(source);
        ReconcileIdentities(parsed, ordered);
        return new(new XamlSyntaxTree(source, parsed.Root), SyntaxUpdateKind.FullParse);
    }
    /// <summary>Finds the smallest text replacement, expanding in-value typing to a complete attribute edit when possible.</summary>
    public XamlSyntaxUpdate WithText(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source == Source) return new(this, SyntaxUpdateKind.Unchanged);
        var start = 0;
        while (start < source.Length && start < Source.Length && source[start] == Source[start]) start++;
        var oldEnd = Source.Length; var newEnd = source.Length;
        while (oldEnd > start && newEnd > start && Source[oldEnd - 1] == source[newEnd - 1]) { oldEnd--; newEnd--; }
        var attribute = At(start)?.Attributes.FirstOrDefault(a => a.ValueSpan.Start <= start && a.ValueSpan.End >= oldEnd);
        if (attribute is not null)
        {
            var text = Source[attribute.ValueSpan.Start..start] + source[start..newEnd] + Source[oldEnd..attribute.ValueSpan.End];
            return ApplyEdits([new(attribute.ValueSpan, text)]);
        }
        return ApplyEdits([new(new(start, oldEnd - start), source[start..newEnd])]);
    }
    private bool TryAttributeUpdate(string source, TextEdit[] edits, out XamlSyntaxTree? result)
    {
        result = null;
        var candidates = Elements.SelectMany(e => e.Attributes).ToDictionary(a => a.ValueSpan);
        var replacements = new Dictionary<SourceSpan, (string Raw, string Value)>();
        foreach (var edit in edits)
        {
            if (!candidates.TryGetValue(edit.Span, out var attribute) || attribute.Name == "xmlns" || attribute.Name.StartsWith("xmlns:", StringComparison.Ordinal) || attribute.Name.StartsWith("xml:", StringComparison.Ordinal) || replacements.ContainsKey(edit.Span)) return false;
            // Parse in an isolated attribute container. Check its exact shape, not just well-formedness,
            // so a quote-breaking replacement cannot introduce a second attribute or XML element.
            try
            {
                using var reader = XmlReader.Create(new StringReader($"<r v={attribute.Quote}{edit.NewText}{attribute.Quote}/>"),
                    new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaximumLength + 16 });
                reader.MoveToContent();
                if (reader.Name != "r" || !reader.IsEmptyElement || reader.AttributeCount != 1 || reader.GetAttribute("v") is not { } value) return false;
                if (reader.Read()) return false;
                replacements.Add(edit.Span, (edit.NewText, value));
            }
            catch (XmlException) { return false; } // Full parsing below provides correct document line/column diagnostics.
        }
        int Offset(int offset)
        {
            var delta = 0;
            foreach (var edit in edits)
                if (edit.Span.End < offset || edit.Span.End == offset && edit.Span.Length > 0) delta += edit.NewText.Length - edit.Span.Length;
            return offset + delta;
        }
        SourceSpan Span(SourceSpan span) => new(Offset(span.Start), Offset(span.End) - Offset(span.Start));
        XamlElement Clone(XamlElement node, XamlElement? parent)
        {
            var clone = new XamlElement { Name = node.Name, Id = node.Id, Identity = node.Identity, Parent = parent,
                NameSpan = Span(node.NameSpan), Span = Span(node.Span), OpenSpan = Span(node.OpenSpan), CloseSpan = Span(node.CloseSpan), SelfClosing = node.SelfClosing };
            foreach (var attribute in node.Attributes)
            {
                var changed = replacements.TryGetValue(attribute.ValueSpan, out var replacement);
                clone.AddAttribute(new(attribute.Name, changed ? replacement.Value : attribute.Value, Span(attribute.Span),
                    changed ? new(Offset(attribute.ValueSpan.Start), replacement.Raw.Length) : Span(attribute.ValueSpan), attribute.Quote));
            }
            foreach (var child in node.Children) clone.AddChild(Clone(child, clone));
            return clone;
        }
        result = new(source, Clone(Root, null)); return true;
    }
    private void ReconcileIdentities(XamlSyntaxTree next, TextEdit[] edits)
    {
        var starts = next.Elements.ToDictionary(n => n.Span.Start); var assigned = new HashSet<XamlElement>(); var reused = new HashSet<long>();
        foreach (var previous in Elements)
        {
            // A replaced/deleted subtree is not the same object merely because a new element occupies its offset.
            if (edits.Any(e => e.Span.Length > 0 && e.Span.Contains(previous.Span.Start))) continue;
            var offset = previous.Span.Start + edits.Where(e => e.Span.End <= previous.Span.Start).Sum(e => e.NewText.Length - e.Span.Length);
            if (!starts.TryGetValue(offset, out var node) || assigned.Contains(node)) continue;
            var rename = edits.FirstOrDefault(e => e.Span == previous.NameSpan);
            if (node.Name != previous.Name && rename?.NewText != node.Name) continue;
            node.Identity = previous.Identity; assigned.Add(node); reused.Add(previous.Identity);
        }
        // Root-scope names uniquely identify moved nodes, but duplicate/template names are deliberately not guessed.
        var named = Elements.Where(n => XamlNames.Scope(n) == Root && XamlNames.Name(n) is not null)
            .GroupBy(n => XamlNames.Name(n)!, StringComparer.Ordinal).Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.Single());
        var targetNames = next.Elements.Where(n => XamlNames.Scope(n) == next.Root && XamlNames.Name(n) is not null)
            .GroupBy(n => XamlNames.Name(n)!, StringComparer.Ordinal).Where(g => g.Count() == 1);
        foreach (var group in targetNames)
        {
            var node = group.Single();
            if (!assigned.Contains(node) && named.TryGetValue(group.Key, out var previous) && !reused.Contains(previous.Identity) && node.Name == previous.Name)
            { node.Identity = previous.Identity; assigned.Add(node); reused.Add(previous.Identity); }
        }
    }
}
