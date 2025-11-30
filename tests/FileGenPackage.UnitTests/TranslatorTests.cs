using Xunit;
using FileGenPackage.Abstractions;
using FileGenPackage.Infrastructure;

namespace FileGenPackage.UnitTests;

/// <summary>
/// Unit tests for translator registry and translator interface.
/// </summary>
public class TranslatorTests
{
    [Fact]
    public void Register_AndGetTranslator_ShouldReturnCorrectInstance()
    {
        // Arrange
        var registry = new TranslatorRegistry();
        var translator = new TestTranslator();

        // Act
        registry.Register("test", translator);
        var retrieved = registry.GetTranslator("test");

        // Assert
        Assert.Same(translator, retrieved);
    }

    [Fact]
    public void GetTranslator_NotRegistered_ShouldThrow()
    {
        // Arrange
        var registry = new TranslatorRegistry();

        // Act & Assert
        var ex = Assert.Throws<KeyNotFoundException>(() => registry.GetTranslator("nonexistent"));
        Assert.Contains("nonexistent", ex.Message);
    }

    [Fact]
    public void TranslateBatch_DefaultImplementation_ShouldCallTranslateRow()
    {
        // Arrange
        var translator = new TestTranslator();
        var rows = new[]
        {
            new Dictionary<string, object?> { { "name", "Alice" }, { "age", 30 } },
            new Dictionary<string, object?> { { "name", "Bob" }, { "age", 25 } }
        };

        // Act
        var results = translator.TranslateBatch(rows).ToList();

        // Assert
        Assert.Equal(2, results.Count);
        Assert.Equal("Alice|30", results[0]);
        Assert.Equal("Bob|25", results[1]);
    }

    private class TestTranslator : ITranslator
    {
        public string TranslateRow(IReadOnlyDictionary<string, object?> row)
        {
            var name = row.TryGetValue("name", out var n) ? n?.ToString() ?? "" : "";
            var age = row.TryGetValue("age", out var a) ? a?.ToString() ?? "" : "";
            return $"{name}|{age}";
        }

        public IEnumerable<string> TranslateBatch(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows)
        {
            return rows.Select(TranslateRow);
        }
    }
}
