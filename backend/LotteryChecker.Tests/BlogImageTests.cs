using FluentAssertions;
using LotteryChecker.Api.Data;
using LotteryChecker.Api.Models;
using LotteryChecker.Api.Services;
using Microsoft.EntityFrameworkCore;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;
using NewPost = LotteryChecker.Api.Services.BlogService.NewPost;

namespace LotteryChecker.Tests;

// Ảnh Blog: gắn ảnh vào bài đúng chủ/đúng hạn, xoá bài thì ảnh thành mồ côi; xử lý ảnh bỏ EXIF + thu nhỏ.
public class BlogImageTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 10, 0, 0, TimeSpan.Zero);

    private static (BlogService Blog, AppDbContext Db, MovableTimeProvider Clock) New()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var clock = new MovableTimeProvider(Now);
        return (new BlogService(db, clock), db, clock);
    }

    private static NewPost Post(params int[] imageIds) =>
        new("Trúng giải tám", "Hôm qua dò vé trúng giải tám, vui ghê!", BlogAuthorMode.Anonymous, null, imageIds);

    private static string Key() => $"blog/2026/09/{Guid.NewGuid():D}.webp";

    [Fact(DisplayName = "Dang bai: gan anh theo thu tu gui len, danh sach va xem 1 bai deu co anh")]
    public async Task Create_AttachesImagesInOrder()
    {
        var (blog, _, _) = New();
        var a = await blog.AddImageAsync(Key(), "g:a", 100, default);
        var b = await blog.AddImageAsync(Key(), "g:a", 100, default);

        var post = await blog.CreateAsync(Post(b.Id, a.Id), null, null, default);
        post.Images.Should().Equal(b.Url, a.Url);
        (await blog.ListAsync(null, 1, "g:x", null, default)).Items.Single().Images.Should().Equal(b.Url, a.Url);
        (await blog.GetAsync(post.Id, "g:x", null, default))!.Images.Should().Equal(b.Url, a.Url);
        (await blog.IsPublishedImageAsync(b.Url["/api/blog/images/".Length..], default)).Should().BeTrue();
    }

    [Fact(DisplayName = "Anh cua nguoi khac / da gan bai / qua han cho thi khong gan duoc")]
    public async Task ImageError_Rules()
    {
        var (blog, _, clock) = New();
        var mine = await blog.AddImageAsync(Key(), "g:a", 100, default);
        var other = await blog.AddImageAsync(Key(), "g:b", 100, default);

        (await blog.ImageErrorAsync(null, "g:a", false, default)).Should().BeNull();
        (await blog.ImageErrorAsync([mine.Id], "g:a", false, default)).Should().BeNull();
        (await blog.ImageErrorAsync([mine.Id, other.Id], "g:a", false, default)).Should().NotBeNull();
        (await blog.ImageErrorAsync([999], "g:a", false, default)).Should().NotBeNull();

        await blog.CreateAsync(Post(mine.Id), null, null, default);
        (await blog.ImageErrorAsync([mine.Id], "g:a", false, default)).Should().NotBeNull();   // đã gắn bài

        clock.Now += BlogService.PendingImageTtl + TimeSpan.FromMinutes(1);
        (await blog.ImageErrorAsync([other.Id], "g:b", false, default)).Should().NotBeNull();  // quá hạn chờ
    }

    [Fact(DisplayName = "Validate: toi da 4 anh, khong trung id")]
    public void Validate_ImageCount()
    {
        BlogService.Validate(Post(1), null, false).Should().BeNull();
        BlogService.Validate(Post(1, 2), null, false).Should().NotBeNull();
        BlogService.Validate(Post(1, 1), null, false).Should().NotBeNull();
    }

    [Fact(DisplayName = "Xoa bai: anh thanh mo coi, khong con phuc vu, worker tim thay de xoa")]
    public async Task Delete_OrphansImages()
    {
        var (blog, _, clock) = New();
        var img = await blog.AddImageAsync(Key(), "u:7", 100, default);
        var post = await blog.CreateAsync(Post(img.Id), 7, "thong", default);

        (await blog.DeleteAsync(post.Id, 7, default)).Should().BeTrue();
        var key = img.Url["/api/blog/images/".Length..];
        (await blog.IsPublishedImageAsync(key, default)).Should().BeFalse();

        clock.Now += BlogService.PendingImageTtl + TimeSpan.FromMinutes(1);
        var orphans = await blog.OrphanImagesAsync(clock.GetUtcNow().UtcDateTime - BlogService.PendingImageTtl, 10, default);
        orphans.Select(x => x.Key).Should().Equal(key);
    }

    [Fact(DisplayName = "Dem anh cho dang theo nguoi upload, anh da dang khong tinh")]
    public async Task PendingCount()
    {
        var (blog, _, _) = New();
        var a = await blog.AddImageAsync(Key(), "g:a", 100, default);
        await blog.AddImageAsync(Key(), "g:a", 100, default);
        await blog.AddImageAsync(Key(), "g:b", 100, default);
        (await blog.PendingImageCountAsync("g:a", default)).Should().Be(2);
        await blog.CreateAsync(Post(a.Id), null, null, default);
        (await blog.PendingImageCountAsync("g:a", default)).Should().Be(1);
    }

    [Fact(DisplayName = "Xu ly anh: ra WebP, thu canh dai ve 1600px, bo EXIF (GPS)")]
    public void Process_ResizesAndStripsExif()
    {
        using var src = new Image<Rgba32>(3200, 2400, Color.Orange);
        src.Metadata.ExifProfile = new ExifProfile();
        src.Metadata.ExifProfile.SetValue(ExifTag.GPSLatitudeRef, "N");
        src.Metadata.ExifProfile.SetValue(ExifTag.Software, "phone");
        using var ms = new MemoryStream();
        src.Save(ms, new JpegEncoder());

        var webp = BlogImageStorage.Process(ms.ToArray());

        Image.DetectFormat(webp).Name.Should().Be("Webp");
        using var outImg = Image.Load(webp);
        outImg.Width.Should().Be(1600);
        outImg.Height.Should().Be(1200);
        outImg.Metadata.ExifProfile.Should().BeNull();
    }

    [Fact(DisplayName = "Xu ly anh: file khong phai anh bi tu choi")]
    public void Process_RejectsGarbage()
    {
        var act = () => BlogImageStorage.Process("không phải ảnh"u8.ToArray());
        act.Should().Throw<InvalidBlogImageException>();
    }
}
