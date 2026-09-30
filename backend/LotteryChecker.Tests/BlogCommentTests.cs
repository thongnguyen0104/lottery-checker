using FluentAssertions;
using LotteryChecker.Api.Data;
using LotteryChecker.Api.Models;
using LotteryChecker.Api.Services;
using Microsoft.EntityFrameworkCore;
using static LotteryChecker.Api.Services.BlogService;

namespace LotteryChecker.Tests;

// Bình luận Blog: luồng 1 cấp, đếm số bình luận, ai được xoá.
public class BlogCommentTests
{
    private static (BlogService Blog, AppDbContext Db) New()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        return (new BlogService(db, TimeProvider.System), db);
    }

    private static async Task<int> AddPost(BlogService blog) =>
        (await blog.CreateAsync(new NewPost("Trúng giải tám", "Hôm qua dò vé trúng giải tám, vui ghê!", BlogAuthorMode.Anonymous, null),
                                null, null, default)).Id;

    private static NewComment Text(string content, int? parentId = null, BlogAuthorMode mode = BlogAuthorMode.Anonymous, string? name = null) =>
        new(content, mode, name, parentId);

    [Fact(DisplayName = "Tra loi mot cau tra loi van gan vao binh luan goc; dem ca tra loi")]
    public async Task Reply_FlattensToRoot()
    {
        var (blog, _) = New();
        var postId = await AddPost(blog);
        var root = (await blog.CreateCommentAsync(postId, Text("Chúc mừng!"), null, null, default)).Comment!;
        var reply = (await blog.CreateCommentAsync(postId, Text("Cảm ơn", root.Id), null, null, default)).Comment!;
        var (nested, count, error) = await blog.CreateCommentAsync(postId, Text("@x hi", reply.Id), null, null, default);

        error.Should().Be(CommentError.None);
        reply.ParentId.Should().Be(root.Id);
        nested!.ParentId.Should().Be(root.Id);
        count.Should().Be(3);
        (await blog.ListCommentsAsync(postId, null, false, default))!.Select(c => c.Content)
            .Should().Equal("Chúc mừng!", "Cảm ơn", "@x hi");
    }

    [Fact(DisplayName = "Tra loi binh luan cua bai khac / bai khong ton tai -> loi")]
    public async Task Create_Errors()
    {
        var (blog, _) = New();
        var a = await AddPost(blog);
        var b = await AddPost(blog);
        var onA = (await blog.CreateCommentAsync(a, Text("Hay"), null, null, default)).Comment!;

        (await blog.CreateCommentAsync(b, Text("Sai bài", onA.Id), null, null, default)).Error.Should().Be(CommentError.NoParent);
        (await blog.CreateCommentAsync(999, Text("Không có bài"), null, null, default)).Error.Should().Be(CommentError.NoPost);
        (await blog.ListCommentsAsync(999, null, false, default)).Should().BeNull();
    }

    [Fact(DisplayName = "Ky ten nhu bai viet; validate do dai va ky ten")]
    public async Task Sign_AndValidate()
    {
        var (blog, _) = New();
        var postId = await AddPost(blog);
        (await blog.CreateCommentAsync(postId, Text("Hay quá", mode: BlogAuthorMode.Account), 7, "thong", default))
            .Comment!.AuthorName.Should().Be("thong");
        (await blog.CreateCommentAsync(postId, Text("Hay quá", mode: BlogAuthorMode.Custom, name: "  Cô Ba "), null, null, default))
            .Comment!.AuthorName.Should().Be("Cô Ba");

        ValidateComment(Text(" x "), null, false).Should().NotBeNull();                              // quá ngắn
        ValidateComment(Text(new string('a', CommentMax + 1)), null, false).Should().NotBeNull();
        ValidateComment(Text("Hay quá", mode: BlogAuthorMode.Account), null, false).Should().NotBeNull();  // khách
        ValidateComment(Text("Hay quá"), null, false).Should().BeNull();
    }

    [Fact(DisplayName = "Xoa: chi nguoi viet hoac admin; xoa goc thi xoa luon tra loi va dem lai")]
    public async Task Delete_OwnerOrAdmin()
    {
        var (blog, db) = New();
        var postId = await AddPost(blog);
        var root = (await blog.CreateCommentAsync(postId, Text("Gốc"), 1, "user1", default)).Comment!;
        await blog.CreateCommentAsync(postId, Text("Trả lời", root.Id), 2, "user2", default);
        var other = (await blog.CreateCommentAsync(postId, Text("Khác"), 2, "user2", default)).Comment!;

        (await blog.ListCommentsAsync(postId, 1, false, default))!.Single(c => c.Id == root.Id).CanDelete.Should().BeTrue();
        (await blog.ListCommentsAsync(postId, 1, false, default))!.Single(c => c.Id == other.Id).CanDelete.Should().BeFalse();
        (await blog.ListCommentsAsync(postId, 1, true, default))!.Should().OnlyContain(c => c.CanDelete);

        (await blog.DeleteCommentAsync(other.Id, 1, false, default)).Should().BeNull();         // không phải của mình
        (await blog.DeleteCommentAsync(root.Id, 1, false, default)).Should().Be((postId, 1));   // gốc + trả lời
        (await blog.DeleteCommentAsync(other.Id, 99, true, default)).Should().Be((postId, 0));  // admin
        db.BlogComments.Should().BeEmpty();
        (await blog.ListAsync(null, 1, "g:x", null, default)).Items.Single().CommentCount.Should().Be(0);
    }
}
