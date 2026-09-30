using Microsoft.Data.SqlClient;

namespace SqlHistory.SqlServer;

public class SqlServerConnectionFactory
{
    private readonly string _connectionString;
    public SqlServerConnectionFactory(string connectionString)
    {
        _connectionString = connectionString;
    }

    public SqlConnection CreateConnection()
    {
        return new SqlConnection(_connectionString);
    }
}