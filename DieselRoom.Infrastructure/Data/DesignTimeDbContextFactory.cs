using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DieselRoom.Infrastructure.Data;

/// <summary>Для dotnet ef migrations: схема генерируется без подключения к базе.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Port=5449;Database=diesel_room_db;Username=diesel_admin")
            .Options;
        return new AppDbContext(options);
    }
}
