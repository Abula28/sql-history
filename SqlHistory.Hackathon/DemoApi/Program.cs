using Npgsql;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using SqlHistory.Hackathon.DemoApi.Services;
using SqlHistory.Hackathon.DemoApi.Telemetry;
using SqlHistory.Hackathon.DemoApi.Workers;

// A small instrumented application for the SQL Time Machine tracing demo. It calls PostgreSQL
// routines from an HTTP endpoint, a background job and an internal service, and exports the
// resulting OpenTelemetry spans to SQL Time Machine.
var builder = WebApplication.CreateBuilder(args);

// Same user-secrets as the SQL Time Machine API (see the csproj). Application Name makes these
// sessions identifiable in pg_stat_activity.
// The pool is capped well below PostgreSQL's max_connections (100 by default) so a load test
// queues requests in the pool instead of locking other clients out of the server.
var connection = new NpgsqlConnectionStringBuilder(builder.Configuration.GetConnectionString("SourceDatabase"))
{
    ApplicationName = DemoTelemetry.ServiceName,
    MaxPoolSize = builder.Configuration.GetValue("Database:MaxPoolSize", 40)
};
builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(connection.ConnectionString));
builder.Services.AddSingleton<UsersService>();

var selfUrl = builder.Configuration.GetValue<string>("Urls")?.Split(';')[0] ?? "http://localhost:5190";
builder.Services.AddHttpClient("self", client => client.BaseAddress = new Uri(selfUrl));
builder.Services.AddHostedService<UserSyncWorker>();
builder.Services.AddHostedService<BillingService>();

// 1.0 records every trace. Lower it for load tests: the exporter queue drops spans it cannot keep up with.
var sampleRatio = builder.Configuration.GetValue("Tracing:SampleRatio", 1.0);

var traceEndpoint = new Uri(builder.Configuration.GetValue<string>("SqlTimeMachine:TraceEndpoint")
                            ?? "http://localhost:5180/api/traces");

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(DemoTelemetry.ServiceName))
    .WithTracing(tracing => tracing
        .SetSampler(new ParentBasedSampler(new TraceIdRatioBasedSampler(sampleRatio)))
        .AddSource(DemoTelemetry.ServiceName)
        .AddAspNetCoreInstrumentation(options => options.Filter = context => context.Request.Path != "/health")
        .AddHttpClientInstrumentation()
        .AddProcessor(new BatchActivityExportProcessor(new SqlTimeMachineExporter(traceEndpoint),
            maxQueueSize: 8192, scheduledDelayMilliseconds: 2000, maxExportBatchSize: 256)));

builder.Services.AddControllers();

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapControllers();

app.Run();
