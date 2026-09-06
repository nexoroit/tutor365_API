using System.Net;

namespace Tutor365.Application.Common;

/// <summary>Branded, table-based HTML email layout (renders in Outlook/Gmail/Apple Mail) plus the platform's transactional emails.</summary>
public static class EmailTemplates
{
    private const string Teal = "#0D9488";
    private const string Navy = "#1E3A5F";
    private const string Ink = "#1F2937";
    private const string Muted = "#6B7280";

    public static string E(string? s) => WebUtility.HtmlEncode(s ?? "");

    public static (string Html, string Text) Layout(string appName, string appUrl, string supportEmail, string title, string preheader, string bodyHtml, string bodyText, (string Label, string Url)? cta = null)
    {
        var button = cta is { } c
            ? $@"<table role=""presentation"" cellspacing=""0"" cellpadding=""0"" border=""0"" style=""margin:24px 0""><tr><td bgcolor=""{Teal}"" style=""border-radius:10px""><a href=""{c.Url}"" style=""display:inline-block;padding:13px 26px;font-family:Segoe UI,Helvetica,Arial,sans-serif;font-size:15px;font-weight:700;color:#ffffff;text-decoration:none;border-radius:10px"">{E(c.Label)}</a></td></tr></table>"
            : "";
        var html = $@"<!DOCTYPE html><html lang=""en""><head><meta charset=""utf-8""><meta name=""viewport"" content=""width=device-width,initial-scale=1""><title>{E(title)}</title></head>
<body style=""margin:0;padding:0;background:#F3F4F6;font-family:Segoe UI,Helvetica,Arial,sans-serif;color:{Ink}"">
<div style=""display:none;max-height:0;overflow:hidden;color:transparent"">{E(preheader)}</div>
<table role=""presentation"" width=""100%"" cellspacing=""0"" cellpadding=""0"" border=""0"" style=""background:#F3F4F6""><tr><td align=""center"" style=""padding:28px 12px"">
<table role=""presentation"" width=""600"" cellspacing=""0"" cellpadding=""0"" border=""0"" style=""max-width:600px;width:100%"">
  <tr><td style=""background:linear-gradient(135deg,{Navy},{Teal});background-color:{Navy};border-radius:16px 16px 0 0;padding:22px 28px"">
    <table role=""presentation"" cellspacing=""0"" cellpadding=""0"" border=""0""><tr>
      <td style=""width:40px;height:40px;background:#ffffff;border-radius:10px;text-align:center;vertical-align:middle;font-size:22px;line-height:40px"">🎯</td>
      <td style=""padding-left:12px;font-size:24px;font-weight:800;color:#ffffff;letter-spacing:-0.5px"">{E(appName)}</td>
    </tr></table>
  </td></tr>
  <tr><td style=""background:#ffffff;padding:32px 28px;border-left:1px solid #E5E7EB;border-right:1px solid #E5E7EB"">
    <h1 style=""margin:0 0 14px;font-size:22px;line-height:1.3;color:{Ink}"">{E(title)}</h1>
    <div style=""font-size:15px;line-height:1.6;color:{Ink}"">{bodyHtml}</div>
    {button}
  </td></tr>
  <tr><td style=""background:#F9FAFB;border:1px solid #E5E7EB;border-top:0;border-radius:0 0 16px 16px;padding:18px 28px;font-size:12px;line-height:1.6;color:{Muted}"">
    You're receiving this because you have a {E(appName)} account. Questions? Email <a href=""mailto:{supportEmail}"" style=""color:{Teal}"">{supportEmail}</a>.<br>
    <a href=""{appUrl}"" style=""color:{Teal}"">{appUrl.Replace("https://", "").Replace("http://", "")}</a> &middot; Your personal GCSE tutor
  </td></tr>
</table></td></tr></table></body></html>";
        var text = $"{title}\n\n{bodyText}\n{(cta is { } t ? $"\n{t.Label}: {t.Url}\n" : "")}\n--\n{appName} · {appUrl} · {supportEmail}";
        return (html, text);
    }

    public static string CodeBox(string code) =>
        $@"<div style=""margin:20px 0;padding:18px;text-align:center;background:#F0FDFA;border:1px dashed #99F6E4;border-radius:12px;font-size:34px;font-weight:800;letter-spacing:10px;color:{Navy}"">{E(code)}</div>";

    public static string Stat(string label, string value) =>
        $@"<td style=""padding:10px 12px;background:#F9FAFB;border-radius:10px;text-align:center""><div style=""font-size:11px;text-transform:uppercase;letter-spacing:.5px;color:{Muted}"">{E(label)}</div><div style=""font-size:20px;font-weight:800;color:{Ink}"">{E(value)}</div></td>";

    public static string Para(string text) => $@"<p style=""margin:0 0 12px"">{text}</p>";

    public static string List(IEnumerable<string> items) =>
        "<ul style=\"margin:0 0 12px 18px;padding:0\">" + string.Join("", items.Select(i => $"<li style=\"margin:4px 0\">{i}</li>")) + "</ul>";

    public static string Badge(string text, string colour = Teal) =>
        $@"<span style=""display:inline-block;padding:2px 10px;border-radius:999px;background:{colour}1a;color:{colour};font-size:12px;font-weight:700"">{E(text)}</span>";
}
