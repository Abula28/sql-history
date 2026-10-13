using SqlHistory.Core.Sql;

namespace SqlHistory.Core.Tests;

public class SqlDefinitionNormalizerTests
{
    [Fact]
    public void Normalize_ShouldReturnEmptyString_WhenDefinitionIsNull()
    {
        string? definition = null;
        
        var result = SqlDefinitionNormalizer.Normalize(definition);

        Assert.Equal(string.Empty, result);
    }
    
    [Fact]
    public void Normalize_ShouldConvertCrLfToLf()
    {
        // Arrange
        var definition = "SELECT 1;\r\nSELECT 2;";

        // Act
        var result = SqlDefinitionNormalizer.Normalize(definition);

        // Assert
        Assert.Equal("SELECT 1;\nSELECT 2;", result);
    }
    
    [Fact]
    public void Normalize_ShouldConvertCarriageReturnToLf()
    {
        // Arrange
        var definition = "SELECT 1;\rSELECT 2;";

        // Act
        var result = SqlDefinitionNormalizer.Normalize(definition);

        // Assert
        Assert.Equal("SELECT 1;\nSELECT 2;", result);
    }
    
    [Fact]
    public void Normalize_ShouldTrimTrailingWhitespace()
    {
        // Arrange
        var definition = "SELECT 1;\n\n   ";

        // Act
        var result = SqlDefinitionNormalizer.Normalize(definition);

        // Assert
        Assert.Equal("SELECT 1;", result);
    }
    
    
    [Fact]
    public void Normalize_ShouldRemoveBomFromBeginning()
    {
        // Arrange
        var definition = "\uFEFFSELECT 1;";

        // Act
        var result = SqlDefinitionNormalizer.Normalize(definition);

        // Assert
        Assert.Equal("SELECT 1;", result);
    }
}