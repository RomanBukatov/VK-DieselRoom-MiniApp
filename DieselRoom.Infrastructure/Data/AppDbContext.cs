using DieselRoom.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DieselRoom.Infrastructure.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<PointsTransaction> PointsTransactions => Set<PointsTransaction>();
    public DbSet<VkActivityLog> VkActivityLogs => Set<VkActivityLog>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();
    public DbSet<PromoBanner> PromoBanners => Set<PromoBanner>();
    public DbSet<MenuLink> MenuLinks => Set<MenuLink>();
    public DbSet<AfishaEvent> AfishaEvents => Set<AfishaEvent>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<ShopItem> ShopItems => Set<ShopItem>();
    public DbSet<ShopOrder> ShopOrders => Set<ShopOrder>();
    public DbSet<Visit> Visits => Set<Visit>();
    public DbSet<PrivilegeRedemption> PrivilegeRedemptions => Set<PrivilegeRedemption>();
    public DbSet<WeeklyRaceResult> WeeklyRaceResults => Set<WeeklyRaceResult>();
    public DbSet<DailyLeaderSnapshot> DailyLeaderSnapshots => Set<DailyLeaderSnapshot>();
    public DbSet<StaffMember> StaffMembers => Set<StaffMember>();
    public DbSet<StaffAuditLog> StaffAuditLogs => Set<StaffAuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>(e =>
        {
            e.Property(u => u.VkId).HasMaxLength(32).IsRequired();
            e.Property(u => u.CardNumber).HasMaxLength(16).IsRequired();
            e.Property(u => u.FirstName).HasMaxLength(100);
            e.Property(u => u.LastName).HasMaxLength(100);
            e.Property(u => u.PhotoUrl).HasMaxLength(500);
            e.Property(u => u.Phone).HasMaxLength(16);
            e.Ignore(u => u.DisplayName);
            e.HasIndex(u => u.VkId).IsUnique();
            e.HasIndex(u => u.CardNumber).IsUnique();
            e.HasIndex(u => u.Phone);
            // Лидерборд недели: сортировка по очкам и времени набора
            e.HasIndex(u => new { u.WeeklyRating, u.WeeklyRatingUpdatedAt });
            e.HasIndex(u => u.TotalRating);
            e.HasIndex(u => u.ReferredByUserId);
        });

        modelBuilder.Entity<PointsTransaction>(e =>
        {
            e.Property(t => t.Type).HasMaxLength(32);
            e.Property(t => t.Description).HasMaxLength(300);
            e.HasIndex(t => new { t.UserId, t.CreatedAt });
        });

        modelBuilder.Entity<VkActivityLog>(e =>
        {
            e.Property(l => l.VkId).HasMaxLength(32);
            e.Property(l => l.ActivityType).HasMaxLength(16);
            e.Property(l => l.CommentText).HasMaxLength(500);
            // Одна награда за один тип активности к одной записи стены
            e.HasIndex(l => new { l.VkId, l.PostId, l.ActivityType }).IsUnique();
            e.HasIndex(l => new { l.UserId, l.CreatedAt });
        });

        modelBuilder.Entity<AppSetting>(e =>
        {
            e.HasKey(s => s.Key);
            e.Property(s => s.Key).HasMaxLength(64);
            e.Property(s => s.Value).HasMaxLength(4000);
        });

        modelBuilder.Entity<PromoBanner>(e =>
        {
            e.Property(b => b.Title).HasMaxLength(200).IsRequired();
            e.Property(b => b.Subtitle).HasMaxLength(300);
            e.Property(b => b.LinkUrl).HasMaxLength(500);
            e.Property(b => b.ImageUrl).HasMaxLength(500);
        });

        modelBuilder.Entity<MenuLink>(e =>
        {
            e.Property(m => m.Key).HasMaxLength(32).IsRequired();
            e.Property(m => m.Emoji).HasMaxLength(16);
            e.Property(m => m.Title).HasMaxLength(100).IsRequired();
            e.Property(m => m.Url).HasMaxLength(500).IsRequired();
            e.HasIndex(m => m.Key).IsUnique();
        });

        modelBuilder.Entity<AfishaEvent>(e =>
        {
            e.Property(a => a.Title).HasMaxLength(200).IsRequired();
            e.Property(a => a.Tagline).HasMaxLength(300);
            e.Property(a => a.Description).HasMaxLength(2000);
            e.Property(a => a.ProductUrl).HasMaxLength(500);
            e.Property(a => a.ImageUrl).HasMaxLength(500);
        });

        modelBuilder.Entity<Booking>(e =>
        {
            e.Property(b => b.Name).HasMaxLength(60).IsRequired();
            e.Property(b => b.Phone).HasMaxLength(16).IsRequired();
            e.Property(b => b.Wishes).HasMaxLength(500);
            e.Property(b => b.StaffComment).HasMaxLength(300);
            e.Property(b => b.ProcessedByStaffName).HasMaxLength(100);
            e.HasIndex(b => new { b.Status, b.Date });
            e.HasIndex(b => new { b.UserId, b.CreatedAt });
        });

        modelBuilder.Entity<ShopItem>(e =>
        {
            e.Property(i => i.Title).HasMaxLength(120).IsRequired();
            e.Property(i => i.Description).HasMaxLength(500);
            e.Property(i => i.Icon).HasMaxLength(16);
            e.ToTable(t => t.HasCheckConstraint("CK_ShopItems_StockCount", "\"StockCount\" >= 0"));
        });

        modelBuilder.Entity<ShopOrder>(e =>
        {
            e.Property(o => o.Code).HasMaxLength(16).IsRequired();
            e.Property(o => o.Title).HasMaxLength(120);
            e.Property(o => o.Icon).HasMaxLength(16);
            e.Property(o => o.IssuedByStaffName).HasMaxLength(100);
            e.HasIndex(o => o.Code).IsUnique();
            e.HasIndex(o => new { o.UserId, o.Status });
            // Фоновая проверка просрочки
            e.HasIndex(o => new { o.Status, o.ExpiresAt });
        });

        modelBuilder.Entity<Visit>(e =>
        {
            e.Property(v => v.ReceiptNumber).HasMaxLength(32).IsRequired();
            e.Property(v => v.StaffName).HasMaxLength(100);
            // 1 чек = 1 раз: один и тот же чек физически нельзя провести дважды
            e.HasIndex(v => v.ReceiptNumber).IsUnique();
            e.HasIndex(v => new { v.UserId, v.CreatedAt });
        });

        modelBuilder.Entity<PrivilegeRedemption>(e =>
        {
            e.Property(r => r.StaffName).HasMaxLength(100);
            e.HasIndex(r => new { r.UserId, r.Tier, r.WeekStart });
        });

        modelBuilder.Entity<WeeklyRaceResult>(e =>
        {
            e.HasIndex(r => new { r.WeekStart, r.UserId }).IsUnique();
            e.HasIndex(r => r.UserId);
        });

        modelBuilder.Entity<DailyLeaderSnapshot>(e =>
        {
            e.HasKey(s => s.Date);
        });

        modelBuilder.Entity<StaffMember>(e =>
        {
            e.Property(s => s.Name).HasMaxLength(100).IsRequired();
            e.Property(s => s.VkId).HasMaxLength(32);
            e.Property(s => s.PinHash).HasMaxLength(200);
            e.HasIndex(s => s.VkId);
            // Два сотрудника не могут иметь один ПИН-код (NULL допускается многократно)
            e.HasIndex(s => s.PinHash).IsUnique();
        });

        modelBuilder.Entity<StaffAuditLog>(e =>
        {
            e.Property(a => a.StaffName).HasMaxLength(100);
            e.Property(a => a.StaffVkId).HasMaxLength(32);
            e.Property(a => a.Action).HasMaxLength(32);
            e.Property(a => a.GuestName).HasMaxLength(200);
            e.Property(a => a.ReceiptNumber).HasMaxLength(32);
            e.Property(a => a.Details).HasMaxLength(1000);
            e.HasIndex(a => a.CreatedAt);
            e.HasIndex(a => new { a.StaffMemberId, a.CreatedAt });
        });
    }
}

