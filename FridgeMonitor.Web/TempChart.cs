using System.Globalization;
using System.Text;
using FridgeMonitor.Core;
using Microsoft.AspNetCore.Html;

namespace FridgeMonitor.Web;

/// <summary>
/// Server-rendered SVG temperature chart. No JavaScript or CDN, so it works offline and loads fast
/// on a phone. The line breaks at gaps and ERR readings instead of drawing across missing data.
/// </summary>
public static class TempChart
{
    const double W = 800, H = 260, Left = 34, Right = 8, Top = 10, Bottom = 26;
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static HtmlString Render(IReadOnlyList<Reading> readings, DateTime from, DateTime to, double limitC, TimeSpan gapThreshold, string summary)
    {
        var temps = readings.Where(r => !r.IsError).Select(r => r.TempC!.Value).ToList();
        double min = Math.Floor(Math.Min(0, temps.DefaultIfEmpty(0).Min()));
        double max = Math.Ceiling(Math.Max(limitC + 3, temps.DefaultIfEmpty(0).Max()));
        double span = Math.Max((to - from).TotalMinutes, 1);

        double X(DateTime t) => Left + (t - from).TotalMinutes / span * (W - Left - Right);
        double Y(double c) => Top + (max - c) / (max - min) * (H - Top - Bottom);
        static string N(double v) => v.ToString("0.#", Inv);

        var svg = new StringBuilder();
        svg.Append("<svg viewBox=\"0 0 800 260\" class=\"chart\" role=\"img\" aria-label=\"")
           .Append(System.Net.WebUtility.HtmlEncode(summary)).Append("\">");

        // Horizontal grid with °C labels.
        double step = max - min > 12 ? 5 : 2;
        for (double c = Math.Ceiling(min / step) * step; c <= max; c += step)
            svg.Append($"<line class=\"grid\" x1=\"{N(Left)}\" x2=\"{N(W - Right)}\" y1=\"{N(Y(c))}\" y2=\"{N(Y(c))}\"/>")
               .Append($"<text class=\"axis\" x=\"{N(Left - 4)}\" y=\"{N(Y(c) + 4)}\" text-anchor=\"end\">{N(c)}</text>");

        // A tick at each midnight, labelled with the day.
        for (var d = from.Date.AddDays(1); d <= to; d = d.AddDays(1))
            svg.Append($"<line class=\"grid\" x1=\"{N(X(d))}\" x2=\"{N(X(d))}\" y1=\"{N(Top)}\" y2=\"{N(H - Bottom)}\"/>")
               .Append($"<text class=\"axis\" x=\"{N(X(d) + 3)}\" y=\"{N(H - 8)}\">{d.ToString("ddd d", Inv)}</text>");

        svg.Append($"<line class=\"limit\" x1=\"{N(Left)}\" x2=\"{N(W - Right)}\" y1=\"{N(Y(limitC))}\" y2=\"{N(Y(limitC))}\"/>")
           .Append($"<text class=\"limit-label\" x=\"{N(W - Right - 2)}\" y=\"{N(Y(limitC) - 4)}\" text-anchor=\"end\">{N(limitC)}°C</text>");

        // The line, split into segments wherever data is missing.
        var segment = new List<Reading>();
        void Flush()
        {
            if (segment.Count == 1)
                svg.Append($"<circle class=\"line-dot\" cx=\"{N(X(segment[0].Time))}\" cy=\"{N(Y(segment[0].TempC!.Value))}\" r=\"2\"/>");
            else if (segment.Count > 1)
                svg.Append("<polyline class=\"line\" points=\"")
                   .AppendJoin(' ', segment.Select(r => $"{N(X(r.Time))},{N(Y(r.TempC!.Value))}"))
                   .Append("\"/>");
            segment.Clear();
        }
        Reading? previous = null;
        foreach (var r in readings)
        {
            if (r.IsError || (previous is not null && r.Time - previous.Time > gapThreshold)) Flush();
            if (!r.IsError) segment.Add(r);
            previous = r;
        }
        Flush();

        // Mark every reading above the limit so brief spikes are visible too.
        foreach (var r in readings.Where(r => r.TempC > limitC))
            svg.Append($"<circle class=\"over\" cx=\"{N(X(r.Time))}\" cy=\"{N(Y(r.TempC!.Value))}\" r=\"3\"/>");

        svg.Append("</svg>");
        return new HtmlString(svg.ToString());
    }
}
