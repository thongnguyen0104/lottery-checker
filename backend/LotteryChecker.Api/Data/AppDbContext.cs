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
    public DbSet<BlogComment> BlogComments => Set<BlogComment>();
    public DbSet<BlogImage> BlogImages => Set<BlogImage>();
    public DbSet<CheckHistoryEntry> CheckHistory => Set<CheckHistoryEntry>();
    public DbSet<ScratchTicket> ScratchTickets => Set<ScratchTicket>();
    public DbSet<WalletTransaction> WalletTransactions => Set<WalletTransaction>();
    public DbSet<FeatureFlag> FeatureFlags => Set<FeatureFlag>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<ShopLocation> Shops => Set<ShopLocation>();
    public DbSet<ShopConfirm> ShopConfirms => Set<ShopConfirm>();
    public DbSet<ShopReview> ShopReviews => Set<ShopReview>();
    public DbSet<ShopWinReport> ShopWinReports => Set<ShopWinReport>();
    public DbSet<ShopReport> ShopReports => Set<ShopReport>();
    public DbSet<ShopImage> ShopImages => Set<ShopImage>();
    public DbSet<ShopRevision> ShopRevisions => Set<ShopRevision>();

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
            e.Property(x => x.Balance).IsConcurrencyToken();
        });

        b.Entity<GuestUsage>(e =>
        {
            e.HasKey(x => x.Key);
            e.Property(x => x.Key).HasMaxLength(64); // "ip:" + IPv6 / "v:" + 32 hex
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
            e.HasIndex(x => x.PublicId).IsUnique();
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

        b.Entity<BlogComment>(e =>
        {
            e.HasIndex(x => new { x.PostId, x.Id });
            e.Property(x => x.Content).HasMaxLength(1000);
            e.Property(x => x.AuthorName).HasMaxLength(30);
            e.Property(x => x.AuthorMode).HasConversion<string>().HasMaxLength(10);
            // Xoá bài → xoá hết bình luận; xoá bình luận gốc → xoá các trả lời của nó.
            e.HasOne<BlogPost>().WithMany().HasForeignKey(x => x.PostId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<BlogComment>().WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<BlogImage>(e =>
        {
            e.HasIndex(x => x.Key).IsUnique();
            e.HasIndex(x => new { x.PostId, x.SortOrder });
            e.HasIndex(x => new { x.OwnerKey, x.CreatedAt });
            e.Property(x => x.Key).HasMaxLength(80);
            e.Property(x => x.OwnerKey).HasMaxLength(40);
            // Xoá bài → ảnh thành mồ côi (không xoá dòng) để worker còn key mà xoá trên bucket.
            e.HasOne<BlogPost>().WithMany().HasForeignKey(x => x.PostId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<CheckHistoryEntry>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.CheckedAt });
            e.Property(x => x.TicketNumber).HasMaxLength(6);
            e.Property(x => x.Province).HasMaxLength(32);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(12);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ScratchTicket>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.PurchasedAt });
            e.HasIndex(x => new { x.Status, x.DrawDate });   // tìm vé chờ chốt
            e.Property(x => x.Province).HasMaxLength(32);
            e.Property(x => x.Number).HasMaxLength(2);
            e.Property(x => x.WinningNumber).HasMaxLength(2);
            // Concurrency token: 2 lượt chốt cùng lúc (worker + user mở trang) thì lượt sau lỗi thay vì cộng tiền 2 lần.
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(10).IsConcurrencyToken();
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<FeatureFlag>(e =>
        {
            e.HasKey(x => x.Key);
            e.Property(x => x.Key).HasMaxLength(40);
            e.Property(x => x.UpdatedBy).HasMaxLength(20);
        });

        b.Entity<Notification>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.Id });
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(16);
            // Xoá tài khoản / bài / bình luận → thông báo liên quan đi theo.
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<BlogPost>().WithMany().HasForeignKey(x => x.PostId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<BlogComment>().WithMany().HasForeignKey(x => x.CommentId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<ShopLocation>().WithMany().HasForeignKey(x => x.ShopId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<ShopReview>().WithMany().HasForeignKey(x => x.ShopReviewId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<ShopWinReport>().WithMany().HasForeignKey(x => x.ShopWinReportId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ShopLocation>(e =>
        {
            e.ToTable("Shops");
            e.HasIndex(x => x.PublicId).IsUnique();
            e.HasIndex(x => new { x.Status, x.Lat, x.Lng });   // truy vấn theo khung nhìn bản đồ
            e.HasIndex(x => new { x.UserId, x.CreatedAt });    // giới hạn số điểm tạo / ngày
            e.Property(x => x.Name).HasMaxLength(80);
            e.Property(x => x.Address).HasMaxLength(200);
            e.Property(x => x.Phone).HasMaxLength(15);
            e.Property(x => x.Note).HasMaxLength(500);
            e.Property(x => x.ImageKey).HasMaxLength(80);
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(12);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(8);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ShopConfirm>(e =>
        {
            e.HasKey(x => new { x.ShopId, x.UserId });
            e.HasOne<ShopLocation>().WithMany().HasForeignKey(x => x.ShopId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ShopReview>(e =>
        {
            e.HasIndex(x => new { x.ShopId, x.UserId }).IsUnique();
            e.Property(x => x.Content).HasMaxLength(1000);
            e.HasOne<ShopLocation>().WithMany().HasForeignKey(x => x.ShopId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ShopWinReport>(e =>
        {
            e.HasIndex(x => new { x.ShopId, x.DrawDate });
            e.Property(x => x.ProvinceCode).HasMaxLength(32);
            e.Property(x => x.PrizeTier).HasMaxLength(10);
            e.Property(x => x.ImageKey).HasMaxLength(80);
            e.HasOne<ShopLocation>().WithMany().HasForeignKey(x => x.ShopId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ShopReport>(e =>
        {
            e.HasKey(x => new { x.ShopId, x.UserId });
            e.Property(x => x.Reason).HasConversion<string>().HasMaxLength(14);
            e.Property(x => x.Note).HasMaxLength(300);
            e.HasOne<ShopLocation>().WithMany().HasForeignKey(x => x.ShopId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ShopRevision>(e =>
        {
            e.HasIndex(x => new { x.ShopId, x.Id });
            e.Property(x => x.Action).HasConversion<string>().HasMaxLength(8);
            e.Property(x => x.SnapshotJson).HasMaxLength(2000);
            e.HasOne<ShopLocation>().WithMany().HasForeignKey(x => x.ShopId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ShopImage>(e =>
        {
            e.HasIndex(x => x.Key).IsUnique();
            e.HasIndex(x => new { x.Used, x.CreatedAt });
            e.Property(x => x.Key).HasMaxLength(80);
        });

        b.Entity<WalletTransaction>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.CreatedAt });
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.Note).HasMaxLength(200);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}