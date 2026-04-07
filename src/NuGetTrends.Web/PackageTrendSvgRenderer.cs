using System.Globalization;
using System.Text;
using System.Xml.Linq;
using NuGetTrends.Data;

namespace NuGetTrends.Web;

internal static class PackageTrendSvgRenderer
{
    private const int Width = 800;
    private const int Height = 420;
    private const double ChartLeft = 72;
    private const double ChartTop = 64;
    private const double ChartWidth = 688;
    private const double ChartHeight = 262;
    private const string FontFamily = "system-ui, sans-serif";
    private const string BackgroundStroke = "#dbe4f0";
    private const string GridStroke = "#e2e8f0";
    private const string PlotBackground = "#ffffff";
    private const string TitleFill = "#0f172a";
    private const string MutedFill = "#64748b";
    private const string EmptyStateFill = "#334155";
    private const string AccentFill = "#2563eb";
    private static readonly XNamespace SvgNamespace = "http://www.w3.org/2000/svg";

    public static string Render(string packageId, IReadOnlyList<DailyDownloadResult> downloads, int months)
    {
        var chartTitle = $"Average daily downloads by week, last {months} month{(months == 1 ? "" : "s")}";
        var points = downloads
            .Where(d => d.Count.HasValue)
            .OrderBy(d => d.Week)
            .ToList();

        var document = new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            new XElement(SvgNamespace + "svg",
                new XAttribute("xmlns", SvgNamespace.NamespaceName),
                new XAttribute("width", Width),
                new XAttribute("height", Height),
                new XAttribute("viewBox", $"0 0 {Width} {Height}"),
                new XAttribute("role", "img"),
                new XAttribute("aria-labelledby", "title desc"),
                new XElement(SvgNamespace + "title",
                    new XAttribute("id", "title"),
                    $"{packageId} download trend"),
                new XElement(SvgNamespace + "desc",
                    new XAttribute("id", "desc"),
                    $"NuGet Trends chart for {packageId}. {chartTitle}."),
                CreateDefs(),
                CreateRect(0, 0, Width, Height, "url(#chart-bg)", BackgroundStroke, rx: 18),
                CreateText(packageId, ChartLeft, 34, TitleFill, 24, fontWeight: "700"),
                CreateText(chartTitle, ChartLeft, 54, MutedFill, 13),
                CreateRect(ChartLeft, ChartTop, ChartWidth, ChartHeight, PlotBackground, GridStroke, rx: 12),
                points.Count == 0
                    ? CreateEmptyState()
                    : CreateChart(points)));

