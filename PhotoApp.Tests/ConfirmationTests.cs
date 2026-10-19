using PhotoApp.Cli;

namespace PhotoApp.Tests;

public sealed class ConfirmationTests
{
    [Theory]
    [InlineData("o", true)]
    [InlineData("OUI", true)]
    [InlineData("y", true)]
    [InlineData("Yes", true)]
    [InlineData(" o ", true)]
    [InlineData("n", false)]
    [InlineData("non", false)]
    [InlineData("", false)]
    [InlineData("peut-être", false)]
    public void Ask_AcceptsOnlyAnExplicitYes(string answer, bool expected)
    {
        var output = new StringWriter();

        Assert.Equal(expected, Confirmation.Ask("Continuer ?", new StringReader(answer + "\n"), output));
        Assert.Contains("Continuer ? [o/N]", output.ToString());
    }

    [Fact]
    public void Ask_EndOfInput_IsNo()
    {
        Assert.False(Confirmation.Ask("Continuer ?", new StringReader(string.Empty), new StringWriter()));
    }
}
