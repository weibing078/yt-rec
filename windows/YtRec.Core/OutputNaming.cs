namespace YtRec.Core;

/// <summary>Cross-platform side-record output filenames (mirrors the mac naming: <c>FileUtil.sanitize</c> +
/// <c>側錄_&lt;title&gt;.mp4</c> / <c>側錄_上次未收工自動修復.mp4</c>). Kept in Core so the sanitize rules are
/// unit-tested and identical to mac. The character policy is deterministic (not OS-dependent) so the output is
/// the same on any host and is always a legal Windows filename.</summary>
public static class OutputNaming
{
    /// <summary>Disaster-recovery output filename (mac AppState.swift:670). No page title is available at
    /// launch-time recovery, so both platforms use this fixed name.</summary>
    public const string RecoveryFileName = "側錄_上次未收工自動修復.mp4";

    // Illegal / risky filename chars → replaced with a space. Superset of mac's set ("/ : \ \0 \n \r \t",
    // all < 0x20 or listed below) plus the remaining Windows-reserved printables (< > " | ? *), so the same
    // input yields the same, Windows-legal, name on every host.
    private static bool IsInvalid(char c) =>
        c < ' ' || c is '<' or '>' or ':' or '"' or '/' or '\\' or '|' or '?' or '*';

    /// <summary>檔名消毒（對齊 mac FileUtil.sanitize）：不合法/易出事字元換成空白、去頭尾空白、空→未命名、限長。</summary>
    public static string Sanitize(string? name, int maxLength = 60)
    {
        var raw = name ?? "";
        var sb = new System.Text.StringBuilder(raw.Length);
        foreach (var c in raw) sb.Append(IsInvalid(c) ? ' ' : c);
        var s = sb.ToString().Trim();
        if (s.Length == 0) s = "未命名";
        if (s.Length > maxLength) s = s.Substring(0, maxLength);
        return s;
    }

    /// <summary>Side-record filename <c>側錄_&lt;sanitized title&gt;.mp4</c> (mac AppState.swift:529). A blank /
    /// unknown title (title never arrived) falls back to <c>側錄.mp4</c> — the containing job folder already
    /// carries the timestamp + video id.</summary>
    public static string SideRecordFileName(string? title)
    {
        var t = (title ?? "").Trim();
        return t.Length == 0 ? "側錄.mp4" : $"側錄_{Sanitize(t)}.mp4";
    }
}
