using FluentAssertions;
using NuGetTrends.Data;
using Xunit;

namespace NuGetTrends.Web.Tests;

public class PackageTrendSvgRendererTests
{
    [Fact]
    public void Render_WithData_ProducesSvgPathAndLatestValue()
    {
        var downloads = new List<DailyDownloadResult>
        {
            new() { Week = new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc), Count = 1200 },
            new() { Week = new DateTime(2026, 1, 12, 0, 0, 0, DateTimeKind.Utc), Count = 1800 },
            new() { Week = new DateTime(2026, 1, 19, 0, 0, 0, DateTimeKind.Utc), Count = 2400 }
        };

        var svg = PackageTrendSvgRenderer.Render("CodeFormatter.DevToys", downloads, months: 6);

        svg.Should().Contain("<svg");
        svg.Should().Contain("CodeFormatter.DevToys");
        svg.Should().Contain("<path d=\"M ");
        svg.Should().Contain("2.4K");
        svg.Should().Contain("Latest weekly avg/day");
    }

    [Fact]
    public void Render_WithoutData_ProducesEmptyStateSvg()
    {
        var svg = PackageTrendSvgRenderer.Render("CodeFormatter.DevToys", [], months: 6);

        svg.Should().Contain("<svg");
        svg.Should().Contain("No download history yet");
        svg.Should().Contain("The package exists, but NuGet Trends has not collected weekly data for it yet.");
    }

    [Fact]
    public void Render_EscapesPackageIdInSvgText()
    {
        var svg = PackageTrendSvgRenderer.Render("Foo<Bar>&Baz", [], months: 1);

        svg.Should().Contain("Foo&lt;Bar&gt;&amp;Baz");
        svg.Should().NotContain("Foo<Bar>&Baz");
    }
}
