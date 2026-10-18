using PhotoApp.Core.Models;
using PhotoApp.Core.Services;

namespace PhotoApp.Tests;

public sealed class RenamePlannerTests
{
    private static readonly IReadOnlyDictionary<string, PreviousOutput> NoPrevious = new Dictionary<string, PreviousOutput>();

    private static PlanInput Input(string path, DateTime wallClock, bool hasTime = true, long size = 10, long ticks = 1) =>
        new(path, new ImageDateCandidate(wallClock, hasTime), size, ticks);

    [Fact]
    public void PlanRenames_SameSecond_IsOrderedByFileName_NotByInputOrder()
    {
        var when = new DateTime(2024, 1, 5, 15, 4, 12);
        var inputs = new[] { Input("/p/b.png", when), Input("/p/a.png", when) };

        var plans = new RenamePlanner().PlanRenames(inputs, NoPrevious, []);

        Assert.Equal("IMG-20240105-150412.jpg", plans.Single(p => p.SourcePath == "/p/a.png").TargetName);
        Assert.Equal("IMG-20240105-150412-01.jpg", plans.Single(p => p.SourcePath == "/p/b.png").TargetName);
    }

    [Fact]
    public void PlanRenames_IsDeterministic_RegardlessOfInputOrder()
    {
        var when = new DateTime(2024, 1, 5, 15, 4, 12);
        var a = new[] { Input("/p/a.png", when), Input("/p/b.png", when), Input("/p/c.png", when) };
        var b = a.Reverse().ToArray();
        var planner = new RenamePlanner();

        var first = planner.PlanRenames(a, NoPrevious, []).ToDictionary(p => p.SourcePath, p => p.TargetName);
        var second = planner.PlanRenames(b, NoPrevious, []).ToDictionary(p => p.SourcePath, p => p.TargetName);

        Assert.Equal(first, second);
    }

    [Fact]
    public void PlanRenames_AlreadyDoneSource_KeepsItsNameAndIsNotRenumbered()
    {
        var when = new DateTime(2024, 1, 5, 15, 4, 12);
        var previous = new Dictionary<string, PreviousOutput> { ["/p/b.png"] = new(10, 1, "IMG-20240105-150412.jpg") };
        var inputs = new[] { Input("/p/a.png", when), Input("/p/b.png", when) };

        var plans = new RenamePlanner().PlanRenames(inputs, previous, ["IMG-20240105-150412.jpg"]);

        var b = plans.Single(p => p.SourcePath == "/p/b.png");
        Assert.True(b.AlreadyDone);
        Assert.Equal("IMG-20240105-150412.jpg", b.TargetName);
        var a = plans.Single(p => p.SourcePath == "/p/a.png");
        Assert.False(a.AlreadyDone);
        Assert.Equal("IMG-20240105-150412-01.jpg", a.TargetName);
    }

    [Fact]
    public void PlanRenames_SourceChangedSinceLastRun_IsPlannedAgainUnderANewName()
    {
        var when = new DateTime(2024, 1, 5, 15, 4, 12);
        var previous = new Dictionary<string, PreviousOutput> { ["/p/a.png"] = new(10, 1, "IMG-20240105-150412.jpg") };
        var inputs = new[] { Input("/p/a.png", when, size: 99) };

        var plan = new RenamePlanner().PlanRenames(inputs, previous, ["IMG-20240105-150412.jpg"]).Single();

        Assert.False(plan.AlreadyDone);
        Assert.Equal("IMG-20240105-150412-01.jpg", plan.TargetName);
    }

    [Fact]
    public void PlanRenames_SecondRunWithSameInputs_IsEntirelyAlreadyDone()
    {
        var when = new DateTime(2024, 1, 5, 15, 4, 12);
        var inputs = new[] { Input("/p/a.png", when), Input("/p/b.png", when), Input("/p/c.png", new DateTime(2024, 1, 5), hasTime: false) };
        var planner = new RenamePlanner();
        var firstRun = planner.PlanRenames(inputs, NoPrevious, []);
        var previous = firstRun.ToDictionary(p => p.SourcePath, p => new PreviousOutput(p.SizeBytes, p.LastWriteUtcTicks, p.TargetName));

        var secondRun = planner.PlanRenames(inputs, previous, firstRun.Select(p => p.TargetName));

        Assert.All(secondRun, p => Assert.True(p.AlreadyDone));
        Assert.Equal(
            firstRun.ToDictionary(p => p.SourcePath, p => p.TargetName),
            secondRun.ToDictionary(p => p.SourcePath, p => p.TargetName));
    }

    [Fact]
    public void PlanRenames_AlwaysProducesTheOutputExtension()
    {
        var plan = new RenamePlanner().PlanRenames([Input("/p/photo.PNG", new DateTime(2024, 1, 5, 15, 4, 12))], NoPrevious, []).Single();

        Assert.EndsWith(".jpg", plan.TargetName);
    }
}
