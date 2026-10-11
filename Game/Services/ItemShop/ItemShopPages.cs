using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;

namespace Navislamia.Game.Services.ItemShop;

/// <summary>An answer of the shop page: a status, a page, or a redirection.</summary>
public sealed record ItemShopWebResponse(int Status, string Html, string Location = null)
{
    public static ItemShopWebResponse Page(string html) => new(200, html);

    public static ItemShopWebResponse Refused(string html) => new(403, html);

    public static ItemShopWebResponse SeeOther(string location) => new(303, null, location);
}

/// <summary>
/// The HTML of the shop. The client shows it in its embedded Internet Explorer window (<c>SetNavigate</c>, official
/// client SGameInterface.cpp:2950), so the page is plain server-rendered HTML with forms and inline styles, no script,
/// sized for a window of about 800 pixels.
/// </summary>
public static class ItemShopPages
{
    public const string ShopPath = "/shop";
    public const string BuyPath = "/buy";

    public static string Message(string title, string text) => Document(title,
        $"<div class=\"box\"><h1>{Encode(title)}</h1><p>{Encode(text)}</p></div>");

    public static string Shop(ItemShopSession session, ItemShopCatalog catalog, string category, long balance,
        string notice)
    {
        var current = catalog.Categories.Contains(category, StringComparer.Ordinal)
            ? category
            : catalog.Categories.FirstOrDefault() ?? string.Empty;
        var body = new StringBuilder();
        body.Append("<div class=\"top\"><span class=\"title\">Item Shop</span>")
            .Append("<span class=\"who\">").Append(Encode(session.CharacterName)).Append(" &middot; <b>")
            .Append(Points(balance)).Append("</b> points</span></div>");
        if (!string.IsNullOrEmpty(notice))
        {
            body.Append("<div class=\"notice\">").Append(Encode(notice)).Append("</div>");
        }

        body.Append("<div class=\"tabs\">");
        foreach (var name in catalog.Categories)
        {
            var css = name == current ? "tab on" : "tab";
            body.Append("<a class=\"").Append(css).Append("\" href=\"").Append(ShopPath).Append("?s=")
                .Append(Url(session.Token)).Append("&amp;c=").Append(Url(name)).Append("\">").Append(Encode(name))
                .Append("</a>");
        }

        body.Append("</div><table><tr><th>Item</th><th>Qty</th><th>Price</th><th></th></tr>");
        foreach (var entry in catalog.Entries.Where(entry => entry.Category == current))
        {
            body.Append("<tr><td>").Append(Encode(entry.Name)).Append("</td><td class=\"n\">")
                .Append(entry.Count.ToString(CultureInfo.InvariantCulture)).Append("</td><td class=\"n\">")
                .Append(Points(entry.Price)).Append("</td><td><form method=\"post\" action=\"").Append(BuyPath)
                .Append("\"><input type=\"hidden\" name=\"s\" value=\"").Append(Encode(session.Token))
                .Append("\"><input type=\"hidden\" name=\"c\" value=\"").Append(Encode(current))
                .Append("\"><input type=\"hidden\" name=\"id\" value=\"")
                .Append(entry.Id.ToString(CultureInfo.InvariantCulture)).Append("\"><input type=\"submit\" value=\"Buy\"")
                .Append(entry.Price > balance ? " disabled" : string.Empty).Append("></form></td></tr>");
        }

        body.Append("</table><p class=\"foot\">Purchases go to the Item Box of your inventory, ")
            .Append("for every character of your account.</p>");
        return Document("Item Shop", body.ToString());
    }

    public static string Points(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

    private static string Encode(string text) => WebUtility.HtmlEncode(text ?? string.Empty);

    private static string Url(string text) => Uri.EscapeDataString(text ?? string.Empty);

    private static string Document(string title, string body) =>
        "<!DOCTYPE html><html><head><meta charset=\"utf-8\">"
        + "<meta http-equiv=\"X-UA-Compatible\" content=\"IE=edge\"><title>" + Encode(title) + "</title><style>"
        + "body{margin:0;background:#1d1a16;color:#e8dcc2;font:13px Tahoma,Verdana,sans-serif}"
        + ".top{background:#3a2f22;padding:10px 14px;border-bottom:2px solid #8a6d3b}"
        + ".title{font-size:18px;font-weight:bold;color:#f2c96b}.who{float:right;padding-top:3px}"
        + ".notice{margin:10px 14px;padding:8px;background:#2f3a22;border:1px solid #6b8a3b}"
        + ".tabs{padding:10px 14px 0}.tab{display:inline-block;padding:5px 10px;margin:0 4px 4px 0;"
        + "background:#2c251d;color:#e8dcc2;text-decoration:none;border:1px solid #5a4a35}"
        + ".tab.on{background:#8a6d3b;color:#1d1a16;font-weight:bold}"
        + "table{margin:8px 14px;border-collapse:collapse;width:760px}"
        + "th{text-align:left;background:#3a2f22;padding:6px}td{padding:5px 6px;border-bottom:1px solid #3a2f22}"
        + "td.n{text-align:right;width:70px}form{margin:0}"
        + ".foot{margin:6px 14px;color:#a89878}.box{margin:40px auto;width:460px;padding:16px;"
        + "background:#2c251d;border:1px solid #8a6d3b}h1{font-size:16px;color:#f2c96b;margin-top:0}"
        + "</style></head><body>" + body + "</body></html>";
}
