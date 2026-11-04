using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Godot;

namespace STS2Mobile.Steam;

internal static class AndroidEncryptedJsonFile
{
    private const string DecryptStringBridgeMethod = "decryptString";
    private const string EncryptStringBridgeMethod = "encryptString";
    private static byte[] s_cachedKey;
    private static readonly object s_keyLock = new();

    internal static T Load<T>(string path)
        where T : class
    {
        if (!File.Exists(path))
            return null;

        var encrypted = File.ReadAllText(path);
        var json = Decrypt(encrypted);
        return json == null ? null : JsonSerializer.Deserialize<T>(json);
    }

    internal static bool Save<T>(string path, T value)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(value);
        var encrypted = Encrypt(json);
        if (encrypted == null)
            return false;

        File.WriteAllText(path, encrypted);
        return true;
    }

    internal static void DeleteQuietly(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
        }
    }

    private static string Encrypt(string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
            return null;

        if (OperatingSystem.IsIOS())
        {
            return EncryptAesGcm(plainText);
        }

        if (OperatingSystem.IsAndroid())
        {
            return AndroidBridgeDispatcher.Run(
                () =>
                {
                    var godotApp = GetGodotApp();
                    return (string)godotApp?.Call(EncryptStringBridgeMethod, plainText);
                }
            );
        }

        // On non-mobile (development/testing): use AES-GCM
        return EncryptAesGcm(plainText);
    }

    private static string Decrypt(string encryptedText)
    {
        if (string.IsNullOrEmpty(encryptedText))
            return null;

        if (OperatingSystem.IsIOS())
        {
            return DecryptAesGcm(encryptedText);
        }

        if (OperatingSystem.IsAndroid())
        {
            return AndroidBridgeDispatcher.Run(
                () =>
                {
                    var godotApp = GetGodotApp();
                    return (string)godotApp?.Call(DecryptStringBridgeMethod, encryptedText);
                }
            );
        }

        // On non-mobile (development/testing): use AES-GCM
        return DecryptAesGcm(encryptedText);
    }

    private static GodotObject GetGodotApp()
    {
        return AndroidGodotAppBridge.TryGetInstance(out var godotApp) ? godotApp : null;
    }

    private static byte[] GetOrCreateDeviceKey()
    {
        lock (s_keyLock)
        {
            if (s_cachedKey != null)
                return s_cachedKey;

            byte[] key = new byte[32];
            bool gotKey = false;

            if (OperatingSystem.IsIOS())
            {
                gotKey = IOSGodotAppBridge.GetOrCreateDeviceKey(key);
            }

            if (!gotKey)
            {
                try
                {
                    string fallbackDir = AppPaths.AppPrivateDataDir;
                    if (!string.IsNullOrEmpty(fallbackDir))
                    {
                        Directory.CreateDirectory(fallbackDir);
                        string keyPath = Path.Combine(fallbackDir, ".device_credential.key");
                        if (File.Exists(keyPath))
                        {
                            byte[] existing = File.ReadAllBytes(keyPath);
                            if (existing.Length == 32)
                            {
                                key = existing;
                                gotKey = true;
                            }
                        }

                        if (!gotKey)
                        {
                            System.Security.Cryptography.RandomNumberGenerator.Fill(key);
                            File.WriteAllBytes(keyPath, key);
                            gotKey = true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    PatchHelper.Log($"[Credentials] Fallback device key warning: {ex.Message}");
                }
            }

            if (!gotKey)
            {
                System.Security.Cryptography.RandomNumberGenerator.Fill(key);
            }

            s_cachedKey = key;
            return s_cachedKey;
        }
    }

    private static string EncryptAesGcm(string plainText)
    {
        try
        {
            var key = GetOrCreateDeviceKey();
            byte[] iv = new byte[12];
            System.Security.Cryptography.RandomNumberGenerator.Fill(iv);

            byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);
            byte[] cipherBytes = new byte[plainBytes.Length];
            byte[] tagBytes = new byte[16];

            using (var aesGcm = new AesGcm(key, 16))
            {
                aesGcm.Encrypt(iv, plainBytes, cipherBytes, tagBytes);
            }

            // Wire format identical to Android: [iv_len (1)] [iv] [ciphertext] [tag (16)]
            byte[] result = new byte[1 + iv.Length + cipherBytes.Length + tagBytes.Length];
            result[0] = (byte)iv.Length;
            Buffer.BlockCopy(iv, 0, result, 1, iv.Length);
            Buffer.BlockCopy(cipherBytes, 0, result, 1 + iv.Length, cipherBytes.Length);
            Buffer.BlockCopy(tagBytes, 0, result, 1 + iv.Length + cipherBytes.Length, tagBytes.Length);

            return Convert.ToBase64String(result);
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[Credentials] AES-GCM Encrypt failed: {ex.Message}");
            return null;
        }
    }

    private static string DecryptAesGcm(string encryptedText)
    {
        try
        {
            byte[] blob = Convert.FromBase64String(encryptedText);
            if (blob.Length < 1 + 12 + 16)
                return null;

            int ivLength = blob[0];
            if (ivLength != 12 || blob.Length < 1 + ivLength + 16)
                return null;

            byte[] iv = new byte[ivLength];
            Buffer.BlockCopy(blob, 1, iv, 0, ivLength);

            int tagLength = 16;
            int cipherLength = blob.Length - 1 - ivLength - tagLength;
            if (cipherLength < 0)
                return null;

            byte[] cipherBytes = new byte[cipherLength];
            Buffer.BlockCopy(blob, 1 + ivLength, cipherBytes, 0, cipherLength);

            byte[] tagBytes = new byte[tagLength];
            Buffer.BlockCopy(blob, 1 + ivLength + cipherLength, tagBytes, 0, tagLength);

            byte[] plainBytes = new byte[cipherLength];
            var key = GetOrCreateDeviceKey();

            using (var aesGcm = new AesGcm(key, tagLength))
            {
                aesGcm.Decrypt(iv, cipherBytes, tagBytes, plainBytes);
            }

            return Encoding.UTF8.GetString(plainBytes);
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[Credentials] AES-GCM Decrypt failed: {ex.Message}");
            return null;
        }
    }
}
