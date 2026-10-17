using Npgsql;
using SqlHistory.Hackathon.DemoApi.Telemetry;

namespace SqlHistory.Hackathon.DemoApi.Services;

public sealed record UserDto(int Id, string Email, bool IsActive);

public sealed class UsersService
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly string _database;

    public UsersService(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
        _database = new NpgsqlConnectionStringBuilder(dataSource.ConnectionString).Database ?? "unknown";
    }

    public async Task<IReadOnlyList<UserDto>> GetAllUsersAsync(CancellationToken cancellationToken)
    {
        using var activity = DemoTelemetry.Source.StartActivity("UsersService.GetAllUsers");

        return await DemoTelemetry.CallRoutineAsync(_database, "public", "get_all_users", "", "Function", async () =>
        {
            await using var command = _dataSource.CreateCommand("SELECT id, email, is_active FROM public.get_all_users()");
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            var users = new List<UserDto>();
            while (await reader.ReadAsync(cancellationToken))
                users.Add(new UserDto(reader.GetInt32(0), reader.GetString(1), reader.GetBoolean(2)));
            return users;
        });
    }

    /// <summary>Calls the procedure public.get_allusers(), which reads the users table and returns nothing.</summary>
    public async Task CheckUsersAsync(CancellationToken cancellationToken)
    {
        using var activity = DemoTelemetry.Source.StartActivity("UsersService.CheckUsers");

        await DemoTelemetry.CallRoutineAsync(_database, "public", "get_allusers", "", "Procedure", async () =>
        {
            await using var command = _dataSource.CreateCommand("CALL public.get_allusers()");
            return await command.ExecuteNonQueryAsync(cancellationToken);
        });
    }

    public async Task<long> CountActiveUsersAsync(CancellationToken cancellationToken)
    {
        using var activity = DemoTelemetry.Source.StartActivity("UsersService.CountActiveUsers");

        return await DemoTelemetry.CallRoutineAsync(_database, "public", "active_users", "", "Function", async () =>
        {
            await using var command = _dataSource.CreateCommand("SELECT public.active_users()");
            return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
        });
    }
}
