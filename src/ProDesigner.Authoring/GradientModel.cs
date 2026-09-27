using System.Globalization;
using System.Xml.Linq;
using ProDesigner.Xaml;

namespace ProDesigner.Authoring;

public sealed record GradientStopModel(double Offset, string Color);
public sealed record GradientModel(double StartX, double StartY, double EndX, double EndY, IReadOnlyList<GradientStopModel> Stops)
{
    public string ToXaml()
    {
        if (new[] { StartX, StartY, EndX, EndY }.Any(n => !double.IsFinite(n)) || Stops.Count < 2 || Stops.Count > 128)
            throw new ArgumentException("A gradient requires finite coordinates and 2–128 stops.");
        if (Stops.Any(s => !double.IsFinite(s.Offset) || s.Offset is < 0 or > 1 || string.IsNullOrWhiteSpace(s.Color))) throw new ArgumentException("Gradient stops need a color and an offset from 0 to 1.");
        static string N(double n) => n.ToString("R", CultureInfo.InvariantCulture);
        return $"<LinearGradientBrush StartPoint=\"{N(StartX * 100)}%,{N(StartY * 100)}%\" EndPoint=\"{N(EndX * 100)}%,{N(EndY * 100)}%\">" +
            string.Join("", Stops.OrderBy(s => s.Offset).Select(s => $"<GradientStop Offset=\"{N(s.Offset)}\" Color=\"{XamlEdits.Escape(s.Color)}\" />")) + "</LinearGradientBrush>";
    }
}
