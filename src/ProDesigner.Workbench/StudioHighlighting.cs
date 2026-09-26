using System.Xml;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Highlighting.Xshd;

namespace ProDesigner.Workbench;

public static class StudioHighlighting
{
    public static IHighlightingDefinition Xaml { get; } = Load("""
<SyntaxDefinition name="ProDesigner XAML" extensions=".axaml;.xaml" xmlns="http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008">
 <Color name="Tag" foreground="#C8A9FA" />
 <Color name="Attribute" foreground="#93C5FD" />
 <Color name="Value" foreground="#B3D7A5" />
 <Color name="Comment" foreground="#778298" fontStyle="italic" />
 <Color name="Entity" foreground="#E6BE88" />
 <RuleSet>
  <Span color="Comment" begin="&lt;!--" end="--&gt;" multiline="true" />
  <Span color="Tag" begin="&lt;" end="&gt;" multiline="true">
   <RuleSet>
    <Span color="Value" begin="&quot;" end="&quot;" multiline="true" />
    <Span color="Value" begin="'" end="'" multiline="true" />
    <Rule color="Attribute">[\w:.-]+(?=\s*=)</Rule>
   </RuleSet>
  </Span>
  <Rule color="Entity">&amp;[\w#]+;</Rule>
 </RuleSet>
</SyntaxDefinition>
""");
    private static IHighlightingDefinition Load(string source)
    {
        using var reader = XmlReader.Create(new StringReader(source), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        return HighlightingLoader.Load(reader, HighlightingManager.Instance);
    }
}
