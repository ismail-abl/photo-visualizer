using PhotoApp.Core.Models;
using PhotoApp.Core.Normalization;
using PhotoApp.Core.Profiles;

namespace PhotoApp.Tests;

public sealed class AmbiguityResolverTests
{
    private static AmbiguityInput Input(string folder, string fileName, params DateCandidate[] others) =>
        new(folder, ProfileMatcher.Read(fileName, null, null), others);

    private static DateCandidate FileDate(int year, int month, int day) =>
        new(new DateTime(year, month, day, 10, 0, 0), DatePrecision.Second, CandidateOrigin.FileModified);

    [Fact]
    public void NeighbourReadBySameMotif_SettlesTheOrder()
    {
        var result = AmbiguityResolver.Resolve(
        [
            Input("/a", "25-12-2024 10-00-00.jpg"),    // 25 > 12 : jour en premier
            Input("/a", "03-04-2025 10-00-00.jpg")
        ]);

        Assert.Equal(new DateTime(2025, 4, 3, 10, 0, 0), result[1].Date?.WallClock);
        Assert.Null(result[1].Ambiguous);
    }

    [Fact]
    public void NeighboursInAnotherFolder_DoNotCount()
    {
        var result = AmbiguityResolver.Resolve([Input("/a", "25-12-2024 10-00-00.jpg"), Input("/b", "03-04-2025 10-00-00.jpg")]);

        Assert.NotNull(result[1].Ambiguous);
        Assert.Null(result[1].Date);
    }

    [Fact]
    public void ConflictingNeighbours_LeaveItAmbiguous()
    {
        var result = AmbiguityResolver.Resolve(
        [
            Input("/a", "25-12-2024 10-00-00.jpg"),    // jour en premier
            Input("/a", "12-25-2024 10-00-00.jpg"),    // mois en premier
            Input("/a", "03-04-2025 10-00-00.jpg")
        ]);

        Assert.NotNull(result[2].Ambiguous);
    }

    [Fact]
    public void AnotherCandidateOnTheSameDay_SettlesIt()
    {
        var result = AmbiguityResolver.Resolve([Input("/a", "03-04-2025 10-00-00.jpg", FileDate(2025, 3, 4))]);

        Assert.Equal(new DateTime(2025, 3, 4, 10, 0, 0), result[0].Date?.WallClock);
    }

    [Fact]
    public void OtherCandidatesMatchingNeitherReading_LeaveItAmbiguous()
    {
        var result = AmbiguityResolver.Resolve([Input("/a", "03-04-2025 10-00-00.jpg", FileDate(2026, 1, 1))]);

        Assert.NotNull(result[0].Ambiguous);
    }

    [Fact]
    public void UnambiguousReadings_AreUntouched()
    {
        var input = Input("/a", "IMG-20211029-WA0039.jpg");

        Assert.Same(input.Reading, AmbiguityResolver.Resolve([input])[0]);
    }
}
