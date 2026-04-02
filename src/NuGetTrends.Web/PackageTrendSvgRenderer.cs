using System.Globalization;
using System.Net;
using System.Text;
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

    public static string Render(string packageId, IReadOnlyList<DailyDownloadResult> downloads, int months)
    {
        var builder = new StringBuilder();
        var escapedPackageId = Escape(packageId);
        var chartTitle = $"Average daily downloads by week, last {months} month{(months == 1 ? "" : "s")}";

        builder.AppendLine("""<?xml version="1.0" encoding="UTF-8"?>""");
        builder.AppendLine(
            $"""<svg xmlns="http://www.w3.org/2000/svg" width="{Width}" height="{Height}" viewBox="0 0 {Width} {Height}" role="img" aria-labelledby="title desc">""");
        builder.AppendLine($"""  <title id="title">{escapedPackageId} download trend</title>""");
        builder.AppendLine(
            $"""  <desc id="desc">NuGet Trends chart for {escapedPackageId}. {Escape(chartTitle)}.</desc>""");
        builder.AppendLine("""  <defs>""");
        builder.AppendLine("""    <linearGradient id="chart-bg" x1="0%" y1="0%" x2="0%" y2="100%">""");
        builder.AppendLine("""      <stop offset="0%" stop-color="#ffffff" />""");
        builder.AppendLine("""      <stop offset="100%" stop-color="#f8fafc" />""");
        builder.AppendLine("""    </linearGradient>""");
        builder.AppendLine("""  </defs>""");
        builder.AppendLine(
            $"""  <rect x="0" y="0" width="{Width}" height="{Height}" rx="18" fill="url(#chart-bg)" stroke="#dbe4f0" />""");
        builder.AppendLine(
            $"""  <text x="{Format(ChartLeft)}" y="34" fill="#0f172a" font-size="24" font-family="system-ui, sans-serif" font-weight="700">{escapedPackageId}</text>""");
        builder.AppendLine(
            $"""  <text x="{Format(ChartLeft)}" y="54" fill="#64748b" font-size="13" font-family="system-ui, sans-serif">{Escape(chartTitle)}</text>""");
        builder.AppendLine(
            $"""  <rect x="{Format(ChartLeft)}" y="{Format(ChartTop)}" width="{Format(ChartWidth)}" height="{Format(ChartHeight)}" rx="12" fill="#ffffff" stroke="#e2e8f0" />""");

        var points = downloads
            .Where(d => d.Count.HasValue)
            .OrderBy(d => d.Week)
            .ToList();

        if (points.Count == 0)
        {
            AppendEmptyState(builder);
        }
        else
        {
            AppendChart(builder, points);
        }

        builder.AppendLine("""</svg>""");
        return builder.ToString();
    }

    private static void AppendEmptyState(StringBuilder builder)
    {
        AppendGrid(builder, 0, 4_000, null);

        var centerX = ChartLeft + ChartWidth / 2;
        var centerY = ChartTop + ChartHeight / 2;

        builder.AppendLine(
            $"""  <text x="{Format(centerX)}" y="{Format(centerY - 8)}" text-anchor="middle" fill="#334155" font-size="18" font-family="system-ui, sans-serif" font-weight="600">No download history yet</text>""");
        builder.AppendLine(
            $"""  <text x="{Format(centerX)}" y="{Format(centerY + 16)}" text-anchor="middle" fill="#64748b" font-size="13" font-family="system-ui, sans-serif">The package exists, but NuGet Trends has not collected weekly data for it yet.</text>""");
    }

    private static void AppendChart(StringBuilder builder, IReadOnlyList<DailyDownloadResult> points)
    {
        var firstWeek = points[0].Week;
        var lastWeek = points[^1].Week;
        var totalDays = Math.Max((lastWeek - firstWeek).TotalDays, 1);

        var minCount = points.Min(p => p.Count!.Value);
        var maxCount = points.Max(p => p.Count!.Value);
        var (yMin, yMax) = GetPaddedRange(minCount, maxCount);

        AppendGrid(builder, yMin, yMax, points);

        var path = new StringBuilder();
        for (var i = 0; i < points.Count; i++)
        {
            var point = points[i];
            var x = points.Count == 1
                ? ChartLeft + ChartWidth / 2
                : ChartLeft + ((point.Week - firstWeek).TotalDays / totalDays) * ChartWidth;
            var y = ChartTop + ChartHeight - ((point.Count!.Value - yMin) / (yMax - yMin)) * ChartHeight;

            path.Append(i == 0 ? "M " : " L ");
            path.Append(Format(x));
            path.Append(' ');
            path.Append(Format(y));
        }

        var latest = points[^1];
        var latestX = points.Count == 1
            ? ChartLeft + ChartWidth / 2
            : ChartLeft + ((latest.Week - firstWeek).TotalDays / totalDays) * ChartWidth;
        var latestY = ChartTop + ChartHeight - ((latest.Count!.Value - yMin) / (yMax - yMin)) * ChartHeight;

        builder.AppendLine(
            $"""  <path d="{path}" fill="none" stroke="#2563eb" stroke-width="3" stroke-linecap="round" stroke-linejoin="round" />""");
        builder.AppendLine(
            $"""  <circle cx="{Format(latestX)}" cy="{Format(latestY)}" r="5" fill="#2563eb" stroke="#ffffff" stroke-width="2" />""");
        builder.AppendLine(
            $"""  <text x="{Format(ChartLeft + ChartWidth)}" y="34" text-anchor="end" fill="#0f172a" font-size="22" font-family="system-ui, sans-serif" font-weight="700">{Escape(FormatCount(latest.Count!.Value))}</text>""");
        builder.AppendLine(
            $"""  <text x="{Format(ChartLeft + ChartWidth)}" y="54" text-anchor="end" fill="#64748b" font-size="13" font-family="system-ui, sans-serif">Latest weekly avg/day</text>""");
    }

    private static void AppendGrid(StringBuilder builder, double yMin, double yMax, IReadOnlyList<DailyDownloadResult>? points)
    {
        for (var i = 0; i <= 4; i++)
        {
            var ratio = i / 4d;
            var y = ChartTop + ChartHeight - ratio * ChartHeight;
            var value = yMin + ratio * (yMax - yMin);

            builder.AppendLine(
                $"""  <line x1="{Format(ChartLeft)}" y1="{Format(y)}" x2="{Format(ChartLeft + ChartWidth)}" y2="{Format(y)}" stroke="#e2e8f0" stroke-width="1" />""");
            builder.AppendLine(
                $"""  <text x="{Format(ChartLeft - 10)}" y="{Format(y + 4)}" text-anchor="end" fill="#64748b" font-size="12" font-family="system-ui, sans-serif">{Escape(FormatCount((long)Math.Round(value)))}</text>""");
        }

        if (points is null || points.Count == 0)
        {
            return;
        }

        var start = points[0].Week;
        var end = points[^1].Week;
        var middle = start.AddDays((end - start).TotalDays / 2);

        AppendXLabel(builder, ChartLeft, start, "start");
        if (end.Month != start.Month || end.Year != start.Year)
        {
            AppendXLabel(builder, ChartLeft + ChartWidth / 2, middle, "middle");
        }

        AppendXLabel(builder, ChartLeft + ChartWidth, end, "end");
    }

    private static void AppendXLabel(StringBuilder builder, double x, DateTime date, string anchor)
    {
        builder.AppendLine(
            $"""  <text x="{Format(x)}" y="{Format(ChartTop + ChartHeight + 24)}" text-anchor="{anchor}" fill="#64748b" font-size="12" font-family="system-ui, sans-serif">{Escape(date.ToString("MMM yyyy", CultureInfo.InvariantCulture))}</text>""");
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

    private static string Escape(string value)
    {
        return WebUtility.HtmlEncode(value);
    }
}