        using var writer = new Utf8StringWriter();
        document.Save(writer, SaveOptions.DisableFormatting);
        return writer.ToString();
    }

    private static XElement CreateDefs()
    {
        return new XElement(SvgNamespace + "defs",
            new XElement(SvgNamespace + "linearGradient",
                new XAttribute("id", "chart-bg"),
                new XAttribute("x1", "0%"),
                new XAttribute("y1", "0%"),
                new XAttribute("x2", "0%"),
                new XAttribute("y2", "100%"),
                CreateStop("0%", "#ffffff"),
                CreateStop("100%", "#f8fafc")));
    }

    private static IEnumerable<XElement> CreateEmptyState()
    {
        var centerX = ChartLeft + ChartWidth / 2;
        var centerY = ChartTop + ChartHeight / 2;

        return CreateGrid(0, 4_000, null)
            .Append(CreateText("No download history yet", centerX, centerY - 8, EmptyStateFill, 18,
                anchor: "middle", fontWeight: "600"))
            .Append(CreateText(
                "The package exists, but NuGet Trends has not collected weekly data for it yet.",
                centerX, centerY + 16, MutedFill, 13, anchor: "middle"));
    }

    private static IEnumerable<XElement> CreateChart(IReadOnlyList<DailyDownloadResult> points)
    {
        var firstWeek = points[0].Week;
        var lastWeek = points[^1].Week;
        var totalDays = Math.Max((lastWeek - firstWeek).TotalDays, 1);

        var minCount = points.Min(p => p.Count!.Value);
        var maxCount = points.Max(p => p.Count!.Value);
        var (yMin, yMax) = GetPaddedRange(minCount, maxCount);

        var latest = points[^1];
        var latestCoordinates = GetCoordinates(latest, firstWeek, totalDays, yMin, yMax);

        return CreateGrid(yMin, yMax, points)
            .Append(CreateElement("path",
                ("d", BuildPath(points, firstWeek, totalDays, yMin, yMax)),
                ("fill", "none"),
                ("stroke", AccentFill),
                ("stroke-width", "3"),
                ("stroke-linecap", "round"),
                ("stroke-linejoin", "round")))
            .Append(CreateCircle(latestCoordinates.X, latestCoordinates.Y, 5, AccentFill, "#ffffff", 2))
            .Append(CreateText(FormatCount(latest.Count!.Value), ChartLeft + ChartWidth, 34, TitleFill, 22,
                anchor: "end", fontWeight: "700"))
            .Append(CreateText("Latest weekly avg/day", ChartLeft + ChartWidth, 54, MutedFill, 13,
                anchor: "end"));
    }

    private static IEnumerable<XElement> CreateGrid(
        double yMin,
        double yMax,
        IReadOnlyList<DailyDownloadResult>? points)
    {
        var elements = new List<XElement>();

        for (var i = 0; i <= 4; i++)
        {
            var ratio = i / 4d;
            var y = ChartTop + ChartHeight - ratio * ChartHeight;
            var value = yMin + ratio * (yMax - yMin);

            elements.Add(CreateLine(ChartLeft, y, ChartLeft + ChartWidth, y, GridStroke, 1));
            elements.Add(CreateText(FormatCount((long)Math.Round(value)), ChartLeft - 10, y + 4, MutedFill, 12,
                anchor: "end"));
        }

        if (points is null || points.Count == 0)
        {
            return elements;
        }

        var start = points[0].Week;
        var end = points[^1].Week;
        var middle = start.AddDays((end - start).TotalDays / 2);

        elements.Add(CreateXLabel(ChartLeft, start, "start"));
        if (end.Month != start.Month || end.Year != start.Year)
        {
            elements.Add(CreateXLabel(ChartLeft + ChartWidth / 2, middle, "middle"));
        }

        elements.Add(CreateXLabel(ChartLeft + ChartWidth, end, "end"));
        return elements;
    }

    private static XElement CreateXLabel(double x, DateTime date, string anchor)
    {
        return CreateText(date.ToString("MMM yyyy", CultureInfo.InvariantCulture),
            x, ChartTop + ChartHeight + 24, MutedFill, 12, anchor);
    }

    private static string BuildPath(
        IReadOnlyList<DailyDownloadResult> points,
        DateTime firstWeek,
        double totalDays,
        double yMin,
        double yMax)
    {
        var builder = new StringBuilder();

        for (var i = 0; i < points.Count; i++)
        {
            var coordinates = GetCoordinates(points[i], firstWeek, totalDays, yMin, yMax);
            builder.Append(i == 0 ? "M " : " L ");
            builder.Append(Format(coordinates.X));
            builder.Append(' ');
            builder.Append(Format(coordinates.Y));
        }

        return builder.ToString();
    }

    private static (double X, double Y) GetCoordinates(
        DailyDownloadResult point,
        DateTime firstWeek,
        double totalDays,
        double yMin,
        double yMax)
    {
        var x = totalDays == 0
            ? ChartLeft + ChartWidth / 2
            : ChartLeft + ((point.Week - firstWeek).TotalDays / totalDays) * ChartWidth;
        var y = ChartTop + ChartHeight - ((point.Count!.Value - yMin) / (yMax - yMin)) * ChartHeight;
        return (x, y);
    }

    private static (double Min, double Max) GetPaddedRange(long min, long max)
    {
        if (min == max)
        {
            var padding = Math.Max(1, max * 0.1);
            return (Math.Max(0, min - padding), max + padding);
        }

        var spread = max - min;
        var paddingAmount = Math.Max(1, spread * 0.08);
        return (Math.Max(0, min - paddingAmount), max + paddingAmount);
    }

    private static XElement CreateElement(
        string name,
        params (string Name, string Value)[] attributes)
    {
        return new XElement(SvgNamespace + name,
            attributes.Select(a => new XAttribute(a.Name, a.Value)));
    }

    private static XElement CreateElement(
        string name,
        string content,
        params (string Name, string Value)[] attributes)
    {
        return new XElement(SvgNamespace + name,
            attributes.Select(a => new XAttribute(a.Name, a.Value)),
            content);
    }

    private static XElement CreateRect(
        double x,
        double y,
        double width,
        double height,
        string fill,
        string stroke,
        double? rx = null)
    {
        var attributes = new List<(string Name, string Value)>
        {
            ("x", Format(x)),
            ("y", Format(y)),
            ("width", Format(width)),
            ("height", Format(height)),
            ("fill", fill),
            ("stroke", stroke)
        };

        if (rx.HasValue)
        {
            attributes.Add(("rx", Format(rx.Value)));
        }

        return CreateElement("rect", attributes.ToArray());
    }

    private static XElement CreateLine(
        double x1,
        double y1,
        double x2,
        double y2,
        string stroke,
        double strokeWidth)
    {
        return CreateElement("line",
            ("x1", Format(x1)),
            ("y1", Format(y1)),
            ("x2", Format(x2)),
            ("y2", Format(y2)),
            ("stroke", stroke),
            ("stroke-width", Format(strokeWidth)));
    }

    private static XElement CreateCircle(
        double cx,
        double cy,
        double radius,
        string fill,
        string stroke,
        double strokeWidth)
    {
        return CreateElement("circle",
            ("cx", Format(cx)),
            ("cy", Format(cy)),
            ("r", Format(radius)),
            ("fill", fill),
            ("stroke", stroke),
            ("stroke-width", Format(strokeWidth)));
    }

    private static XElement CreateText(
        string content,
        double x,
        double y,
        string fill,
        int fontSize,
        string? anchor = null,
        string? fontWeight = null)
    {
        var attributes = new List<(string Name, string Value)>
        {
            ("x", Format(x)),
            ("y", Format(y)),
            ("fill", fill),
            ("font-size", fontSize.ToString(CultureInfo.InvariantCulture)),
            ("font-family", FontFamily)
        };

        if (anchor is not null)
        {
            attributes.Add(("text-anchor", anchor));
        }

        if (fontWeight is not null)
        {
            attributes.Add(("font-weight", fontWeight));
        }

        return CreateElement("text", content, attributes.ToArray());
    }

    private static XElement CreateStop(string offset, string stopColor)
    {
        return CreateElement("stop",
            ("offset", offset),
            ("stop-color", stopColor));
    }

    private static string Format(double value)
    {
        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private static string FormatCount(long value)
    {
        if (value >= 1_000_000_000)
        {
            return $"{TrimTrailingZero(value / 1_000_000_000d)}B";
        }

        if (value >= 1_000_000)
        {
            return $"{TrimTrailingZero(value / 1_000_000d)}M";
        }

        if (value >= 1_000)
        {
            return $"{TrimTrailingZero(value / 1_000d)}K";
        }

        return value.ToString("N0", CultureInfo.InvariantCulture);
    }

    private static string TrimTrailingZero(double value)
    {
        return value.ToString("0.#", CultureInfo.InvariantCulture);
    }

    private sealed class Utf8StringWriter : StringWriter
    {
        public override Encoding Encoding => Encoding.UTF8;
    }
}
