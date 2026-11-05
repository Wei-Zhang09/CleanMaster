using CleanMaster.Services;
using Microsoft.Win32;

namespace CleanMaster.Tests.Services;

public class StartupApprovalTests : IDisposable
{
    private readonly string _testValueName = "CleanMasterTest_" + Guid.NewGuid().ToString("N")[..8];
    private readonly string _runKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private readonly string _approvedKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private readonly SoftwareService _service = new();

    public void Dispose()
    {
        // 清掉测试写入的 Run 值 / 审批值，避免污染本机真实启动项
        try
        {
            using var run = Registry.CurrentUser.OpenSubKey(_runKeyPath, writable: true);
            run?.DeleteValue(_testValueName, false);
            run?.DeleteValue("Disabled_" + _testValueName, false);
        }
        catch { }

        try
        {
            using var approved = Registry.CurrentUser.OpenSubKey(_approvedKeyPath, writable: true);
            approved?.DeleteValue(_testValueName, false);
        }
        catch { }
    }

    // ---------- 纯函数：12 字节状态块 ----------

    [Fact]
    public void BuildBlob_NewEntry_SetsCanonicalStates()
    {
        var disabled = StartupApproval.BuildBlob(null, enabled: false);
        var enabled = StartupApproval.BuildBlob(null, enabled: true);

        Assert.NotNull(disabled);
        Assert.NotNull(enabled);
        Assert.Equal(12, disabled!.Length);
        Assert.Equal(0x03, disabled[0]);
        Assert.Equal(0x02, enabled![0]);
    }

    [Fact]
    public void BuildBlob_PreservesTimestampBytes()
    {
        var existing = new byte[12];
        existing[0] = 0x02;
        for (var i = 4; i < 12; i++) existing[i] = (byte)(0xA0 + i);

        var disabled = StartupApproval.BuildBlob(existing, enabled: false)!;

        Assert.Equal(0x03, disabled[0]);
        for (var i = 4; i < 12; i++)
            Assert.Equal(existing[i], disabled[i]);
    }

    [Theory]
    [InlineData(0x06, 0x07)] // 变体保持变体：0x06 启用 → 0x07 禁用
    [InlineData(0x07, 0x06)] // 0x07 禁用 → 0x06 启用
    [InlineData(0x02, 0x03)]
    [InlineData(0x03, 0x02)]
    public void BuildBlob_PreservesVariantFlavor(byte current, byte expected)
    {
        var existing = new byte[12];
        existing[0] = current;

        var toggled = StartupApproval.BuildBlob(existing, enabled: current is 0x03 or 0x07)!;

        Assert.Equal(expected, toggled[0]);
    }

    [Fact]
    public void BuildBlob_ExistingBlobTooShort_ReturnsNull()
    {
        // 格式不认识就不要改：宁可失败，也不要把用户的记录写坏
        Assert.Null(StartupApproval.BuildBlob(new byte[4], enabled: false));
    }

    [Fact]
    public void ParseState_MapsAllKnownStates()
    {
        static byte[] Blob(byte state) => new byte[] { state, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };

        Assert.Equal(StartupApprovalState.Enabled, StartupApproval.ParseState(Blob(0x02)));
        Assert.Equal(StartupApprovalState.Enabled, StartupApproval.ParseState(Blob(0x06)));
        Assert.Equal(StartupApprovalState.Disabled, StartupApproval.ParseState(Blob(0x03)));
        Assert.Equal(StartupApprovalState.Disabled, StartupApproval.ParseState(Blob(0x07)));
        Assert.Equal(StartupApprovalState.Unknown, StartupApproval.ParseState(Blob(0x09)));
        Assert.Equal(StartupApprovalState.Unknown, StartupApproval.ParseState(null));
    }

    [Fact]
    public void IsSupported_RunKeysYes_RunOnceNo()
    {
        Assert.True(StartupApproval.IsSupported("HKCU"));
        Assert.True(StartupApproval.IsSupported("HKLM"));
        Assert.True(StartupApproval.IsSupported("HKLM64"));
        Assert.True(StartupApproval.IsSupported("StartupFolder"));
        Assert.False(StartupApproval.IsSupported("HKCU-Once"));
        Assert.False(StartupApproval.IsSupported("HKLM-Once"));
    }

    // ---------- 真实注册表读写 ----------

    [Fact]
    public void Write_ThenRead_RoundTripsThroughRealRegistry()
    {
        Assert.True(StartupApproval.Write("HKCU", _testValueName, enabled: false));
        Assert.Equal(StartupApprovalState.Disabled, StartupApproval.Read("HKCU", _testValueName));

        using (var key = Registry.CurrentUser.OpenSubKey(_approvedKeyPath))
        {
            var blob = key?.GetValue(_testValueName) as byte[];
            Assert.NotNull(blob);
            Assert.Equal(12, blob!.Length);
            Assert.Equal(0x03, blob[0]);
        }

        Assert.True(StartupApproval.Write("HKCU", _testValueName, enabled: true));
        Assert.Equal(StartupApprovalState.Enabled, StartupApproval.Read("HKCU", _testValueName));
    }

