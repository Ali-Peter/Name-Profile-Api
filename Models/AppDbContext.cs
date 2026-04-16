using Microsoft.EntityFrameworkCore;

public class AppDbContext : DbContext
{
    public DbSet<Profile> Profiles { get; set; }

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Profile>()
            .HasIndex(p => p.Name)
            .IsUnique(); // For fast duplicate check
    }
}