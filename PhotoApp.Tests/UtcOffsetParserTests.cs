using PhotoApp.Core.Services;

namespace PhotoApp.Tests;

public sealed class UtcOffsetParserTests
{
    [Theory]
    [InlineData("+09:00", 9, 0)]
    [InlineData("-03:30", -3, -30)]
    [InlineData("+0200", 2, 0)]
    [InlineData("Z", 0, 0)]
    [InlineData(" +01:00 ", 1, 0)]
    public void TryParse_AcceptsValidOffsets(string text, int hours, int minutes)
    {
        Assert.True(UtcOffsetParser.TryParse(text, out var offset));
        Assert.Equal(new TimeSpan(hours, minutes, 0), offset);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Paris")]
    [InlineData("+25:00")]
    [InlineData("+01:75")]
    public void TryParse_RejectsInvalidOffsets(string? text)
    {
        Assert.False(UtcOffsetParser.TryParse(text, out _));
    }
}