    // ---------- 与 StartupItem / SoftwareService 的联动 ----------

    [Fact]
    public void GetStartupItems_ReflectsApprovalStateAndDisableTogglesIt()
    {
        using (var run = Registry.CurrentUser.CreateSubKey(_runKeyPath))
            run.SetValue(_testValueName, @"cmd.exe /c exit", RegistryValueKind.String);

        var item = _service.GetStartupItems().FirstOrDefault(i => i.ValueName == _testValueName);
        Assert.NotNull(item);
        Assert.True(item!.IsEnabled);

        Assert.True(_service.DisableStartupItem(item));

        // 注册表里应出现 0x03 的审批记录，且列表回读为禁用
        using (var key = Registry.CurrentUser.OpenSubKey(_approvedKeyPath))
        {
            var blob = key?.GetValue(_testValueName) as byte[];
            Assert.NotNull(blob);
            Assert.Equal(0x03, blob![0]);
        }

        var afterDisable = _service.GetStartupItems().First(i => i.ValueName == _testValueName);
        Assert.False(afterDisable.IsEnabled);

        Assert.True(_service.EnableStartupItem(afterDisable));

        var afterEnable = _service.GetStartupItems().First(i => i.ValueName == _testValueName);
        Assert.True(afterEnable.IsEnabled);
    }

    [Fact]
    public void GetStartupItems_LegacyPrefixedEntry_StillReportedAsDisabled()
    {
        // 迁移兼容：老版本用 "Disabled_" 前缀禁用，新版本必须还能识别成"已禁用"
        using (var run = Registry.CurrentUser.CreateSubKey(_runKeyPath))
            run.SetValue("Disabled_" + _testValueName, @"cmd.exe /c exit", RegistryValueKind.String);

        var item = _service.GetStartupItems().FirstOrDefault(i => i.Name == _testValueName);

        Assert.NotNull(item);
        Assert.False(item!.IsEnabled);
        Assert.True(item.IsLegacyDisabled);
        Assert.Equal("Disabled_" + _testValueName, item.ValueName);
    }

    [Fact]
    public void EnableStartupItem_LegacyPrefixedEntry_RenamesBackAndClearsLegacyFlag()
    {
        using (var run = Registry.CurrentUser.CreateSubKey(_runKeyPath))
            run.SetValue("Disabled_" + _testValueName, @"cmd.exe /c exit", RegistryValueKind.String);

        var item = _service.GetStartupItems().First(i => i.Name == _testValueName);
        Assert.True(_service.EnableStartupItem(item));

        using (var run = Registry.CurrentUser.OpenSubKey(_runKeyPath))
        {
            Assert.NotNull(run?.GetValue(_testValueName));
            Assert.Null(run?.GetValue("Disabled_" + _testValueName));
        }

        var afterEnable = _service.GetStartupItems().First(i => i.ValueName == _testValueName);
        Assert.True(afterEnable.IsEnabled);
        Assert.False(afterEnable.IsLegacyDisabled);
    }

    [Fact]
    public void GetStartupItemSignature_KnownSystemBinary_ReportsSigned()
    {
        // notepad.exe 由微软签名，用来验证"有签名"这条路径真的走通
        var notepad = Path.Combine(Environment.SystemDirectory, "notepad.exe");
        if (!File.Exists(notepad)) return;

        var item = new StartupItem { Name = "notepad", Command = notepad };

        Assert.Equal("Signed", _service.GetStartupItemSignature(item));
    }

    [Fact]
    public void GetStartupItemSignature_NonexistentPath_ReportsUnknown()
    {
        var item = new StartupItem { Name = "ghost", Command = @"C:\NoSuchDir\ghost.exe" };
        Assert.Equal("Unknown", _service.GetStartupItemSignature(item));
    }

    [Fact]
    public void AuthenticodeChecker_UnsignedFile_ReportsUnsigned()
    {
        // 本项目自己构建的 DLL 没有 Authenticode 签名（Get-AuthenticodeSignature 也是 NotSigned），
        // 用来验证"未签名"这条路径不会把正常文件误报成已签名。
        var unsigned = Path.Combine(AppContext.BaseDirectory, "CleanMaster.dll");
        if (!File.Exists(unsigned)) return;

        Assert.Equal(SignatureState.Unsigned, AuthenticodeChecker.Check(unsigned));
    }

    [Fact]
    public void AuthenticodeChecker_SignedSystemBinary_ReportsSigned()
    {
        var notepad = Path.Combine(Environment.SystemDirectory, "notepad.exe");
        if (!File.Exists(notepad)) return;

        Assert.Equal(SignatureState.Signed, AuthenticodeChecker.Check(notepad));
    }
}
