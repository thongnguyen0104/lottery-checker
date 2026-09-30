using LotteryChecker.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LotteryChecker.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<LotteryResult> LotteryResults => Set<LotteryResult>();
    public DbSet<User> Users => Set<User>();
    public DbSet<GuestUsage> GuestUsages => Set<GuestUsage>();
    public DbSet<CheckedTicket> CheckedTickets => Set<CheckedTicket>();
    public DbSet<BlogPost> BlogPosts => Set<BlogPost>();
    public DbSet<BlogVote> BlogVotes => Set<BlogVote>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<LotteryResult>(e =>
        {
            e.HasIndex(x => new { x.DrawDate, x.Province });
            e.HasIndex(x => x.Number);
            e.Property(x => x.Region).HasMaxLength(8);
            e.Property(x => x.Province).HasMaxLength(32);
            e.Property(x => x.PrizeTier).HasMaxLength(4);
            e.Property(x => x.Number).HasMaxLength(8);
        });

        b.Entity<User>(e =>
        {
            e.HasIndex(x => x.Username).IsUnique();
            e.Property(x => x.Username).HasMaxLength(20);
        });

        b.Entity<GuestUsage>(e =>
        {
            e.HasKey(x => x.Ip);
            e.Property(x => x.Ip).HasMaxLength(45); // đủ cho IPv6
        });

        b.Entity<CheckedTicket>(e =>
        {
            e.HasIndex(x => new { x.DrawDate, x.Province, x.TicketNumber }).IsUnique();
            e.Property(x => x.Province).HasMaxLength(32);
            e.Property(x => x.TicketNumber).HasMaxLength(6);
        });

        b.Entity<BlogPost>(e =>
        {
            e.HasIndex(x => x.CreatedAt);
            e.Property(x => x.Title).HasMaxLength(120);
            e.Property(x => x.Content).HasMaxLength(5000);
            e.Property(x => x.AuthorName).HasMaxLength(30);
            e.Property(x => x.AuthorMode).HasConversion<string>().HasMaxLength(10);
        });

        b.Entity<BlogVote>(e =>
        {
            e.HasKey(x => new { x.PostId, x.VoterKey });
            e.Property(x => x.VoterKey).HasMaxLength(40);
            e.HasOne<BlogPost>().WithMany().HasForeignKey(x => x.PostId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}