using System;
using System.Runtime.InteropServices;
using Watermark.Native;

namespace Watermark.Services;

public class SessionInfoProvider
{
    public string GetUserName()
    {
        try
        {
            var name = QueryString(NativeMethods.WTS_INFO_CLASS.WTSUserName);
            if (!string.IsNullOrEmpty(name)) return name;
        }
        catch { }
        return Environment.UserName;
    }

    public string GetDomainName()
    {
        try
        {
            var d = QueryString(NativeMethods.WTS_INFO_CLASS.WTSDomainName);
            if (!string.IsNullOrEmpty(d)) return d;
        }
        catch { }
        return Environment.UserDomainName;
    }

    public string GetComputerName() => Environment.MachineName;

    public string GetClientName()
    {
        try
        {
            return QueryString(NativeMethods.WTS_INFO_CLASS.WTSClientName) ?? string.Empty;
        }
        catch { return string.Empty; }
    }

    public string GetClientAddress()
    {
        IntPtr buffer = IntPtr.Zero;
        try
        {
            if (!NativeMethods.WTSQuerySessionInformation(
                    NativeMethods.WTS_CURRENT_SERVER_HANDLE,
                    NativeMethods.WTS_CURRENT_SESSION,
                    NativeMethods.WTS_INFO_CLASS.WTSClientAddress,
                    out buffer, out _))
            {
                return string.Empty;
            }

            var ca = Marshal.PtrToStructure<NativeMethods.WTS_CLIENT_ADDRESS>(buffer);
            if (ca.Address == null) return string.Empty;

            if (ca.AddressFamily == NativeMethods.AF_INET)
            {
                // Bytes 2..5 contain IPv4 octets
                return $"{ca.Address[2]}.{ca.Address[3]}.{ca.Address[4]}.{ca.Address[5]}";
            }
            if (ca.AddressFamily == NativeMethods.AF_INET6)
            {
                var parts = new string[8];
                for (int i = 0; i < 8; i++)
                {
                    int hi = ca.Address[i * 2];
                    int lo = ca.Address[i * 2 + 1];
                    parts[i] = ((hi << 8) | lo).ToString("x");
                }
                return string.Join(":", parts);
            }
            return string.Empty;
        }
        catch { return string.Empty; }
        finally
        {
            if (buffer != IntPtr.Zero) NativeMethods.WTSFreeMemory(buffer);
        }
    }

    public string GetFormattedUserHost()
    {
        var u = GetUserName();
        var d = GetDomainName();
        var host = GetComputerName();
        var userPart = string.IsNullOrEmpty(d) || string.Equals(d, host, StringComparison.OrdinalIgnoreCase)
            ? u : $"{d}\\{u}";
        return $"{userPart} @ {host}";
    }

    public string GetFormattedClient()
    {
        var name = GetClientName();
        var addr = GetClientAddress();
        if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(addr)) return $"{name} ({addr})";
        if (!string.IsNullOrEmpty(addr)) return addr;
        if (!string.IsNullOrEmpty(name)) return name;
        return "local";
    }

    private static string QueryString(NativeMethods.WTS_INFO_CLASS infoClass)
    {
        IntPtr buffer = IntPtr.Zero;
        try
        {
            if (!NativeMethods.WTSQuerySessionInformation(
                    NativeMethods.WTS_CURRENT_SERVER_HANDLE,
                    NativeMethods.WTS_CURRENT_SESSION,
                    infoClass,
                    out buffer, out int bytes))
            {
                return string.Empty;
            }
            return Marshal.PtrToStringUni(buffer) ?? string.Empty;
        }
        finally
        {
            if (buffer != IntPtr.Zero) NativeMethods.WTSFreeMemory(buffer);
        }
    }
}
