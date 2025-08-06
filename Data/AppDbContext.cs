using DonorSQLAgent;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using Pgvector.EntityFrameworkCore;

public class AppDbContext : DbContext
{
    public DbSet<DocumentsDetails> documentdetails { get; set; }
    public DbSet<DocumentChunkDetails> documentchunkdetails { get; set; }

    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("vector");

        modelBuilder.Entity<DocumentChunkDetails>()
            .Property(e => e.embeddedchunkdata)
            .HasColumnType("vector(1536)");
    }
}
