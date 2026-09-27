using Microsoft.CodeAnalysis;
using ProDesigner.Core;
using ProDesigner.Xaml;
using Severity = ProDesigner.Core.DiagnosticSeverity;

namespace ProDesigner.Roslyn;

public sealed record XamlMemberDescriptor(string Name, string Type, string Kind, bool Writable);
public sealed record BoundXamlElement(string Id, string Type, SourceSpan Span, IReadOnlyList<XamlMemberDescriptor> Members);
public sealed record XamlCompilationAnalysis(IReadOnlyList<BoundXamlElement> Elements, IReadOnlyList<DesignDiagnostic> Diagnostics);

/// <summary>Binds source-preserving XAML nodes to actual Roslyn project and reference-assembly symbols without executing project types.</summary>
public sealed class XamlCompilationService
{
    private static readonly string[] AvaloniaNamespaces = ["Avalonia.Controls", "Avalonia.Controls.Primitives", "Avalonia.Controls.Shapes", "Avalonia.Controls.Templates", "Avalonia.Styling", "Avalonia.Media", "Avalonia.Animation", "Avalonia.Animation.Easings", "Avalonia.Data", "Avalonia"];
    public XamlCompilationAnalysis Analyze(string source, Compilation compilation)
    {
        var tree = XamlSyntaxTree.Parse(source); var diagnostics = new List<DesignDiagnostic>(); var elements = new List<BoundXamlElement>();
        var codeBehind = tree.Root.Get("x:Class") is { } className ? compilation.GetTypeByMetadataName(className) : null;
        foreach (var node in tree.Elements.Where(n => !n.IsProperty))
        {
            var type = ResolveType(node.Name, node, compilation);
            if (type is null)
            {
                diagnostics.Add(new("XAML101", $"Type '{node.Name}' could not be resolved in this project's compilation.", Severity.Warning, node.NameSpan.Start, node.NameSpan.Length));
                continue;
            }
            var members = AllMembers(type).Where(m => m.DeclaredAccessibility == Accessibility.Public && !m.IsStatic)
                .Where(m => m is IPropertySymbol or IEventSymbol).DistinctBy(m => m.Name).ToArray();
            elements.Add(new(node.Id, type.ToDisplayString(), node.Span, members.Select(m => m switch
            {
                IPropertySymbol p => new XamlMemberDescriptor(p.Name, p.Type.ToDisplayString(), "Property", p.SetMethod is not null),
                IEventSymbol e => new XamlMemberDescriptor(e.Name, e.Type.ToDisplayString(), "Event", true),
                _ => throw new InvalidOperationException()
            }).OrderBy(m => m.Name).ToArray()));
            foreach (var attribute in node.Attributes)
            {
                if (attribute.Name == "xmlns" || attribute.Name.StartsWith("xmlns:",StringComparison.Ordinal) || attribute.Name.StartsWith("x:",StringComparison.Ordinal) || attribute.Name.StartsWith("d:",StringComparison.Ordinal) || attribute.Name.StartsWith("mc:",StringComparison.Ordinal)) continue;
                var dot = attribute.Name.LastIndexOf('.');
                if (dot > 0)
                {
                    var ownerName = attribute.Name[..dot]; var property = attribute.Name[(dot+1)..]; var owner = ResolveType(ownerName,node,compilation);
                    if (owner is null || !AllMembers(owner).Any(m => m.IsStatic && (m.Name == property + "Property" || m.Name == "Set" + property)))
                        diagnostics.Add(new("XAML103", $"Attached property '{attribute.Name}' could not be resolved.", Severity.Warning, attribute.Span.Start, attribute.Span.Length));
                    continue;
                }
                var member = members.FirstOrDefault(m => m.Name == attribute.Name);
                if (member is null)
                    diagnostics.Add(new("XAML102", $"'{type.Name}' has no public XAML property or event named '{attribute.Name}'.", Severity.Warning, attribute.Span.Start, attribute.Span.Length));
                else if (member is IEventSymbol && codeBehind is not null && !attribute.Value.StartsWith('{') && !AllMembers(codeBehind).OfType<IMethodSymbol>().Any(m => m.Name == attribute.Value))
                    diagnostics.Add(new("XAML104", $"Event handler '{attribute.Value}' was not found in '{codeBehind.ToDisplayString()}'.", Severity.Warning, attribute.ValueSpan.Start, attribute.ValueSpan.Length));
            }
        }
        return new(elements, diagnostics);
    }
    private static IEnumerable<ISymbol> AllMembers(INamedTypeSymbol type)
    {
        for (var current=type;current is not null;current=current.BaseType)
            foreach(var member in current.GetMembers()) yield return member;
    }
    private static INamedTypeSymbol? ResolveType(string qualifiedName, XamlElement context, Compilation compilation)
    {
        var split=qualifiedName.IndexOf(':'); var prefix=split<0?"":qualifiedName[..split]; var name=split<0?qualifiedName:qualifiedName[(split+1)..];
        string? ns=null;
        for(var node=context;node is not null;node=node.Parent)
            if(node.Get(prefix.Length==0?"xmlns":"xmlns:"+prefix) is { } resolved) {ns=resolved;break;}
        if(ns=="https://github.com/avaloniaui" || (ns is null && prefix.Length==0))
            return AvaloniaNamespaces.Select(n=>compilation.GetTypeByMetadataName(n+"."+name)).FirstOrDefault(t=>t is not null);
        if(ns?.StartsWith("using:",StringComparison.Ordinal)==true) return compilation.GetTypeByMetadataName(ns[6..]+"."+name);
        if(ns?.StartsWith("clr-namespace:",StringComparison.Ordinal)==true) return compilation.GetTypeByMetadataName(ns[14..].Split(';')[0]+"."+name);
        if(ns=="http://schemas.microsoft.com/winfx/2006/xaml") return compilation.GetTypeByMetadataName("System."+name);
        return null;
    }
}
