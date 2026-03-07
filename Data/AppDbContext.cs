using Microsoft.EntityFrameworkCore;
using TgDataPlanner.Data.Entities;

namespace DefaultNamespace;

public class AppDbContext : DbContext
{
    public DbSet<Player> Players => Set<Player>();
    public DbSet<Group> Groups => Set<Group>();
    public DbSet<AvailabilitySlot> Slots => Set<AvailabilitySlot>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        // Для начала используем SQLite
        var dbPath = Path.Combine(AppContext.BaseDirectory, "../../../dnd_planner.db");
        optionsBuilder.UseSqlite($"Data Source={dbPath}");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Настройка связи многие-ко-многим (Игроки <-> Группы)
        modelBuilder.Entity<Group>()
            .HasMany(g => g.Players)
            .WithMany(p => p.Groups);
    }
}