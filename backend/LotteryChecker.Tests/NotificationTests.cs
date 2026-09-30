using FluentAssertions;
using LotteryChecker.Api.Data;
using LotteryChecker.Api.Models;
using LotteryChecker.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using static LotteryChecker.Api.Services.BlogService;

namespace LotteryChecker.Tests;

// Chuông thông báo: ai nhận khi có bình luận / trả lời, đọc / chưa đọc, không lộ tên người ký ẩn danh.
public class NotificationTests
{
    private class FakePusher : INotificationPusher
    {
        public List<(int UserId, NotificationService.NotificationDto N)> Sent { get; } = [];
        public Task PushAsync(int userId, NotificationService.NotificationDto n, CancellationToken ct)
        {
            Sent.Add((userId, n));
            return Task.CompletedTask;
        }
    }

    private const int Owner = 1, Alice = 2, Bob = 3;

    private static (BlogService Blog, NotificationService Notes, FakePusher Pusher) New()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var pusher = new FakePusher();
        return (new BlogService(db, TimeProvider.System),
                new NotificationService(db, pusher, TimeProvider.System, NullLogger<NotificationService>.Instance), pusher);
    }

    private static async Task<int> AddPost(BlogService blog, int? userId = Owner) =>
        (await blog.CreateAsync(new NewPost("Trúng giải tám", "Hôm qua dò vé trúng giải tám, vui ghê!", BlogAuthorMode.Anonymous, null),
                                userId, null, default)).Id;

    // Mô phỏng BlogController: thêm bình luận rồi báo.
    private static async Task<CommentDto> Comment(BlogService blog, NotificationService notes, int postId, int? userId,
                                                  string text, int? parentId = null, string? name = null)
    {
        var c = (await blog.CreateCommentAsync(postId,
            new NewComment(text, name == null ? BlogAuthorMode.Anonymous : BlogAuthorMode.Custom, name, parentId),
            userId, null, default)).Comment!;
        await notes.OnCommentAsync(c.Id, parentId, default);
        return c;
    }

    [Fact(DisplayName = "Binh luan bai -> bao chu bai (realtime + DB); tu binh luan bai minh thi khong bao")]
    public async Task Comment_NotifiesOwner()
    {
        var (blog, notes, pusher) = New();
        var postId = await AddPost(blog);

        await Comment(blog, notes, postId, Owner, "Tự bình luận");
        await Comment(blog, notes, postId, Alice, "Chúc mừng nha!", name: "Alice");

        pusher.Sent.Should().ContainSingle();
        var (uid, n) = pusher.Sent[0];
        uid.Should().Be(Owner);
        n.Kind.Should().Be(NotificationKind.PostComment);
        n.ActorName.Should().Be("Alice");
        n.PostTitle.Should().Be("Trúng giải tám");

        var list = await notes.ListAsync(Owner, default);
        list.Unread.Should().Be(1);
        list.Items.Should().ContainSingle().Which.Snippet.Should().Be("Chúc mừng nha!");
    }

    [Fact(DisplayName = "Tra loi -> bao nguoi duoc tra loi + nguoi viet goc + chu bai, moi nguoi 1 lan")]
    public async Task Reply_NotifiesThread()
    {
        var (blog, notes, pusher) = New();
        var postId = await AddPost(blog);
        var root = await Comment(blog, notes, postId, Alice, "Hay quá");
        var reply = await Comment(blog, notes, postId, Bob, "Đồng ý", root.Id);
        pusher.Sent.Clear();

        // Chủ bài trả lời câu trả lời của Bob → Bob (được trả lời) và Alice (gốc luồng) nhận; chủ bài thì không.
        await Comment(blog, notes, postId, Owner, "@Bob cảm ơn", reply.Id);

        pusher.Sent.Select(x => x.UserId).Should().BeEquivalentTo([Alice, Bob]);
        pusher.Sent.Should().OnlyContain(x => x.N.Kind == NotificationKind.CommentReply);
    }

    [Fact(DisplayName = "Khach / an danh: khong bao ai khi bai cua khach; ten an danh khong lo")]
    public async Task Guests_AndAnonymous()
    {
        var (blog, notes, pusher) = New();
        var guestPost = await AddPost(blog, userId: null);
        await Comment(blog, notes, guestPost, Alice, "Hi");
        pusher.Sent.Should().BeEmpty();

        var postId = await AddPost(blog);
        await Comment(blog, notes, postId, Alice, "Ẩn danh đây");   // Alice đăng nhập nhưng ký Ẩn danh
        pusher.Sent.Should().ContainSingle().Which.N.ActorName.Should().BeNull();
    }

    [Fact(DisplayName = "Danh dau da doc: tung cai / tat ca; chi cua minh")]
    public async Task MarkRead()
    {
        var (blog, notes, _) = New();
        var postId = await AddPost(blog);
        await Comment(blog, notes, postId, Alice, "Một");
        await Comment(blog, notes, postId, Bob, "Hai");
        var items = (await notes.ListAsync(Owner, default)).Items;
        items.Select(x => x.Snippet).Should().Equal("Hai", "Một");   // mới nhất trước

        (await notes.MarkReadAsync(Alice, [items[0].Id], default)).Should().Be(0);   // không phải của Alice
        (await notes.MarkReadAsync(Owner, [items[0].Id], default)).Should().Be(1);
        (await notes.MarkReadAsync(Owner, null, default)).Should().Be(0);
        (await notes.ListAsync(Owner, default)).Items.Should().OnlyContain(x => x.IsRead);
    }

    [Fact(DisplayName = "Binh luan bi xoa -> thong bao bien mat")]
    public async Task DeletedComment_Disappears()
    {
        var (blog, notes, _) = New();
        var postId = await AddPost(blog);
        var c = await Comment(blog, notes, postId, Alice, "Sẽ xoá");
        (await blog.DeleteCommentAsync(c.Id, Alice, false, default)).Should().NotBeNull();

        var list = await notes.ListAsync(Owner, default);
        list.Items.Should().BeEmpty();
        list.Unread.Should().Be(0);
    }
}
