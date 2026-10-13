using SqlHistory.Core.Models;
using SqlHistory.Core.Versioning;

namespace SqlHistory.Core.Tests;

public class StoredProcedureChangeDetectorTests
{
    [Fact]
    public void Detect_ShouldReturnNew_WhenPreviousSnapshotIsNull()
    {
        var detector = new StoredProcedureChangeDetector();

        var current = CreateSnapshot("ABC");

        var result = detector.Detect(null, current);

        Assert.Equal(ProcedureChangeStatus.New, result);
    }
    
    
    [Fact]
    public void Detect_ShouldReturnUnchanged_WhenHashesAreEqual()
    {
        // Arrange
        var detector = new StoredProcedureChangeDetector();

        var previous = CreateSnapshot("ABC");
        var current = CreateSnapshot("ABC");

        // Act
        var result = detector.Detect(previous, current);

        // Assert
        Assert.Equal(
            ProcedureChangeStatus.Unchanged,
            result);
    }
    
    
    [Fact]
    public void Detect_ShouldReturnModified_WhenHashesAreDifferent()
    {
        // Arrange
        var detector = new StoredProcedureChangeDetector();

        var previous = CreateSnapshot("ABC");
        var current = CreateSnapshot("XYZ");

        // Act
        var result = detector.Detect(previous, current);

        // Assert
        Assert.Equal(
            ProcedureChangeStatus.Modified,
            result);
    }
    
    
    [Fact]
    public void Detect_ShouldIgnoreModifiedAt_WhenHashesAreEqual()
    {
        // Arrange
        var detector = new StoredProcedureChangeDetector();

        var previous = CreateSnapshot("ABC") with
        {
            ModifiedAt = new DateTime(2026, 1, 1)
        };

        var current = CreateSnapshot("ABC") with
        {
            ModifiedAt = new DateTime(2026, 10, 13)
        };

        // Act
        var result = detector.Detect(previous, current);

        // Assert
        Assert.Equal(
            ProcedureChangeStatus.Unchanged,
            result);
    }
    
    
    
    [Fact]
    public void Create_ShouldUseTimeProviderForCapturedAtUtc()
    {
        // Arrange
        var expectedTime =
            new DateTimeOffset(
                2026, 10, 13,
                12, 0, 0,
                TimeSpan.Zero);

        var timeProvider =
            new TestTimeProvider(expectedTime);

        var factory =
            new StoredProcedureSnapshotFactory(timeProvider);

        var definition = CreateDefinition("SELECT 1;");

        // Act
        var snapshot = factory.Create(definition);

        // Assert
        Assert.Equal(
            expectedTime.UtcDateTime,
            snapshot.CapturedAtUtc);
    }
    
    [Fact]
    public void Create_ShouldProduceSameHash_ForSameDefinition()
    {
        // Arrange
        var timeProvider =
            new TestTimeProvider(DateTimeOffset.UtcNow);

        var factory =
            new StoredProcedureSnapshotFactory(timeProvider);

        var first =
            CreateDefinition("SELECT 1;");

        var second =
            CreateDefinition("SELECT 1;");

        // Act
        var firstSnapshot = factory.Create(first);
        var secondSnapshot = factory.Create(second);

        // Assert
        Assert.Equal(
            firstSnapshot.DefinitionHash,
            secondSnapshot.DefinitionHash);
    }
    
    private static StoredProcedureSnapshot CreateSnapshot(string hash)
    {
        return new StoredProcedureSnapshot
        {
            ObjectId = 1,
            SchemaName = "dbo",
            ProcedureName = "GetUsers",
            Definition = "SELECT 1;",
            DefinitionHash = hash,
            CreatedAt = DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow,
            CapturedAtUtc = DateTime.UtcNow
        };
    }
    
    [Fact]
    public void Create_ShouldProduceDifferentHash_WhenDefinitionChanges()
    {
        // Arrange
        var factory =
            new StoredProcedureSnapshotFactory(
                new TestTimeProvider(DateTimeOffset.UtcNow));

        var first =
            CreateDefinition("SELECT 1;");

        var second =
            CreateDefinition("SELECT 2;");

        // Act
        var firstSnapshot = factory.Create(first);
        var secondSnapshot = factory.Create(second);

        // Assert
        Assert.NotEqual(
            firstSnapshot.DefinitionHash,
            secondSnapshot.DefinitionHash);
    }
    
    [Fact]
    public void Create_ShouldProduceSameHash_ForCrLfAndLfDefinitions()
    {
        // Arrange
        var factory =
            new StoredProcedureSnapshotFactory(
                new TestTimeProvider(DateTimeOffset.UtcNow));

        var windows =
            CreateDefinition(
                "SELECT 1;\r\nSELECT 2;");

        var unix =
            CreateDefinition(
                "SELECT 1;\nSELECT 2;");

        // Act
        var windowsSnapshot =
            factory.Create(windows);

        var unixSnapshot =
            factory.Create(unix);

        // Assert
        Assert.Equal(
            windowsSnapshot.DefinitionHash,
            unixSnapshot.DefinitionHash);
    }

    private static StoredProcedureDefinition CreateDefinition(string? definition)
    {
        return new StoredProcedureDefinition
        {
            ObjectId = 1,
            SchemaName = "dbo",
            ProcedureName = "GetUsers",
            Definition = definition,
            CreatedAt = DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow,
        };
    }
}