using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using SqlHistory.Core.Abstractions;
using SqlHistory.SqlServer;

var configurationBuilder = new ConfigurationBuilder();

BuildConfig(configurationBuilder);

var configuration = configurationBuilder.Build();

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();

try
{
    Log.Information("Application starting...");

    var host = Host.CreateDefaultBuilder(args)
        .UseSerilog()
        .ConfigureServices((context, services) =>
        {
            services.AddScoped<
                IStoredProcedureReader,
                SqlServerStoreProceduresReader>();
            services.AddScoped<ApplicationRunner>();
            services.AddSingleton(_ =>
            {
                var connectionString = configuration.GetValue<string>("ServerConnectionString");
                if (string.IsNullOrWhiteSpace(connectionString))
                {
                    throw new InvalidOperationException(
                        "Connection string 'ServerConnectionString' is not configured.");
                }

                return new SqlServerConnectionFactory(connectionString);
            });
        })
        .Build();

    using var scope = host.Services.CreateScope();

    var service = scope.ServiceProvider.GetRequiredService<ApplicationRunner>();

    await service.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly.");
}
finally
{
    Log.CloseAndFlush();
}

static void BuildConfig(IConfigurationBuilder builder)
{
    builder
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
        .AddJsonFile(
            $"appsettings.{Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")}.json",
            optional: true)
        .AddUserSecrets<Program>(optional: true)
        .AddEnvironmentVariables();
}