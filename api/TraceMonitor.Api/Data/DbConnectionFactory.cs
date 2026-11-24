using Npgsql;

namespace TraceMonitor.Api.Data;

public sealed class DbConnectionFactory : IDisposable
{
    private readonly NpgsqlDataSource _dataSource;

    public DbConnectionFactory(IConfiguration configuration)
    {
        var connStr = configuration.GetConnectionString("PostgreSQL")
            ?? throw new InvalidOperationException("ConnectionStrings:PostgreSQL is not configured.");
        _dataSource = NpgsqlDataSource.Create(connStr);
    }

    public async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken ct = default)
        => await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);

    public void Dispose() => _dataSource.Dispose();
}
