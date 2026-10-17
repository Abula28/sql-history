using System.Diagnostics;

namespace SqlHistory.Hackathon.DemoApi.Telemetry;

/// <summary>Spans the demo creates itself; ASP.NET Core and HttpClient spans come from instrumentation.</summary>
public static class DemoTelemetry
{
    public const string ServiceName = "UsersApi";

    public static readonly ActivitySource Source = new(ServiceName);

    /// <summary>
    /// Wraps one call of a PostgreSQL routine in a Client span carrying the routine's identity, so
    /// SQL Time Machine can attribute it without parsing SQL. No SQL text or parameter values are added.
    /// </summary>
    public static async Task<T> CallRoutineAsync<T>(string database, string schema, string name, string arguments,
        string routineType, Func<Task<T>> call)
    {
        using var activity = Source.StartActivity($"{schema}.{name}({arguments})", ActivityKind.Client);
        activity?.SetTag("db.system.name", "postgresql");
        activity?.SetTag("db.namespace", database);
        activity?.SetTag("db.operation.name", routineType == "Procedure" ? "CALL" : "SELECT");
        activity?.SetTag("db.stored_procedure.name", $"{schema}.{name}");
        activity?.SetTag("sqlhistory.routine.schema", schema);
        activity?.SetTag("sqlhistory.routine.name", name);
        activity?.SetTag("sqlhistory.routine.arguments", arguments);
        activity?.SetTag("sqlhistory.routine.type", routineType);

        try
        {
            return await call();
        }
        catch (Exception ex)
        {
            // The exception type only: messages can contain data.
            activity?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);
            activity?.SetTag("error.type", ex.GetType().FullName);
            throw;
        }
    }

    /// <summary>Starts the root span of work that was not triggered by an incoming request.</summary>
    public static Activity? StartRoot(string name, string callerType)
    {
        // Detach from any ambient activity so each run is its own trace.
        var previous = Activity.Current;
        Activity.Current = null;
        var activity = Source.StartActivity(name, ActivityKind.Internal);
        if (activity == null) Activity.Current = previous;

        activity?.SetTag("sqlhistory.caller.type", callerType);
        activity?.SetTag("sqlhistory.job.name", name.Split('.')[0]);
        return activity;
    }
}
