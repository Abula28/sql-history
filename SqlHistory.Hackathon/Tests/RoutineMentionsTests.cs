using SqlHistory.Core.Models;
using SqlHistory.Hackathon.Api.Performance;

namespace SqlHistory.Hackathon.Tests;

public class RoutineMentionsTests
{
    private static readonly StoredProcedureIdentity ActiveUsers =
        new(SourceProviders.PostgreSql, "public", "active_users", RoutineType.Function, "");

    [Theory]
    [InlineData("SELECT public.active_users()")]
    [InlineData("select * from PUBLIC.ACTIVE_USERS ( )")]
    [InlineData("SELECT \"public\".\"active_users\"()")]
    public void Find_ShouldMatchQualifiedCalls(string query)
    {
        Assert.Single(RoutineMentions.Find(query, [ActiveUsers]));
    }

    [Theory]
    [InlineData("SELECT public.active_users_v2()")]
    [InlineData("SELECT my_public.active_users()")]
    [InlineData("SELECT active_users()")] // unqualified: not matched, so absence proves nothing
    [InlineData("SELECT count(*) FROM public.users")]
    public void Find_ShouldNotMatchOtherText(string query)
    {
        Assert.Empty(RoutineMentions.Find(query, [ActiveUsers]));
    }
}
