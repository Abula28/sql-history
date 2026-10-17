using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SqlHistory.Core.Abstractions;
using SqlHistory.Core.Services;
using SqlHistory.Core.Versioning;
using SqlHistory.Hackathon.AiAnalysis;
using SqlHistory.Hackathon.Api;
using SqlHistory.Hackathon.Api.Endpoints;
using SqlHistory.PostgreSql;
using SqlHistory.PostgreSql.Routines;
using SqlHistory.Storage.Sqlite.Persistence;
using SqlHistory.Storage.Sqlite.Repositories;

var builder = WebApplication.CreateBuilder(args);

// Source database (PostgreSQL). Keep passwords out of appsettings: use ~/.pgpass,
// user-secrets or the ConnectionStrings__SourceDatabase environment variable.
var sourceConnectionString = builder.Configuration.GetConnectionString("SourceDatabase");
if (string.IsNullOrWhiteSpace(sourceConnectionString))
{
    throw new InvalidOperationException("Connection string 'SourceDatabase' is not configured.");
}

// History database (SQLite). A relative path is resolved against the project directory,
// so the same file is used no matter where the API is started from.
var historyConnection = new SqliteConnectionStringBuilder(
    builder.Configuration.GetConnectionString("SqlHistoryStorage"));
if (!Path.IsPathRooted(historyConnection.DataSource))
{
    historyConnection.DataSource = Path.Combine(builder.Environment.ContentRootPath, historyConnection.DataSource);
}

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<StoredProcedureSnapshotFactory>();
builder.Services.AddSingleton<StoredProcedureChangeDetector>();

builder.Services.AddSingleton(_ => new PostgreSqlConnectionFactory(sourceConnectionString));
builder.Services.AddSingleton<PostgreSqlRoutineReader>();
builder.Services.AddSingleton<IStoredProcedureReader>(sp => sp.GetRequiredService<PostgreSqlRoutineReader>());
builder.Services.AddSingleton<PostgreSqlRoutineRestorer>();

builder.Services.AddDbContext<SqlHistoryDbContext>(options =>
    options.UseSqlite(historyConnection.ToString()));
builder.Services.AddScoped<IStoredProcedureHistoryRepository, StoredProcedureHistoryRepository>();
builder.Services.AddScoped<SnapshotScanService>();
builder.Services.AddScoped<RoutineQueries>();
builder.Services.AddSingleton<HistoryCoordinator>();
builder.Services.AddHostedService<AutoScanService>();

var aiOptions = builder.Configuration.GetSection(AiAnalysisOptions.SectionName).Get<AiAnalysisOptions>()
                ?? new AiAnalysisOptions();
if (string.IsNullOrWhiteSpace(aiOptions.ApiKey))
{
    aiOptions.ApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
}
builder.Services.AddSingleton(aiOptions);
builder.Services.AddSingleton<ISqlChangeAnalyzer, OpenAiSqlChangeAnalyzer>();

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(allowedOrigins)
    .WithMethods("GET", "POST")
    .WithHeaders("Content-Type", RequireClientHeaderFilter.HeaderName)));

builder.Services.AddOpenApi(options =>
{
    options.AddOperationTransformer<ClientHeaderOperationTransformer>();
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Info.Title = "SQL Time Machine API";
        document.Info.Description =
            "Version history, diffs, AI change review and safe rollback for PostgreSQL functions and procedures.";
        return Task.CompletedTask;
    });
});
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<SqlHistoryDbContext>();
    await dbContext.Database.MigrateAsync();
}

app.Logger.LogInformation("History database: {Path}", historyConnection.DataSource);

app.UseExceptionHandler();
app.UseCors();

if (app.Environment.IsDevelopment())
{
    // OpenAPI document at /openapi/v1.json, Swagger UI at /swagger.
    app.MapOpenApi();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "SQL Time Machine API"));
}

app.MapStatusEndpoints();
app.MapRoutineEndpoints();
app.MapScanEndpoints();
app.MapAnalysisEndpoints();
app.MapRollbackEndpoints();

app.Run();
