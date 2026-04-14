using System.Security.Cryptography;
using System.Text;

namespace FleetManager.Desktop.Services;

internal static class DesktopCredentialProtector
{
    public static string? Protect(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var bytes = Encoding.UTF8.GetBytes(value);
        var protectedBytes = ProtectedData.Protect(bytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(protectedBytes);
    }

    public static string? Unprotect(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            var protectedBytes = Convert.FromBase64String(value);
            var rawBytes = ProtectedData.Unprotect(protectedBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(rawBytes);
        }
        catch
        {
            return null;
        }
    }
}
