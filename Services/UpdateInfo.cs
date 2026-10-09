namespace CleanMaster.Services;

/// <summary>远程更新信息（来自 GitHub Release）。</summary>
public class UpdateInfo
{
    public string Version { get; set; } = "";          // 如 "2.5.0"
    public string TagName { get; set; } = "";          // 如 "v2.5.0"
    public string DownloadUrl { get; set; } = "";      // 安装包下载直链
    public string FileName { get; set; } = "";         // 安装包文件名
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = "";           // 十六进制 sha256（可选）
    public string ReleasePageUrl { get; set; } = "";   // 供用户手动前往
    public string Notes { get; set; } = "";            // 发布说明
}
