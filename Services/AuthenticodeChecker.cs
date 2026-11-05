using System.IO;
using System.Runtime.InteropServices;

namespace CleanMaster.Services;

public enum SignatureState
{
    /// <summary>检查失败或无法判断（不当作"无签名"，避免误伤正常程序）。</summary>
    Unknown,
    /// <summary>有 Authenticode 签名（内嵌或系统目录清单）。</summary>
    Signed,
    /// <summary>既没有内嵌签名，也不在任何系统目录清单里。</summary>
    Unsigned
}

/// <summary>
/// 判断文件是否带 Authenticode 签名。
///
/// 为什么不用 <c>X509Certificate.CreateFromSignedFile</c>：实测它对签名正常的系统文件也会抛
/// <c>CryptographicException</c>（.NET 实现只读内嵌签名，且部分文件解析失败），会把 notepad.exe
/// 判成"无签名"。给用户看的安全标记必须经得起核对，所以这里走 WinVerifyTrust，
/// 并在"无内嵌签名"时再查系统目录清单（catalog），避免把 catalog 签名的系统文件误报成未签名。
/// </summary>
public static class AuthenticodeChecker
{
    private const int TrustENoSignature = unchecked((int)0x800B0100);
    private const uint WtdUiNone = 2;
    private const uint WtdRevokeNone = 0;
    private const uint WtdChoiceFile = 1;
    private const uint WtdStateActionVerify = 1;
    private const uint WtdStateActionClose = 2;
    private const uint WtdRevocationCheckNone = 0x10;
    private const uint WtdCacheOnlyUrlRetrieval = 0x1000;

