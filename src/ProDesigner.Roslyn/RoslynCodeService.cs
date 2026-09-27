using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using ProDesigner.Core;
using Severity = ProDesigner.Core.DiagnosticSeverity;

namespace ProDesigner.Roslyn;

public sealed class RoslynCodeService : ICodeService
{
    public IReadOnlyList<DesignDiagnostic> Validate(string source) => CSharpSyntaxTree.ParseText(source).GetDiagnostics().Select(d =>
        new DesignDiagnostic(d.Id, d.GetMessage(), d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error ? Severity.Error : Severity.Warning,
            d.Location.SourceSpan.Start, d.Location.SourceSpan.Length)).ToArray();
    public string EnsureEventHandler(string source, string className, string handlerName)
    {
        if (!SyntaxFacts.IsValidIdentifier(handlerName)) throw new ArgumentException("A valid C# method identifier is required.", nameof(handlerName));
        var root = CSharpSyntaxTree.ParseText(source).GetCompilationUnitRoot();
        if (root.ContainsDiagnostics) throw new InvalidOperationException("Fix C# syntax errors before generating a handler.");
        var type = root.DescendantNodes().OfType<ClassDeclarationSyntax>().FirstOrDefault(c => c.Identifier.ValueText == className)
                   ?? throw new InvalidOperationException($"Class '{className}' was not found.");
        if (type.Members.OfType<MethodDeclarationSyntax>().Any(m => m.Identifier.ValueText == handlerName)) return source;
        var method = SyntaxFactory.ParseMemberDeclaration($"private void {handlerName}(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)\n{{\n}}")!;
        var nl = source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        method = method.NormalizeWhitespace("    ", nl).WithLeadingTrivia(SyntaxFactory.EndOfLine(nl), SyntaxFactory.Whitespace("    "))
            .WithTrailingTrivia(SyntaxFactory.EndOfLine(nl));
        return root.ReplaceNode(type, type.AddMembers(method)).ToFullString();
    }
    public IReadOnlyList<CompletionItem> GetMembers(Compilation compilation, string metadataName)
    {
        var type = compilation.GetTypeByMetadataName(metadataName);
        if (type is null) return [];
        var items = new List<CompletionItem>();
        for (var current = type; current is not null; current = current.BaseType)
            items.AddRange(current.GetMembers().Where(s => s.DeclaredAccessibility == Accessibility.Public && !s.IsImplicitlyDeclared)
                .Select(s => new CompletionItem(s.Name, s.Kind.ToString(), s.ToDisplayString())));
        return items.DistinctBy(i => (i.Label, i.Kind)).OrderBy(i => i.Label).ToArray();
    }
    public async Task<Solution> RenameAsync(Document document, int position, string newName, CancellationToken cancellationToken = default)
    {
        if (!SyntaxFacts.IsValidIdentifier(newName)) throw new ArgumentException("A valid identifier is required.", nameof(newName));
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false) ?? throw new InvalidOperationException("Missing syntax root.");
        var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false) ?? throw new InvalidOperationException("Missing semantic model.");
        var node = root.FindToken(position).Parent ?? throw new InvalidOperationException("No symbol at the requested position.");
        var symbol = model.GetSymbolInfo(node, cancellationToken).Symbol ?? model.GetDeclaredSymbol(node, cancellationToken)
            ?? throw new InvalidOperationException("No resolvable symbol at the requested position.");
        return await Microsoft.CodeAnalysis.Rename.Renamer.RenameSymbolAsync(document.Project.Solution, symbol,
            new Microsoft.CodeAnalysis.Rename.SymbolRenameOptions(), newName, cancellationToken).ConfigureAwait(false);
    }
}
