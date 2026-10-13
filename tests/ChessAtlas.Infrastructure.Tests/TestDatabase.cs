using ChessAtlas.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ChessAtlas.Infrastructure.Tests;

/// <summary>
/// A throwaway SQLite database with the real migrations applied.
///
/// <para>
/// In-memory SQLite only lives as long as its connection, so the connection is owned by the fixture
/// and disposed last. Every test gets its own database: the catalogue importer is deliberately
/// idempotent, and a test that could see another test's rows would not be proving that.
/// </para>
/// </summary>
public sealed class TestDatabase : IDisposable
{
    private readonly SqliteConnection _connection;

    public TestDatabase()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        Options = new DbContextOptionsBuilder<ChessAtlasDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var context = new ChessAtlasDbContext(Options);
        context.Database.Migrate();
    }

    public DbContextOptions<ChessAtlasDbContext> Options { get; }

    public ChessAtlasDbContext CreateContext() => new(Options);

    /// <summary>Names of every table the migrations created.</summary>
    public IReadOnlyList<string> TableNames()
    {
        using var context = CreateContext();
        // The fixture keeps the connection open for the lifetime of the in-memory database.
        var connection = context.Database.GetDbConnection();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name";

        using var reader = command.ExecuteReader();
        var names = new List<string>();
        while (reader.Read()) names.Add(reader.GetString(0));
        return names;
    }

    public IReadOnlyList<string> IndexNames()
    {
        using var context = CreateContext();
        // The fixture keeps the connection open for the lifetime of the in-memory database.
        var connection = context.Database.GetDbConnection();

        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT name FROM sqlite_master WHERE type = 'index' AND name NOT LIKE 'sqlite_%' ORDER BY name";

        using var reader = command.ExecuteReader();
        var names = new List<string>();
        while (reader.Read()) names.Add(reader.GetString(0));
        return names;
    }

    public void Dispose() => _connection.Dispose();
}