public static class DbErrors
{
    /// <summary>Нарушение уникального индекса: PostgreSQL 23505 или SQLite «UNIQUE constraint failed» (тесты).</summary>
    public static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } ||
        (ex.InnerException?.GetType().Name == "SqliteException" &&
         ex.InnerException.Message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Блокирует строку (SELECT ... FOR UPDATE) до конца текущей транзакции — только в PostgreSQL. В SQLite (тесты)
    /// пишущая транзакция и так единственная, а условные UPDATE в сервисах всё равно не дают уйти в минус.
    /// </summary>
    public static async Task LockRowAsync(this AppDbContext db, string table, int id, CancellationToken ct = default)
    {
        if (!db.Database.IsNpgsql()) return;
        if (!LockableTables.Contains(table)) throw new ArgumentOutOfRangeException(nameof(table), table, "Таблица не поддерживает блокировку строк.");
#pragma warning disable EF1002 // имя таблицы — из белого списка выше, значение Id передаётся параметром
        await db.Database.ExecuteSqlRawAsync($"SELECT 1 FROM \"{table}\" WHERE \"Id\" = {{0}} FOR UPDATE", [id], ct);
#pragma warning restore EF1002
    }

    private static readonly HashSet<string> LockableTables = ["Users", "ShopItems", "ShopOrders", "Bookings"];
}
