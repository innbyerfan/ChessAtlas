using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ChessAtlas.Infrastructure.Persistence;

/// <summary>
/// Lets <c>dotnet ef migrations add</c> build a context without the application running.
/// The database it points at is never used — migrations are generated from the model, not the file —
/// but a valid provider is required.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ChessAtlasDbContext>
{
    public ChessAtlasDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ChessAtlasDbContext>()
            .UseSqlite("Data Source=chessatlas-designtime.db")
            .Options;

        return new ChessAtlasDbContext(options);
    }
}
