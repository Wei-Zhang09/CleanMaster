namespace CleanMaster.Services.Interfaces;

public interface ILicenseService
{
    LicenseInfo? LoadLocal();
    void ClearLocal();
    Task<(bool Success, string Message, LicenseInfo? Info)> VerifyKeyAsync(string keyCode);
    Task<(bool IsValid, string Message)> CheckActivationAsync();
}
