using SqlHistory.Core.Models;
using SqlHistory.Hackathon.Api;

namespace SqlHistory.Hackathon.Tests;

public class RoutineIdTests
{
    [Fact]
    public void Encode_ShouldRoundTrip_AndBeUrlSafe()
    {
        var identity = new StoredProcedureIdentity(SourceProviders.PostgreSql, "My Schema", "calc/total",
            RoutineType.Function, "integer, timestamp with time zone");

        var id = RoutineId.Encode(identity);

        Assert.Matches("^[A-Za-z0-9_-]+$", id);
        Assert.True(RoutineId.TryDecode(id, out var decoded));
        Assert.Equal(identity, decoded);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not base64!")]
    [InlineData("bm90IGpzb24")] // "not json"
    public void TryDecode_ShouldRejectGarbage(string id)
    {
        Assert.False(RoutineId.TryDecode(id, out _));
    }

    [Fact]
    public void TryDecode_ShouldRejectOtherProviders()
    {
        var id = RoutineId.Encode(new StoredProcedureIdentity(SourceProviders.SqlServer, "dbo", "p",
            RoutineType.Procedure, ""));

        Assert.False(RoutineId.TryDecode(id, out _));
    }
}
