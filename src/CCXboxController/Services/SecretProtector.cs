using System;
using System.Security.Cryptography;
using System.Text;

namespace CCXboxController.Services;

// Wraps DPAPI (CurrentUser scope) — only this Windows user account can decrypt.
public static class SecretProtector
{
    public static string Protect(string plain)
    {
        if (string.IsNullOrEmpty(plain)) return "";
        var bytes = Encoding.UTF8.GetBytes(plain);
        var enc = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(enc);
    }

    public static string Unprotect(string base64Cipher)
    {
        if (string.IsNullOrEmpty(base64Cipher)) return "";
        try
        {
            var cipher = Convert.FromBase64String(base64Cipher);
            var plain = ProtectedData.Unprotect(cipher, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }
        catch
        {
            return "";
        }
    }
}
