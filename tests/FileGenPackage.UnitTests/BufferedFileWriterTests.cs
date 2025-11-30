using Xunit;
using Microsoft.Extensions.Logging;
using Moq;
using FileGenPackage.Infrastructure;

namespace FileGenPackage.UnitTests;

/// <summary>
/// Unit tests for BufferedFileWriter with atomic header updates.
/// </summary>
public class BufferedFileWriterTests : IDisposable
{
    private readonly string _testDir;
    private readonly ILogger _logger = new Mock<ILogger>().Object;

    public BufferedFileWriterTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"filegen-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, true);
    }

    [Fact]
    public async Task WriteHeaderAsync_ShouldCreateHeaderLine()
    {
        // Arrange
        var filePath = Path.Combine(_testDir, "test.txt");
        var writer = new BufferedFileWriter(filePath, "test-file", _logger);

        try
        {
            // Act
            await writer.WriteHeaderAsync(5, 50000);

            // Assert
            Assert.True(File.Exists(filePath));
            var lines = File.ReadAllLines(filePath);
            Assert.Equal("5,50000", lines[0]);
        }
        finally
        {
            await writer.DisposeAsync();
        }
    }

    [Fact]
    public async Task AppendLinesAsync_ShouldAddContent()
    {
        // Arrange
        var filePath = Path.Combine(_testDir, "test.txt");
        var writer = new BufferedFileWriter(filePath, "test-file", _logger);

        try
        {
            // Act
            await writer.WriteHeaderAsync(0, 0);
            await writer.AppendLinesAsync(new[] { "line1\n", "line2\n", "line3\n" });
            await writer.CloseAsync();

            // Assert
            var lines = File.ReadAllLines(filePath);
            Assert.Equal(4, lines.Length);
            Assert.Equal("0,0", lines[0]);
            Assert.Equal("line1", lines[1]);
            Assert.Equal("line2", lines[2]);
            Assert.Equal("line3", lines[3]);
        }
        finally
        {
            await writer.DisposeAsync();
        }
    }

    [Fact]
    public async Task WriteHeaderAsync_ShouldUpdateExistingHeader()
    {
        // Arrange
        var filePath = Path.Combine(_testDir, "test.txt");
        var writer = new BufferedFileWriter(filePath, "test-file", _logger);

        try
        {
            // Act
            await writer.WriteHeaderAsync(0, 10000);
            await writer.AppendLinesAsync(new[] { "data1\n", "data2\n" });
            await writer.CloseAsync();

            var writer2 = new BufferedFileWriter(filePath, "test-file", _logger);
            await writer2.WriteHeaderAsync(1, 20000);
            await writer2.CloseAsync();

            // Assert
            var lines = File.ReadAllLines(filePath);
            Assert.Equal("1,20000", lines[0]);
            Assert.Equal("data1", lines[1]);
            Assert.Equal("data2", lines[2]);
        }
        finally
        {
            await writer.DisposeAsync();
        }
    }

    [Fact]
    public async Task RemoveHeaderAsync_ShouldRemoveFirstLine()
    {
        // Arrange
        var filePath = Path.Combine(_testDir, "test.txt");
        var writer = new BufferedFileWriter(filePath, "test-file", _logger);

        try
        {
            // Act
            await writer.WriteHeaderAsync(5, 50000);
            await writer.AppendLinesAsync(new[] { "data1\n", "data2\n" });
            await writer.CloseAsync();

            var writer2 = new BufferedFileWriter(filePath, "test-file", _logger);
            await writer2.RemoveHeaderAsync();
            await writer2.CloseAsync();

            // Assert
            var lines = File.ReadAllLines(filePath);
            Assert.Equal(2, lines.Length);
            Assert.Equal("data1", lines[0]);
            Assert.Equal("data2", lines[1]);
        }
        finally
        {
            await writer.DisposeAsync();
        }
    }

    [Fact]
    public async Task RemoveHeaderAsync_OnlyHeader_ShouldDeleteFile()
    {
        // Arrange
        var filePath = Path.Combine(_testDir, "test.txt");
        var writer = new BufferedFileWriter(filePath, "test-file", _logger);

        try
        {
            // Act
            await writer.WriteHeaderAsync(0, 0);
            await writer.CloseAsync();

            var writer2 = new BufferedFileWriter(filePath, "test-file", _logger);
            await writer2.RemoveHeaderAsync();
            await writer2.CloseAsync();

            // Assert
            Assert.False(File.Exists(filePath));
        }
        finally
        {
            await writer.DisposeAsync();
        }
    }

    [Fact]
    public void ReadHeader_ValidHeader_ShouldParse()
    {
        // Arrange
        var filePath = Path.Combine(_testDir, "test.txt");
        File.WriteAllLines(filePath, new[] { "5,50000", "data1", "data2" });

        // Act
        var (page, rows) = BufferedFileWriter.ReadHeader(filePath);

        // Assert
        Assert.Equal(5, page);
        Assert.Equal(50000L, rows);
    }

    [Fact]
    public void ReadHeader_NoFile_ShouldReturn0()
    {
        // Act
        var (page, rows) = BufferedFileWriter.ReadHeader("/nonexistent/file.txt");

        // Assert
        Assert.Equal(0, page);
        Assert.Equal(0L, rows);
    }

    [Fact]
    public void ReadHeader_InvalidFormat_ShouldReturn0()
    {
        // Arrange
        var filePath = Path.Combine(_testDir, "test.txt");
        File.WriteAllText(filePath, "invalid header\ndata");

        // Act
        var (page, rows) = BufferedFileWriter.ReadHeader(filePath);

        // Assert
        Assert.Equal(0, page);
        Assert.Equal(0L, rows);
    }
}
