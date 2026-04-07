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
                CreateElement("rect",
                    ("x", "0"),
                    ("y", "0"),
                    ("width", Width.ToString(CultureInfo.InvariantCulture)),
                    ("height", Height.ToString(CultureInfo.InvariantCulture)),
                    ("rx", "18"),
                    ("fill", "url(#chart-bg)"),
                    ("stroke", "#dbe4f0")),
                CreateElement("text", packageId,
                    ("x", Format(ChartLeft)),
                    ("y", "34"),
                    ("fill", "#0f172a"),
                    ("font-size", "24"),
                    ("font-family", "system-ui, sans-serif"),
                    ("font-weight", "700")),
                CreateElement("text", chartTitle,
                    ("x", Format(ChartLeft)),
                    ("y", "54"),
                    ("fill", "#64748b"),
                    ("font-size", "13"),
                    ("font-family", "system-ui, sans-serif")),
                CreateElement("rect",
                    ("x", Format(ChartLeft)),
                    ("y", Format(ChartTop)),
                    ("width", Format(ChartWidth)),
                    ("height", Format(ChartHeight)),
                    ("rx", "12"),
                    ("fill", "#ffffff"),
                    ("stroke", "#e2e8f0")),
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
                CreateElement("stop",
                    ("offset", "0%"),
                    ("stop-color", "#ffffff")),
                CreateElement("stop",
                    ("offset", "100%"),
                    ("stop-color", "#f8fafc"))));
    }

    private static IEnumerable<XElement> CreateEmptyState()
    {
        var centerX = ChartLeft + ChartWidth / 2;
        var centerY = ChartTop + ChartHeight / 2;

        return CreateGrid(0, 4_000, null)
            .Append(CreateElement("text", "No download history yet",
                ("x", Format(centerX)),
                ("y", Format(centerY - 8)),
                ("text-anchor", "middle"),
                ("fill", "#334155"),
                ("font-size", "18"),
                ("font-family", "system-ui, sans-serif"),
                ("font-weight", "600")))
            .Append(CreateElement("text",
                "The package exists, but NuGet Trends has not collected weekly data for it yet.",
                ("x", Format(centerX)),
                ("y", Format(centerY + 16)),
                ("text-anchor", "middle"),
                ("fill", "#64748b"),
                ("font-size", "13"),
                ("font-family", "system-ui, sans-serif")));
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
                ("stroke", "#2563eb"),
                ("stroke-width", "3"),
                ("stroke-linecap", "round"),
                ("stroke-linejoin", "round")))
            .Append(CreateElement("circle",
                ("cx", Format(latestCoordinates.X)),
                ("cy", Format(latestCoordinates.Y)),
                ("r", "5"),
                ("fill", "#2563eb"),
                ("stroke", "#ffffff"),
                ("stroke-width", "2")))
            .Append(CreateElement("text", FormatCount(latest.Count!.Value),
                ("x", Format(ChartLeft + ChartWidth)),
                ("y", "34"),
                ("text-anchor", "end"),
                ("fill", "#0f172a"),
                ("font-size", "22"),
                ("font-family", "system-ui, sans-serif"),
                ("font-weight", "700")))
            .Append(CreateElement("text", "Latest weekly avg/day",
                ("x", Format(ChartLeft + ChartWidth)),
                ("y", "54"),
                ("text-anchor", "end"),
                ("fill", "#64748b"),
                ("font-size", "13"),
                ("font-family", "system-ui, sans-serif")));
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

            elements.Add(CreateElement("line",
                ("x1", Format(ChartLeft)),
                ("y1", Format(y)),
                ("x2", Format(ChartLeft + ChartWidth)),
                ("y2", Format(y)),
                ("stroke", "#e2e8f0"),
                ("stroke-width", "1")));
            elements.Add(CreateElement("text", FormatCount((long)Math.Round(value)),
                ("x", Format(ChartLeft - 10)),
                ("y", Format(y + 4)),
                ("text-anchor", "end"),
                ("fill", "#64748b"),
                ("font-size", "12"),
                ("font-family", "system-ui, sans-serif")));
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
        return CreateElement("text", date.ToString("MMM yyyy", CultureInfo.InvariantCulture),
            ("x", Format(x)),
            ("y", Format(ChartTop + ChartHeight + 24)),
            ("text-anchor", anchor),
            ("fill", "#64748b"),
            ("font-size", "12"),
            ("font-family", "system-ui, sans-serif"));
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
