using Microsoft.EntityFrameworkCore;
using RoshedTehran.Models;

namespace RoshedTehran.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

        public DbSet<Entity> Entities => Set<Entity>();
        public DbSet<PhoneNumber> PhoneNumbers => Set<PhoneNumber>();
        public DbSet<Email> Emails => Set<Email>();
        public DbSet<SocialMediaLink> SocialMediaLinks => Set<SocialMediaLink>();
        public DbSet<QueryEntity> Queries => Set<QueryEntity>();
        public DbSet<CrawlIngestState> CrawlIngestStates => Set<CrawlIngestState>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Entity>(e =>
            {
                e.HasKey(x => x.Id);
                e.HasIndex(x => x.Domain).IsUnique();
                e.HasMany(x => x.PhoneNumbers).WithOne(p => p.Entity!).HasForeignKey(p => p.RecordId);
                e.HasMany(x => x.Emails).WithOne(p => p.Entity!).HasForeignKey(p => p.RecordId);
                e.HasMany(x => x.SocialMediaLinks).WithOne(p => p.Entity!).HasForeignKey(p => p.RecordId);
            });
        }
    }
}
