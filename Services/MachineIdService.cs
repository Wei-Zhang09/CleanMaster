using System.Management;
using System.Security.Cryptography;
using System.Text;

namespace CleanMaster.Services;

public static class MachineIdService
{
    private static string? _cachedId;

    public static string GetMachineId()
    {
        if (_cachedId != null) return _cachedId;

        var parts = new List<string>();

        // CPU ID (WMI - may fail on some systems)
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT ProcessorId FROM Win32_Processor");
            foreach (ManagementObject obj in searcher.Get())
            {
                parts.Add(obj["ProcessorId"]?.ToString() ?? "");
                break;
            }
        }
        catch
        {
            App.Log("WMI CPU query failed, using fallback");
            parts.Add(Environment.ProcessorCount.ToString());
        }

        // Motherboard Serial
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT SerialNumber FROM Win32_BaseBoard");
            foreach (ManagementObject obj in searcher.Get())
            {
                parts.Add(obj["SerialNumber"]?.ToString() ?? "");
                break;
            }
        }
        catch
        {
            App.Log("WMI Board query failed, using fallback");
            parts.Add(Environment.MachineName);
        }

        // Disk Serial
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT SerialNumber FROM Win32_DiskDrive");
            foreach (ManagementObject obj in searcher.Get())
            {
                parts.Add(obj["SerialNumber"]?.ToString() ?? "");
                break;
            }
        }
        catch
        {
            App.Log("WMI Disk query failed, using fallback");
            parts.Add("disk_unknown");
        }

        // MAC Address
        try
        {
            var nic = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(n => n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up
                                 && n.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Loopback);
            if (nic != null) parts.Add(nic.GetPhysicalAddress().ToString());
        }
        catch
        {
            parts.Add("mac_unknown");
        }

        var combined = string.Join("-", parts);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(combined));
        _cachedId = Convert.ToHexString(hash)[..32].ToUpper();
        App.Log($"MachineId generated: {_cachedId}");
        return _cachedId;
    }
}
