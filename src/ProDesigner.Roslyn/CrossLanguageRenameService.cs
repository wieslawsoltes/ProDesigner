using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Rename;
using ProDesigner.Core;
using ProDesigner.Xaml;
using Severity = ProDesigner.Core.DiagnosticSeverity;

namespace ProDesigner.Roslyn;

public sealed record XamlRefactoringDocument(ProjectId ProjectId, string Path, string Source);

/// <summary>Prepares a Roslyn symbol rename and source-spanned, type-bound XAML changes. It never writes files.</summary>
public sealed class CrossLanguageRenameService
{
    public async Task<RefactoringPlan> PrepareAsync(Solution solution, ProjectId projectId, string metadataName,
        string? memberName, string newName, IReadOnlyList<XamlRefactoringDocument> xamlDocuments,
        CancellationToken cancellationToken = default)
    {
        if (!SyntaxFacts.IsValidIdentifier(newName) || newName.StartsWith('@')) throw new ArgumentException("Use a non-keyword C# identifier that is also a valid XAML name.", nameof(newName));
        System.Xml.XmlConvert.VerifyNCName(newName);
        var project = solution.GetProject(projectId) ?? throw new InvalidOperationException("The project was not found.");
        var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false) ?? throw new InvalidOperationException("The project cannot be compiled for analysis.");
        var type = compilation.GetTypeByMetadataName(metadataName) ?? throw new InvalidOperationException($"Type '{metadataName}' is missing or ambiguous.");
        if (!type.Locations.Any(l => l.IsInSource)) throw new InvalidOperationException("Only project source symbols may be renamed.");
        if (type.ContainingType is not null || type.Arity != 0) throw new InvalidOperationException("Nested and generic type renaming is not yet supported by the XAML rename adapter.");
        ISymbol symbol = type;
        if (!string.IsNullOrWhiteSpace(memberName))
        {
            var members = type.GetMembers(memberName).Where(m => !m.IsImplicitlyDeclared && m is IMethodSymbol or IPropertySymbol or IEventSymbol or IFieldSymbol).ToArray();
            if (members.Length != 1) throw new InvalidOperationException("Choose a uniquely declared method, property, event or field. Overload-set renaming requires a separate review.");
            symbol = members[0];
            if (type.GetMembers(newName).Any(m => !SymbolEqualityComparer.Default.Equals(m, symbol))) throw new InvalidOperationException($"'{newName}' already exists on '{metadataName}'.");
        }
        else
        {
            var fullName = type.ContainingNamespace.IsGlobalNamespace ? newName : type.ContainingNamespace.ToDisplayString() + "." + newName;
            if (compilation.GetTypeByMetadataName(fullName) is { } existing && !SameType(existing, type)) throw new InvalidOperationException($"Type '{fullName}' already exists.");
        }
        if (symbol.Name == newName) return new(Guid.NewGuid().ToString("N"), "Rename " + symbol.Name, [], []);
        var renamed = await Renamer.RenameSymbolAsync(solution, symbol, new SymbolRenameOptions(), newName, cancellationToken).ConfigureAwait(false);
        var changes = new Dictionary<string, RefactoringFile>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var diagnostics = new List<DesignDiagnostic>();
        foreach (var changedProject in renamed.GetChanges(solution).GetProjectChanges())
        {
            foreach (var documentId in changedProject.GetChangedDocuments())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var before = solution.GetDocument(documentId)!; var after = renamed.GetDocument(documentId)!;
                if (before.FilePath is null) { diagnostics.Add(new("RENAME001", "An affected C# document has no file path.", Severity.Error)); continue; }
                var oldText = (await before.GetTextAsync(cancellationToken).ConfigureAwait(false)).ToString();
                var newText = (await after.GetTextAsync(cancellationToken).ConfigureAwait(false)).ToString();
                if (oldText != newText) Add(new(before.FilePath, oldText, newText, oldText));
            }
            var oldCompilation = await changedProject.OldProject.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
            var newCompilation = await changedProject.NewProject.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
            if (oldCompilation is null || newCompilation is null) continue;
            var oldErrors = oldCompilation.GetDiagnostics(cancellationToken).Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
                .GroupBy(d => (d.Id, d.GetMessage())).ToDictionary(g => g.Key, g => g.Count());
            foreach (var group in newCompilation.GetDiagnostics(cancellationToken).Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error).GroupBy(d => (d.Id, d.GetMessage())))
                if (group.Count() > oldErrors.GetValueOrDefault(group.Key)) diagnostics.Add(new("RENAME002", group.First().GetMessage(), Severity.Error, File: changedProject.NewProject.FilePath));
        }
        var compilations = new Dictionary<ProjectId, Compilation>();
        foreach (var document in xamlDocuments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!compilations.TryGetValue(document.ProjectId, out var context))
            {
                context = await (solution.GetProject(document.ProjectId) ?? throw new InvalidOperationException("Unknown XAML project.")).GetCompilationAsync(cancellationToken).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("XAML project compilation is unavailable.");
                compilations.Add(document.ProjectId, context);
            }
            try
            {
                var tree = XamlSyntaxTree.Parse(document.Source);
                var edits = RenameXaml(tree, context, type, symbol, newName, diagnostics, document.Path);
                var source = EditApplication.Apply(document.Source, edits);
                if (source != document.Source) { XamlSyntaxTree.Parse(source); Add(new(document.Path, document.Source, source, document.Source)); }
            }
            catch (System.Xml.XmlException ex) { diagnostics.Add(new("RENAME003", "Invalid XAML must be repaired before cross-file rename: " + ex.Message, Severity.Error, File: document.Path)); }
        }
        return new(Guid.NewGuid().ToString("N"), $"Rename {symbol.ToDisplayString()} to {newName}", changes.Values.OrderBy(c => c.Path, StringComparer.Ordinal).ToArray(), diagnostics.ToArray());

        void Add(RefactoringFile change)
        {
            if (changes.TryGetValue(change.Path, out var existing) && existing.After != change.After)
                diagnostics.Add(new("RENAME004", "Linked-file project contexts produced conflicting replacements.", Severity.Error, File: change.Path));
            else changes[change.Path] = change;
        }
    }
    private static IReadOnlyList<TextEdit> RenameXaml(XamlSyntaxTree tree, Compilation compilation, INamedTypeSymbol declaringType,
        ISymbol symbol, string newName, List<DesignDiagnostic> diagnostics, string file)
    {
        var edits = new List<TextEdit>(); var renamingType = symbol is INamedTypeSymbol;
        var classAttribute = tree.Root.Attributes.FirstOrDefault(a => Directive(tree.Root, a.Name, "Class"));
        var codeBehind = classAttribute is null ? null : compilation.GetTypeByMetadataName(classAttribute.Value);
        if (renamingType && classAttribute is not null && SameType(codeBehind, declaringType))
        {
            var value = declaringType.ContainingNamespace.IsGlobalNamespace ? newName : declaringType.ContainingNamespace.ToDisplayString() + "." + newName;
            edits.Add(XamlEdits.SetAttribute(tree, tree.Root, classAttribute.Name, value));
        }
        bool CanRenameMember(INamedTypeSymbol? owner, SourceSpan span, bool publicOnly = true)
        {
            if (!BindsMember(owner, symbol, publicOnly)) return false;
            // A rename that binds XAML to a pre-existing different member changes meaning even
            // when all C# still compiles. Refuse it instead of silently selecting that member.
            if (NearestMembers(owner, newName).Length > 0)
            {
                diagnostics.Add(new("RENAME005", $"'{owner!.ToDisplayString()}' already resolves '{newName}'. The XAML rename would change member binding.", Severity.Error, span.Start, span.Length, file));
                return false;
            }
            return true;
        }
        foreach (var node in tree.Elements)
        {
            var dot = node.Name.IndexOf('.'); var ownerName = dot < 0 ? node.Name : node.Name[..dot];
            var nodeType = ResolveType(ownerName, node, compilation);
            if (renamingType && SameType(nodeType, declaringType))
            {
                var colon = ownerName.IndexOf(':'); var prefix = colon < 0 ? "" : ownerName[..(colon + 1)];
                edits.AddRange(XamlEdits.RenameType(tree, node, prefix + newName + (dot < 0 ? "" : node.Name[dot..])));
            }
            else if (!renamingType && symbol is IPropertySymbol && dot > 0 && node.Name[(dot + 1)..] == symbol.Name && CanRenameMember(nodeType, node.NameSpan))
                edits.AddRange(XamlEdits.RenameType(tree, node, ownerName + "." + newName));
            foreach (var attribute in node.Attributes)
            {
                if (attribute == classAttribute || attribute.Name == "xmlns" || attribute.Name.StartsWith("xmlns:", StringComparison.Ordinal)) continue;
                var memberDot = attribute.Name.IndexOf('.');
                if (renamingType && memberDot > 0 && SameType(ResolveType(attribute.Name[..memberDot], node, compilation), declaringType))
                {
                    var colon = attribute.Name.IndexOf(':'); var prefix = colon < 0 || colon > memberDot ? "" : attribute.Name[..(colon + 1)];
                    edits.Add(new(new(attribute.Span.Start, attribute.Name.Length), prefix + newName + attribute.Name[memberDot..], attribute.Name));
                }
                else if (!renamingType && symbol is IPropertySymbol or IEventSymbol && attribute.Name == symbol.Name && CanRenameMember(nodeType, attribute.Span))
                    edits.Add(new(new(attribute.Span.Start, attribute.Name.Length), newName, attribute.Name));
                var value = attribute.Value;
                if (renamingType)
                {
                    if (Directive(node, attribute.Name, "DataType") || attribute.Name == "TargetType") value = RenameTypeToken(value, node, compilation, declaringType, newName);
                    value = RenameTypeExtension(value, node, compilation, declaringType, newName);
                }
                else if (symbol is IMethodSymbol && value == symbol.Name && NearestMembers(nodeType, attribute.Name).OfType<IEventSymbol>().Any() && CanRenameMember(codeBehind, attribute.ValueSpan, publicOnly: false)) value = newName;
                if (value != attribute.Value) edits.Add(XamlEdits.SetAttribute(tree, node, attribute.Name, value));
                else if (!renamingType && attribute.Value.StartsWith('{') && attribute.Value.Contains(symbol.Name, StringComparison.Ordinal))
                    diagnostics.Add(new("RENAME101", $"Review markup extension '{attribute.Name}': binding paths and custom extensions are not rewritten without an unambiguous data-context symbol.", Severity.Warning, attribute.ValueSpan.Start, attribute.ValueSpan.Length, file));
                else if (renamingType && attribute.Value.Contains(symbol.Name, StringComparison.Ordinal) && (attribute.Name == "Selector" || attribute.Value.StartsWith('{')))
                    diagnostics.Add(new("RENAME103", $"Review '{attribute.Name}': this selector or extension is outside the supported type-reference grammar and has not been rewritten.", Severity.Warning, attribute.ValueSpan.Start, attribute.ValueSpan.Length, file));
            }
        }
        return edits;
    }
    private static ISymbol[] NearestMembers(INamedTypeSymbol? type, string name)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            var members = current.GetMembers(name).Where(m => !m.IsImplicitlyDeclared).ToArray();
            if (members.Length > 0) return members; // A new/hidden member shadows the base lookup.
        }
        return [];
    }
    private static bool BindsMember(INamedTypeSymbol? owner, ISymbol target, bool publicOnly)
    {
        var candidates = NearestMembers(owner, target.Name);
        if (candidates.Length != 1 || candidates[0].Kind != target.Kind || (publicOnly && candidates[0].DeclaredAccessibility != Accessibility.Public)) return false;
        var actual = OverrideRoot(candidates[0]); var expected = OverrideRoot(target);
        return actual.Kind == expected.Kind && actual.MetadataName == expected.MetadataName && actual.ContainingType is { } a && expected.ContainingType is { } b && SameType(a, b);
    }
    private static ISymbol OverrideRoot(ISymbol symbol)
    {
        while (true)
        {
            ISymbol? parent = symbol switch
            {
                IPropertySymbol p => p.OverriddenProperty, IEventSymbol e => e.OverriddenEvent,
                IMethodSymbol m => m.OverriddenMethod, _ => null
            };
            if (parent is null) return symbol;
            symbol = parent;
        }
    }
    private static string RenameTypeExtension(string value, XamlElement node, Compilation compilation, INamedTypeSymbol type, string newName)
    {
        var match = Regex.Match(value, @"^\{(?<prefix>[A-Za-z_][\w]*):(?<kind>Type|Static)\s+(?<token>[A-Za-z_][\w:.]*)(?<tail>\s*)\}$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        if (!match.Success || XamlNames.Namespace(node, match.Groups["prefix"].Value) != XamlNames.LanguageNamespace) return value;
        var token = match.Groups["token"]; var original = token.Value; var suffix = "";
        if (match.Groups["kind"].Value == "Static") { var dot = original.LastIndexOf('.'); if (dot < 0) return value; suffix = original[dot..]; original = original[..dot]; }
        var renamed = RenameTypeToken(original, node, compilation, type, newName);
        return renamed == original ? value : value[..token.Index] + renamed + suffix + value[(token.Index + token.Length)..];
    }
    private static string RenameTypeToken(string token, XamlElement node, Compilation compilation, INamedTypeSymbol type, string newName)
    {
        if (!SameType(ResolveType(token, node, compilation), type)) return token;
        var colon = token.IndexOf(':'); return (colon < 0 ? "" : token[..(colon + 1)]) + newName;
    }
    private static bool Directive(XamlElement node, string attribute, string name)
    {
        var colon = attribute.IndexOf(':'); return colon > 0 && attribute[(colon + 1)..] == name && XamlNames.Namespace(node, attribute[..colon]) == XamlNames.LanguageNamespace;
    }
    private static INamedTypeSymbol? ResolveType(string token, XamlElement node, Compilation compilation)
    {
        var colon = token.IndexOf(':'); var prefix = colon < 0 ? "" : token[..colon]; var name = colon < 0 ? token : token[(colon + 1)..];
        var uri = XamlNames.Namespace(node, prefix);
        if (uri?.StartsWith("using:", StringComparison.Ordinal) == true) return compilation.GetTypeByMetadataName(uri[6..] + "." + name);
        if (uri?.StartsWith("clr-namespace:", StringComparison.Ordinal) == true)
        {
            var parts = uri[14..].Split(';'); var type = compilation.GetTypeByMetadataName(parts[0] + "." + name);
            var assembly = parts.FirstOrDefault(p => p.StartsWith("assembly=", StringComparison.Ordinal))?[9..];
            return assembly is null || type?.ContainingAssembly.Name == assembly ? type : null;
        }
        if (uri == XamlNames.AvaloniaNamespace)
            return new[] { "Avalonia.Controls", "Avalonia.Controls.Primitives", "Avalonia.Controls.Shapes", "Avalonia.Styling", "Avalonia.Media", "Avalonia" }
                .Select(ns => compilation.GetTypeByMetadataName(ns + "." + name)).FirstOrDefault(t => t is not null);
        return null;
    }
    private static bool SameType(INamedTypeSymbol? a, INamedTypeSymbol b) => a is not null && a.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == b.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) && a.ContainingAssembly.Identity.Equals(b.ContainingAssembly.Identity);
}
