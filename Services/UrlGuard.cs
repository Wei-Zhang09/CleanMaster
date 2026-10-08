namespace CleanMaster.Services;

/// <summary>
/// 校验待打开的 URL 是否安全（仅允许 http/https），防止 file:// 或自定义协议被系统直接执行。
/// </summary>
public static class UrlGuard
{
    public static bool TryGetWebUri(string? text, out Uri uri)
    {
        if (Uri.TryCreate(text, UriKind.Absolute, out var u) &&
            (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps))
        {
            uri = u;
            return true;
        }
        uri = null!;
        return false;
    }
}
