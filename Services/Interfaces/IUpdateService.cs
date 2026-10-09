using CleanMaster.Services;

namespace CleanMaster.Services.Interfaces;

public interface IUpdateService
{
    /// <summary>检查 GitHub Release 是否有更新版本。无更新返回 null。</summary>
    Task<UpdateInfo?> CheckForUpdateAsync();

    /// <summary>下载安装包到临时目录，校验 SHA256。返回本地文件路径。</summary>
    Task<string> DownloadAsync(UpdateInfo info, IProgress<double>? progress = null, CancellationToken ct = default);

    /// <summary>静默运行安装包并退出当前进程（安装器接管覆盖与重启）。</summary>
    void InstallAndRestart(string installerPath);
}
