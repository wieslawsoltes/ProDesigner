using ProDesigner.Core;

namespace ProDesigner.Xaml;

/// <summary>Token-aware markup-extension reference edits. Quoted fallback text and custom extension arguments are never renamed.</summary>
public static class MarkupReferences
{
    public static string Rename(string value, string oldName, string newName)
    {
        if (!value.StartsWith('{') || value.StartsWith("{}", StringComparison.Ordinal)) return value;
        var edits = new List<TextEdit>();
        void Reference(int start, int end, bool path)
        {
            while (start < end && char.IsWhiteSpace(value[start])) start++;
            while (end > start && char.IsWhiteSpace(value[end - 1])) end--;
            if (start < end && value[start] is '\'' or '"') { start++; end--; }
            if (path)
            {
                if (start >= end || value[start] != '#') return;
                start++; var dot = value.IndexOf('.', start, end - start); if (dot >= 0) end = dot;
            }
            if (end - start == oldName.Length && value.AsSpan(start, end - start).SequenceEqual(oldName)) edits.Add(new(new(start, oldName.Length), newName));
        }
        int Visit(int start)
        {
            var i = start + 1; while (i < value.Length && char.IsWhiteSpace(value[i])) i++;
            var nameStart = i; while (i < value.Length && !char.IsWhiteSpace(value[i]) && value[i] is not ',' and not '}') i++;
            var name = value[nameStart..i];
            var binding = name is "Binding" or "CompiledBinding" or "ReflectionBinding";
            var reference = name == "Reference" || name.EndsWith(":Reference", StringComparison.Ordinal);
            var segment = i; var first = true;
            void Argument(int end)
            {
                var a = segment; while (a < end && char.IsWhiteSpace(value[a])) a++;
                var equals = -1; char quote = '\0'; var depth = 0;
                for (var j = a; j < end; j++)
                {
                    var c = value[j];
                    if (quote != '\0') { if (c == quote) quote = '\0'; continue; }
                    if (c is '\'' or '"') { quote = c; continue; }
                    if (c == '{') depth++; if (c == '}') depth--;
                    if (c == '=' && depth == 0) { equals = j; break; }
                }
                var property = equals < 0 ? "" : value[a..equals].Trim();
                if (binding && property == "ElementName") Reference(equals + 1, end, false);
                else if (binding && (property == "Path" || first && equals < 0)) Reference(equals < 0 ? a : equals + 1, end, true);
                else if (reference && (property == "Name" || first && equals < 0)) Reference(equals < 0 ? a : equals + 1, end, false);
                first = false;
            }
            while (i < value.Length)
            {
                if (value[i] is '\'' or '"') { var quote = value[i++]; while (i < value.Length && value[i] != quote) i++; if (i < value.Length) i++; continue; }
                if (value[i] == '{') { i = Visit(i); continue; }
                if (value[i] is ',' or '}')
                {
                    Argument(i); if (value[i] == '}') return i + 1;
                    segment = ++i; continue;
                }
                i++;
            }
            return i;
        }
        Visit(0); return EditApplication.Apply(value, edits.DistinctBy(e => e.Span));
    }
}
