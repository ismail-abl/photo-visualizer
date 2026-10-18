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
            new("source1.jpg", "target1.jpg", Noon),
            new("source2.jpg", "target2.jpg", Noon)
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
            var plan = new RenamePlan(sourcePath, "target.jpg", Noon);
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
            var plan = new RenamePlan(sourcePath, "target.jpg", Noon);
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
            var plan = new RenamePlan(sourcePath, "target.jpg", Noon);
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
            var plan = new RenamePlan(sourcePath, "target.jpg", Noon);
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
}