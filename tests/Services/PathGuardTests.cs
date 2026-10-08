using CleanMaster.Services;

namespace CleanMaster.Tests.Services;

public class PathGuardTests
{
    [Fact]
    public void EmptyPath_ReturnsFalse()
    {
        Assert.False(PathGuard.IsDeletable("", out var reason));
        Assert.NotEmpty(reason);
    }

    [Fact]
    public void WhitespacePath_ReturnsFalse()
    {
        Assert.False(PathGuard.IsDeletable("   ", out _));
    }

    [Fact]
    public void DriveRoot_ReturnsFalse()
    {
        var root = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.System)) ?? "C:\\";
        Assert.False(PathGuard.IsDeletable(root, out var reason));
        Assert.Contains("盘符根", reason);
    }

    [Fact]
    public void WindowsDirectory_ReturnsFalse()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        Assert.False(PathGuard.IsDeletable(windows, out var reason));
        Assert.Contains("受保护", reason);
    }

    [Fact]
    public void UserProfile_ReturnsFalse()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.False(PathGuard.IsDeletable(profile, out _));
    }

    [Fact]
    public void TempSubdirectory_ReturnsTrue()
    {
        var tempSub = Path.Combine(Path.GetTempPath(), "CleanMasterGuardTest_" + Guid.NewGuid());
        Assert.True(PathGuard.IsDeletable(tempSub, out _));
    }

    [Fact]
    public void InvalidPath_ReturnsFalse()
    {
        Assert.False(PathGuard.IsDeletable("\0invalid", out _));
    }
}

public class UrlGuardTests
{
    [Theory]
    [InlineData("https://example.com")]
    [InlineData("http://example.com/path?q=1")]
    public void HttpHttps_ReturnsTrue(string url)
    {
        Assert.True(UrlGuard.TryGetWebUri(url, out var uri));
        Assert.NotNull(uri);
    }

    [Theory]
    [InlineData("file:///C:/Windows/system32/calc.exe")]
    [InlineData("javascript:alert(1)")]
    [InlineData("ftp://example.com")]
    [InlineData("not-a-url")]
    [InlineData("")]
    public void NonHttp_ReturnsFalse(string url)
    {
        Assert.False(UrlGuard.TryGetWebUri(url, out _));
    }
}
