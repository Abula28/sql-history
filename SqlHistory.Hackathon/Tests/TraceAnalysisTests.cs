using SqlHistory.Hackathon.Api.Telemetry;
using SqlHistory.Hackathon.Api.Tracing;

namespace SqlHistory.Hackathon.Tests;

public class TraceAnalysisTests
{
    private const string Trace = "0123456789abcdef0123456789abcdef";
    private static readonly DateTime T0 = new(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Classify_ShouldDetectExternalHttpRequest()
    {
        var spans = HttpTrace(parentOfServer: null);

        var caller = TraceAnalysis.Classify(spans);

        Assert.Equal(CallerType.HttpRequest, caller.Type);
        Assert.Equal("GET /api/users", caller.Endpoint);
        Assert.Equal("UsersApi / GET /api/users", caller.Label);
    }

    [Fact]
    public void Classify_ShouldTreatServerSpanWithUnreceivedParent_AsInternalServiceCall()
    {
        var spans = HttpTrace(parentOfServer: "ffffffffffffffff");

        Assert.Equal(CallerType.InternalService, TraceAnalysis.Classify(spans).Type);
    }

    [Fact]
    public void Classify_ShouldPreferTheCallerTypeDeclaredOnTheRoot()
    {
        var spans = new[]
        {
            Span("aaaaaaaaaaaaaaaa", null, "UserSyncWorker.Run", "Internal", declared: "background_job"),
            Span("bbbbbbbbbbbbbbbb", "aaaaaaaaaaaaaaaa", "UsersService.GetAllUsers", "Internal"),
            Span("cccccccccccccccc", "bbbbbbbbbbbbbbbb", "public.get_all_users()", "Client", routine: "get_all_users")
        };

        var caller = TraceAnalysis.Classify(spans);

        Assert.Equal(CallerType.BackgroundJob, caller.Type);
        Assert.Equal("UsersApi / UserSyncWorker.Run", caller.Label);
        Assert.Contains("declared", caller.Source);
    }

    [Fact]
    public void Classify_ShouldReturnUnknown_WhenNothingIdentifiesTheCaller()
    {
        var spans = new[] { Span("aaaaaaaaaaaaaaaa", null, "public.get_all_users()", "Client", routine: "get_all_users") };

        Assert.Equal(CallerType.Unknown, TraceAnalysis.Classify(spans).Type);
        Assert.Equal(CallerType.Unknown, TraceAnalysis.Classify([]).Type);
    }

    [Fact]
    public void PathTo_ShouldFollowParentLinksFromRootToRoutineCall()
    {
        var spans = HttpTrace(parentOfServer: null);
        var dbCall = spans.Single(TraceAnalysis.IsRoutineCall);

        var path = TraceAnalysis.PathTo(dbCall, spans);

        Assert.Equal(["GET /api/users", "UsersController.GetUsers", "UsersService.GetAllUsers", "public.get_all_users()"],
            path.Select(x => x.Name));
    }

    [Fact]
    public void PathTo_ShouldStopAtMissingParentsAndCycles()
    {
        var spans = new[]
        {
            Span("aaaaaaaaaaaaaaaa", "bbbbbbbbbbbbbbbb", "a", "Internal"),
            Span("bbbbbbbbbbbbbbbb", "aaaaaaaaaaaaaaaa", "b", "Internal"),
            Span("cccccccccccccccc", "9999999999999999", "orphan", "Client")
        };

        Assert.Equal(2, TraceAnalysis.PathTo(spans[0], spans).Count);
        Assert.Single(TraceAnalysis.PathTo(spans[2], spans));
    }

    [Fact]
    public void Ingestion_ShouldKeepOnlyAllowListedAttributes()
    {
        var request = new SpanRequest(Trace, "aaaaaaaaaaaaaaaa", null, "GET /api/users", "Server", T0, 12.5, "Ok", null,
            new Dictionary<string, string?>
            {
                ["http.request.method"] = "GET",
                ["http.route"] = "/api/users",
                ["url.query"] = "token=secret",
                ["db.query.text"] = "SELECT * FROM users WHERE password = 'x'",
                ["enduser.id"] = "alice",
                [TraceAttributes.RoutineName] = "get_all_users"
            });

        var entity = TraceIngestion.ToEntity("UsersApi", request, T0);

        Assert.DoesNotContain("secret", entity.AttributesJson);
        Assert.DoesNotContain("password", entity.AttributesJson);
        Assert.DoesNotContain("alice", entity.AttributesJson);
        Assert.Equal(("GET", "/api/users", "get_all_users"), (entity.HttpMethod, entity.HttpRoute, entity.RoutineName));
    }

    [Theory]
    [InlineData("not-a-trace-id", "aaaaaaaaaaaaaaaa", null, "Server", 1)]
    [InlineData(Trace, "AAAAAAAAAAAAAAAA", null, "Server", 1)]
    [InlineData(Trace, "aaaaaaaaaaaaaaaa", "123", "Server", 1)]
    [InlineData(Trace, "aaaaaaaaaaaaaaaa", null, "Banana", 1)]
    [InlineData(Trace, "aaaaaaaaaaaaaaaa", null, "Server", -5)]
    public void Validate_ShouldRejectMalformedSpans(string traceId, string spanId, string? parent, string kind,
        double duration)
    {
        var span = new SpanRequest(traceId, spanId, parent, "name", kind, T0, duration, "Ok", null, null);

        Assert.NotNull(TraceIngestion.Validate(span));
    }

    [Fact]
    public void Validate_ShouldAcceptWellFormedSpans()
    {
        var span = new SpanRequest(Trace, "aaaaaaaaaaaaaaaa", "bbbbbbbbbbbbbbbb", "name", "Client", T0, 3, "Ok", null, null);

        Assert.Null(TraceIngestion.Validate(span));
    }

    private static SpanEntity[] HttpTrace(string? parentOfServer)
    {
        return
        [
            Span("aaaaaaaaaaaaaaaa", parentOfServer, "GET /api/users", "Server", method: "GET", route: "/api/users"),
            Span("bbbbbbbbbbbbbbbb", "aaaaaaaaaaaaaaaa", "UsersController.GetUsers", "Internal", offsetMs: 1),
            Span("cccccccccccccccc", "bbbbbbbbbbbbbbbb", "UsersService.GetAllUsers", "Internal", offsetMs: 2),
            Span("dddddddddddddddd", "cccccccccccccccc", "public.get_all_users()", "Client", routine: "get_all_users",
                offsetMs: 3)
        ];
    }

    private static SpanEntity Span(string id, string? parent, string name, string kind, string? declared = null,
        string? method = null, string? route = null, string? routine = null, int offsetMs = 0)
    {
        return new SpanEntity
        {
            TraceId = Trace,
            SpanId = id,
            ParentSpanId = parent,
            Name = name,
            Kind = kind,
            ServiceName = "UsersApi",
            StartUtc = T0.AddMilliseconds(offsetMs),
            DurationMs = 5,
            DeclaredCallerType = declared,
            HttpMethod = method,
            HttpRoute = route,
            RoutineSchema = routine == null ? null : "public",
            RoutineName = routine,
            RoutineArguments = routine == null ? null : "",
            AttributesJson = "{}"
        };
    }
}