    private static readonly Guid WinTrustActionGenericVerifyV2 =
        new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    public static SignatureState Check(string path)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrEmpty(path) || !File.Exists(path))
            return SignatureState.Unknown;

        var hr = VerifyEmbeddedSignature(path);
        if (hr == 0) return SignatureState.Signed;

        if (hr == TrustENoSignature)
            return IsInSystemCatalog(path) ? SignatureState.Signed : SignatureState.Unsigned;

        return SignatureState.Unknown;
    }

    /// <summary>内嵌签名校验。返回 WinVerifyTrust 的 HRESULT，0 表示有效。</summary>
    private static int VerifyEmbeddedSignature(string path)
    {
        var fileInfoPtr = IntPtr.Zero;
        var pathPtr = IntPtr.Zero;
        var dataPtr = IntPtr.Zero;

        try
        {
            pathPtr = Marshal.StringToHGlobalUni(path);

            var fileInfo = new WinTrustFileInfo
            {
                cbStruct = (uint)Marshal.SizeOf<WinTrustFileInfo>(),
                pcwszFilePath = pathPtr,
                hFile = IntPtr.Zero,
                pgKnownSubject = IntPtr.Zero
            };
            fileInfoPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
            Marshal.StructureToPtr(fileInfo, fileInfoPtr, false);

            var data = new WinTrustData
            {
                cbStruct = (uint)Marshal.SizeOf<WinTrustData>(),
                pPolicyCallbackData = IntPtr.Zero,
                pSIPClientData = IntPtr.Zero,
                dwUIChoice = WtdUiNone,
                fdwRevocationChecks = WtdRevokeNone,
                dwUnionChoice = WtdChoiceFile,
                pFile = fileInfoPtr,
                dwStateAction = WtdStateActionVerify,
                hWVTStateData = IntPtr.Zero,
                pwszURLReference = IntPtr.Zero,
                dwProvFlags = WtdRevocationCheckNone | WtdCacheOnlyUrlRetrieval,
                dwUIContext = 0
            };
            dataPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustData>());
            Marshal.StructureToPtr(data, dataPtr, false);

            var action = WinTrustActionGenericVerifyV2;
            var hr = WinVerifyTrust(IntPtr.Zero, ref action, dataPtr);

            // 释放校验状态，否则会泄漏（WinVerifyTrust 文档要求配对的 CLOSE 调用）
            var close = Marshal.PtrToStructure<WinTrustData>(dataPtr);
            close.dwStateAction = WtdStateActionClose;
            Marshal.StructureToPtr(close, dataPtr, false);
            WinVerifyTrust(IntPtr.Zero, ref action, dataPtr);

            return hr;
        }
        catch (Exception ex)
        {
            CleanMaster.App.LogError("AuthenticodeChecker.VerifyEmbeddedSignature", ex);
            return -1;
        }
        finally
        {
            if (dataPtr != IntPtr.Zero) Marshal.FreeHGlobal(dataPtr);
            if (fileInfoPtr != IntPtr.Zero) Marshal.FreeHGlobal(fileInfoPtr);
            if (pathPtr != IntPtr.Zero) Marshal.FreeHGlobal(pathPtr);
        }
    }

    /// <summary>查系统目录签名清单（catalog）。系统自带的很多 exe 只有 catalog 签名，没有内嵌签名。</summary>
    private static bool IsInSystemCatalog(string path)
    {
        IntPtr catAdmin = IntPtr.Zero;
        try
        {
            // SHA256 优先（Win8+），失败退回 SHA1
            if (!CryptCATAdminAcquireContext2(ref catAdmin, IntPtr.Zero, "SHA256", IntPtr.Zero, 0))
                catAdmin = IntPtr.Zero;

            var useV2 = catAdmin != IntPtr.Zero;
            if (!useV2 && !CryptCATAdminAcquireContext(ref catAdmin, IntPtr.Zero, 0))
                return false;

            using var stream = File.OpenRead(path);
            var handle = stream.SafeFileHandle.DangerousGetHandle();

            uint hashSize = 0;
            if (useV2)
            {
                if (!CryptCATAdminCalcHashFromFileHandle2(catAdmin, handle, ref hashSize, IntPtr.Zero, 0))
                    return false;
            }
            else
            {
                if (!CryptCATAdminCalcHashFromFileHandle(handle, ref hashSize, IntPtr.Zero, 0))
                    return false;
            }

            if (hashSize == 0) return false;

            var hash = Marshal.AllocHGlobal((int)hashSize);
            try
            {
                var ok = useV2
                    ? CryptCATAdminCalcHashFromFileHandle2(catAdmin, handle, ref hashSize, hash, 0)
                    : CryptCATAdminCalcHashFromFileHandle(handle, ref hashSize, hash, 0);
                if (!ok) return false;

                var catInfo = CryptCATAdminEnumCatalogFromHash(catAdmin, hash, hashSize, 0, IntPtr.Zero);
                if (catInfo == IntPtr.Zero) return false;

                CryptCATAdminReleaseCatalogContext(catAdmin, catInfo, 0);
                return true;
            }
            finally
            {
                Marshal.FreeHGlobal(hash);
            }
        }
        catch (Exception ex)
        {
            CleanMaster.App.LogError("AuthenticodeChecker.IsInSystemCatalog", ex);
            return false;
        }
        finally
        {
            if (catAdmin != IntPtr.Zero)
            {
                try { CryptCATAdminReleaseContext(catAdmin, 0); } catch { }
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinTrustFileInfo
    {
        public uint cbStruct;
        public IntPtr pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinTrustData
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public IntPtr pFile;
        public uint dwStateAction;
        public IntPtr hWVTStateData;
        public IntPtr pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
    }

    [DllImport("wintrust.dll", PreserveSig = true)]
    private static extern int WinVerifyTrust(IntPtr hwnd, ref Guid pgActionID, IntPtr pWVTData);

    [DllImport("wintrust.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptCATAdminAcquireContext2(
        ref IntPtr phCatAdmin, IntPtr pgSubsystem, string pwszHashAlgorithm, IntPtr pStrongHashPolicy, uint dwFlags);

    [DllImport("wintrust.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptCATAdminAcquireContext(ref IntPtr phCatAdmin, IntPtr pgSubsystem, uint dwFlags);

    [DllImport("wintrust.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptCATAdminCalcHashFromFileHandle(
        IntPtr hFile, ref uint pcbHash, IntPtr pbHash, uint dwFlags);

    [DllImport("wintrust.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptCATAdminCalcHashFromFileHandle2(
        IntPtr hCatAdmin, IntPtr hFile, ref uint pcbHash, IntPtr pbHash, uint dwFlags);

    [DllImport("wintrust.dll", SetLastError = true)]
    private static extern IntPtr CryptCATAdminEnumCatalogFromHash(
        IntPtr hCatAdmin, IntPtr pbHash, uint cbHash, uint dwFlags, IntPtr phPrevCatInfo);

    [DllImport("wintrust.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptCATAdminReleaseCatalogContext(IntPtr hCatAdmin, IntPtr hCatInfo, uint dwFlags);

    [DllImport("wintrust.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptCATAdminReleaseContext(IntPtr hCatAdmin, uint dwFlags);
}
