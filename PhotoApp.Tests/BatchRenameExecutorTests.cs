using Moq;
using PhotoApp.Core.Models;
using PhotoApp.Core.Services;

namespace PhotoApp.Tests;

public sealed class BatchRenameExecutorTests
{
    private static readonly ImageDateCandidate Noon = new(new DateTime(2023, 1, 1, 12, 0, 0), true, TimeSpan.Zero);

    [Fact]
    public void DryRun_ReturnsResultsWithoutExecuting()
    {
        var plans = new List<RenamePlan>
        {
            new("source1.jpg", "target1.jpg", Noon, 0, 0),
            new("source2.jpg", "target2.jpg", Noon, 0, 0)
        };
        var outputFolder = "output";
        var executor = new BatchRenameExecutor(Mock.Of<IImageConverter>(), Mock.Of<IFileTimestampApplier>());

        var results = executor.DryRun(plans, outputFolder);

        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.True(r.Success));
        Assert.All(results, r => Assert.Null(r.ErrorMessage));
    }

    [Fact]
    public void Execute_WithJpegSource_CopiesAndAppliesTimestamps()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        try
        {
            var sourcePath = Path.Combine(tempDir, "source.jpg");
            File.WriteAllText(sourcePath, "fake jpeg");
            var plan = new RenamePlan(sourcePath, "target.jpg", Noon, 0, 0);
            var outputFolder = Path.Combine(tempDir, "output");
            var mockConverter = new Mock<IImageConverter>();
            mockConverter.Setup(c => c.ConvertToJpeg(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>())).Callback<string, string, int>((input, output, q) => File.Copy(input, output));
            var mockApplier = new Mock<IFileTimestampApplier>();
            var executor = new BatchRenameExecutor(mockConverter.Object, mockApplier.Object);

            var results = executor.Execute([plan], outputFolder, 80, false);

            Assert.Single(results);
            Assert.True(results[0].Success);
            mockConverter.Verify(c => c.ConvertToJpeg(It.IsAny<string>(), It.IsAny<string>(), 80), Times.Once);
            mockApplier.Verify(a => a.ApplyTimestamps(It.IsAny<string>(), new DateTimeOffset(2023, 1, 1, 12, 0, 0, TimeSpan.Zero)), Times.Once);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Execute_WithPngSource_ConvertsToJpeg()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        try
        {
            var sourcePath = Path.Combine(tempDir, "source.png");
            File.WriteAllText(sourcePath, "fake png");
            var plan = new RenamePlan(sourcePath, "target.jpg", Noon, 0, 0);
            var outputFolder = Path.Combine(tempDir, "output");
            var mockConverter = new Mock<IImageConverter>();
            mockConverter.Setup(c => c.ConvertToJpeg(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>())).Callback<string, string, int>((input, output, q) =>
            {
                Assert.EndsWith(".png", input, StringComparison.OrdinalIgnoreCase);
                File.Copy(input, output);
            });
            var mockApplier = new Mock<IFileTimestampApplier>();
            var executor = new BatchRenameExecutor(mockConverter.Object, mockApplier.Object);

            var results = executor.Execute([plan], outputFolder, 80, false);

            Assert.Single(results);
            Assert.True(results[0].Success);
            mockConverter.Verify(c => c.ConvertToJpeg(It.IsAny<string>(), It.IsAny<string>(), 80), Times.Once);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Execute_WithDeleteOriginal_DeletesSourceAfterSuccess()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        try
        {
            var sourcePath = Path.Combine(tempDir, "source.jpg");
            File.WriteAllText(sourcePath, "fake jpeg");
            var plan = new RenamePlan(sourcePath, "target.jpg", Noon, 0, 0);
            var outputFolder = Path.Combine(tempDir, "output");
            var mockConverter = new Mock<IImageConverter>();
            mockConverter.Setup(c => c.ConvertToJpeg(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>())).Callback<string, string, int>((input, output, q) => File.Copy(input, output));
            var mockApplier = new Mock<IFileTimestampApplier>();
            var executor = new BatchRenameExecutor(mockConverter.Object, mockApplier.Object);

            var results = executor.Execute([plan], outputFolder, 80, true);

            Assert.Single(results);
            Assert.True(results[0].Success);
            Assert.False(File.Exists(sourcePath));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Execute_OnFailure_ReturnsErrorResult()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        try
        {
            var sourcePath = Path.Combine(tempDir, "source.jpg");
            File.WriteAllText(sourcePath, "fake jpeg");
            var plan = new RenamePlan(sourcePath, "target.jpg", Noon, 0, 0);
            var outputFolder = Path.Combine(tempDir, "output");
            var mockConverter = new Mock<IImageConverter>();
            mockConverter.Setup(c => c.ConvertToJpeg(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>())).Throws(new Exception("Conversion failed"));
            var mockApplier = new Mock<IFileTimestampApplier>();
            var executor = new BatchRenameExecutor(mockConverter.Object, mockApplier.Object);

            var results = executor.Execute([plan], outputFolder, 80, false);

            Assert.Single(results);
            Assert.False(results[0].Success);
            Assert.Contains("Conversion failed", results[0].ErrorMessage);
            Assert.True(File.Exists(sourcePath)); // Original not deleted on failure
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Execute_WhenConversionFailsAfterWritingOutput_LeavesNoOrphanFiles()
    {
        using var temp = new TempDirectory();
        var source = temp.Combine("source.png");
        File.WriteAllText(source, "x");
        var output = temp.Combine("output");
        var converter = new Mock<IImageConverter>();
        converter.Setup(c => c.ConvertToJpeg(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
            .Callback<string, string, int>((_, target, _) =>
            {
                File.WriteAllText(target, "partial");
                throw new IOException("boom");
            });
        var executor = new BatchRenameExecutor(converter.Object, Mock.Of<IFileTimestampApplier>());

        var results = executor.Execute([new RenamePlan(source, "t.jpg", Noon, 1, 1)], output, 80, false);

        Assert.False(results[0].Success);
        Assert.Empty(Directory.GetFiles(output));
        Assert.True(File.Exists(source));
    }

    [Fact]
    public void Execute_WhenTimestampApplierFails_LeavesNoOrphanFiles()
    {
        using var temp = new TempDirectory();
        var source = temp.Combine("source.jpg");
        File.WriteAllText(source, "x");
        var output = temp.Combine("output");
        var converter = new Mock<IImageConverter>();
        converter.Setup(c => c.ConvertToJpeg(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
            .Callback<string, string, int>((input, target, _) => File.Copy(input, target));
        var applier = new Mock<IFileTimestampApplier>();
        applier.Setup(a => a.ApplyTimestamps(It.IsAny<string>(), It.IsAny<DateTimeOffset>())).Throws(new IOException("locked"));
        var executor = new BatchRenameExecutor(converter.Object, applier.Object);

        var results = executor.Execute([new RenamePlan(source, "t.jpg", Noon, 1, 1)], output, 80, false);

        Assert.False(results[0].Success);
        Assert.Empty(Directory.GetFiles(output));
    }

    [Fact]
    public void Execute_AlreadyDonePlan_IsSkippedWithoutTouchingAnything()
    {
        using var temp = new TempDirectory();
        var output = temp.Combine("output");
        var converter = new Mock<IImageConverter>(MockBehavior.Strict);
        var executor = new BatchRenameExecutor(converter.Object, Mock.Of<IFileTimestampApplier>());

        var results = executor.Execute([new RenamePlan("/p/a.png", "t.jpg", Noon, 1, 1, AlreadyDone: true)], output, 80, true);

        Assert.True(results[0].Success);
        Assert.True(results[0].Skipped);
    }

    [Fact]
    public void Execute_Success_RecordsTheSourceInTheManifest()
    {
        using var temp = new TempDirectory();
        var source = temp.Combine("source.jpg");
        File.WriteAllText(source, "x");
        var output = temp.Combine("output");
        var converter = new Mock<IImageConverter>();
        converter.Setup(c => c.ConvertToJpeg(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
            .Callback<string, string, int>((input, target, _) => File.Copy(input, target));
        var manifest = ProcessedManifest.Empty();
        var executor = new BatchRenameExecutor(converter.Object, Mock.Of<IFileTimestampApplier>());

        executor.Execute([new RenamePlan(source, "t.jpg", Noon, 7, 9)], output, 80, false, manifest);

        Assert.Equal(new PreviousOutput(7, 9, "t.jpg"), manifest.ToPrevious()[source]);
    }
}
