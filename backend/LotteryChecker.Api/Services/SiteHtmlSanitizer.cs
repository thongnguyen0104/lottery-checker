using AngleSharp.Dom;
using Ganss.Xss;

namespace LotteryChecker.Api.Services;

/// <summary>
/// Lọc HTML từ trình soạn TipTap trước khi lưu — site con hiện HTML này cho khách bằng dangerouslySetInnerHTML,
/// nên chỉ giữ đúng các thẻ TipTap sinh ra; ảnh chỉ được trỏ về ảnh của site (không nhúng ảnh ngoài để theo dõi người xem).
/// </summary>
public class SiteHtmlSanitizer
{
    private readonly HtmlSanitizer _s;

    public SiteHtmlSanitizer()
    {
        _s = new HtmlSanitizer();
        _s.AllowedTags.Clear();
        foreach (var t in new[] { "p", "br", "hr", "h2", "h3", "h4", "strong", "b", "em", "i", "u", "s",
                                  "a", "ul", "ol", "li", "blockquote", "img", "code", "pre" })
            _s.AllowedTags.Add(t);
        _s.AllowedAttributes.Clear();
        foreach (var a in new[] { "href", "src", "alt", "title" }) _s.AllowedAttributes.Add(a);
        _s.AllowedCssProperties.Clear();
        _s.AllowedClasses.Clear();
        _s.AllowedSchemes.Clear();
        foreach (var sc in new[] { "http", "https", "mailto", "tel" }) _s.AllowedSchemes.Add(sc);
        _s.UriAttributes.Clear();
        _s.UriAttributes.Add("href");
        _s.UriAttributes.Add("src");

        _s.FilterUrl += (_, e) =>
        {
            if (e.Tag is { TagName: "IMG" } && !e.OriginalUrl.StartsWith(SiteService.ImageUrlPrefix, StringComparison.Ordinal))
                e.SanitizedUrl = null;
        };
        _s.PostProcessNode += (_, e) =>
        {
            if (e.Node is not IElement el) return;
            if (el.TagName == "A")
            {
                el.SetAttribute("rel", "nofollow noopener noreferrer");
                el.SetAttribute("target", "_blank");
            }
            else if (el.TagName == "IMG" && !el.HasAttribute("src"))
            {
                el.Remove();
            }
        };
        // "target" do chính PostProcessNode thêm — vẫn phải cho phép thì mới không bị lọc mất ở bước sau.
        _s.AllowedAttributes.Add("rel");
        _s.AllowedAttributes.Add("target");
    }

    public string Sanitize(string? html) => string.IsNullOrWhiteSpace(html) ? "" : _s.Sanitize(html).Trim();

    /// <summary>Chữ thuần (bỏ thẻ) — dùng cho đoạn tóm tắt / đăng chéo sang Blog.</summary>
    public static string ToText(string html)
    {
        var doc = new HtmlAgilityPack.HtmlDocument();
        doc.LoadHtml(html.Replace("</p>", "</p>\n").Replace("<br>", "\n"));
        return System.Net.WebUtility.HtmlDecode(doc.DocumentNode.InnerText).Trim();
    }
}
