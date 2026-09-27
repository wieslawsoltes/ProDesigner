using ProDesigner.Core;
using ProDesigner.Xaml;
using XamlX.Parsers;

namespace ProDesigner.XamlX;

/// <summary>Real XamlX syntax/markup-extension validation, without executing constructors or emitting IL.</summary>
public sealed class XamlXSemanticService : IXamlSemanticService
{
    public IReadOnlyList<DesignDiagnostic> Validate(string source)
    {
        try
        {
            // The lossless parser enforces size/depth limits and prohibits DTDs before upstream parsing.
            XamlSyntaxTree.Parse(source);
            _ = XDocumentXamlParser.Parse(source);
            return [];
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            return [new("XAMLX001", ex.Message, DiagnosticSeverity.Error)];
        }
    }
}
