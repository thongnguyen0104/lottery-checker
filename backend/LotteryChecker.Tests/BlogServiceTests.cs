using FluentAssertions;
using LotteryChecker.Api.Data;
using LotteryChecker.Api.Models;
using LotteryChecker.Api.Services;
using Microsoft.EntityFrameworkCore;
using NewPost = LotteryChecker.Api.Services.BlogService.NewPost;

namespace LotteryChecker.Tests;

// Blog: ký tên, like/dislike mỗi người 1 lượt, xoá bài của mình.
public class BlogServiceTests
{
    private static BlogService NewBlog() => new(
        new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options),
        TimeProvider.System);

    private static NewPost Post(BlogAuthorMode mode = BlogAuthorMode.Anonymous, string? name = null) =>
        new("Trúng giải tám", "Hôm qua dò vé trúng giải tám, vui ghê!", mode, name);

    [Fact(DisplayName = "Ky ten: tai khoan lay username, an danh khong ten, tu dat lay ten da trim")]
    public async Task Create_SignsByMode()
    {
        var blog = NewBlog();
        (await blog.CreateAsync(Post(BlogAuthorMode.Account), 7, "thong", default)).AuthorName.Should().Be("thong");
        (await blog.CreateAsync(Post(BlogAuthorMode.Anonymous, "bỏ qua"), 7, "thong", default)).AuthorName.Should().BeNull();
        (await blog.CreateAsync(Post(BlogAuthorMode.Custom, "  Cô Ba  "), null, null, default)).AuthorName.Should().Be("Cô Ba");
    }

    [Fact(DisplayName = "Validate: khach khong ky ten tai khoan; ten tu dat qua ngan bi chan")]
    public void Validate_Rules()
    {
        BlogService.Validate(Post(BlogAuthorMode.Account), null, false).Should().NotBeNull();
        BlogService.Validate(Post(BlogAuthorMode.Account), "thong", false).Should().BeNull();
        BlogService.Validate(Post(BlogAuthorMode.Custom, "A"), null, false).Should().NotBeNull();
        BlogService.Validate(Post() with { Title = "  " }, null, false).Should().NotBeNull();
        BlogService.Validate(Post() with { Content = "ngắn" }, null, false).Should().NotBeNull();
        BlogService.Validate(Post(), null, false).Should().BeNull();
    }

    [Fact(DisplayName = "Vote: moi nguoi 1 luot, doi chieu thi chuyen, 0 la bo")]
    public async Task Vote_OnePerVoter()
    {
        var blog = NewBlog();
        var id = (await blog.CreateAsync(Post(), null, null, default)).Id;

        await blog.VoteAsync(id, "g:a", 1, default);
        await blog.VoteAsync(id, "g:a", 1, default);          // bấm lại cùng nút — không cộng thêm
        await blog.VoteAsync(id, "g:b", 1, default);
        (await blog.VoteAsync(id, "g:c", -1, default)).Should().Be(new BlogService.VoteDto(2, 1, -1));
        (await blog.VoteAsync(id, "g:a", -1, default)).Should().Be(new BlogService.VoteDto(1, 2, -1));
        (await blog.VoteAsync(id, "g:a", 0, default)).Should().Be(new BlogService.VoteDto(1, 1, 0));
        (await blog.VoteAsync(999, "g:a", 1, default)).Should().BeNull();

        var page = await blog.ListAsync(null, 1, "g:c", null, default);
        page.Items.Single().MyVote.Should().Be(-1);
    }

    [Fact(DisplayName = "List: moi nhat truoc / top theo like-dislike; phan trang co HasMore")]
    public async Task List_SortsAndPages()
    {
        var blog = NewBlog();
        var ids = new List<int>();
        for (var i = 0; i < BlogService.PageSize + 2; i++)
            ids.Add((await blog.CreateAsync(Post(), null, null, default)).Id);
        await blog.VoteAsync(ids[0], "g:a", 1, default);

        var first = await blog.ListAsync(null, 1, "g:x", null, default);
        first.Items.Should().HaveCount(BlogService.PageSize);
        first.HasMore.Should().BeTrue();
        first.Items[0].Id.Should().Be(ids[^1]);
        (await blog.ListAsync(null, 2, "g:x", null, default)).HasMore.Should().BeFalse();
        (await blog.ListAsync("top", 1, "g:x", null, default)).Items[0].Id.Should().Be(ids[0]);
    }

    [Fact(DisplayName = "Xoa: chi nguoi dang (da dang nhap) xoa duoc")]
    public async Task Delete_OnlyOwner()
    {
        var blog = NewBlog();
        var dto = await blog.CreateAsync(Post(), 7, "thong", default);
        dto.Mine.Should().BeTrue();

        (await blog.DeleteAsync(dto.Id, 8, default)).Should().BeFalse();
        (await blog.DeleteAsync(dto.Id, 7, default)).Should().BeTrue();
        (await blog.ListAsync(null, 1, "g:x", 7, default)).Items.Should().BeEmpty();
    }

    [Fact(DisplayName = "Link chia se: moi bai 1 PublicId rieng, mo lai duoc theo guid kem luot cua minh")]
    public async Task GetByPublicId()
    {
        var blog = NewBlog();
        var a = await blog.CreateAsync(Post(), null, null, default);
        var b = await blog.CreateAsync(Post(), null, null, default);
        a.PublicId.Should().NotBeEmpty().And.NotBe(b.PublicId);
        await blog.VoteAsync(b.Id, "g:x", 1, default);

        var got = await blog.GetAsync(b.PublicId, "g:x", null, default);
        got!.Id.Should().Be(b.Id);
        got.MyVote.Should().Be(1);
        (await blog.GetAsync(Guid.NewGuid(), "g:x", null, default)).Should().BeNull();
    }
}
