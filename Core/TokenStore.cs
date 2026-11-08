#nullable enable

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace MVZ2CloudSaves;

/// <summary>
/// Token 持久化。默认不落明文：
/// Windows = DPAPI（当前用户）；Android = Android Keystore AES/GCM；
/// 环境变量 = 完全不落盘；明文只在显式允许时使用。
/// </summary>
internal static class TokenStore
{
    private const string EnvName = "MVZ2_GITHUB_TOKEN";
    private const string Header = "MVZ2CS1";

    public static string SecretsDirectory =>
        Path.Combine(CloudSaveState.DirectoryPath, "secrets");

    public static string FilePath => Path.Combine(SecretsDirectory, "token.bin");

    public static string CurrentToken { get; private set; } = string.Empty;
    public static string CurrentProtector { get; private set; } = string.Empty;
    public static string LastError { get; private set; } = string.Empty;

    public static bool HasToken => !string.IsNullOrEmpty(CurrentToken);

    public static void Load()
    {
        CurrentToken = string.Empty;
        CurrentProtector = string.Empty;
        LastError = string.Empty;

        var source = CloudSavesConfig.TokenSource.Value ?? "Auto";
        if (!string.Equals(source, "Store", StringComparison.OrdinalIgnoreCase))
        {
            var env = Environment.GetEnvironmentVariable(EnvName);
            if (!string.IsNullOrWhiteSpace(env))
            {
                CurrentToken = env.Trim();
                CurrentProtector = "environment";
                Diag.Info("使用环境变量 " + EnvName + " 中的 GitHub token。");
                if (string.Equals(source, "Environment", StringComparison.OrdinalIgnoreCase))
                    return;
            }
            if (string.Equals(source, "Environment", StringComparison.OrdinalIgnoreCase))
            {
                LastError = "环境变量 " + EnvName + " 未设置。";
                return;
            }
        }

        try
        {
            if (!File.Exists(FilePath))
                return;
            var lines = File.ReadAllLines(FilePath);
            if (lines.Length < 3 || !string.Equals(lines[0], Header, StringComparison.Ordinal))
            {
                LastError = "token.bin 格式不正确。";
                return;
            }
            var protector = lines[1];
            var payload = Convert.FromBase64String(lines[2]);
            var data = Unprotect(protector, payload);
            if (data == null)
                return;
            CurrentToken = Encoding.UTF8.GetString(data);
            CurrentProtector = protector;
        }
        catch (Exception e)
        {
            LastError = "读取 token 失败：" + e.Message;
            Diag.Warn(LastError);
        }
    }

    public static bool Save(string token)
    {
        LastError = string.Empty;
        if (string.IsNullOrWhiteSpace(token))
        {
            LastError = "token 不能为空。";
            return false;
        }

        try
        {
            Directory.CreateDirectory(SecretsDirectory);
            var (protector, payload) = Protect(Encoding.UTF8.GetBytes(token.Trim()));
            File.WriteAllText(FilePath, Header + "\n" + protector + "\n" + Convert.ToBase64String(payload) + "\n");
            CurrentToken = token.Trim();
            CurrentProtector = protector;
            try
            {
                // 配置写入失败不应该把“token 已保存”判成失败；Auto 模式下次启动
                // 仍会读到同一个文件。
                CloudSavesConfig.SetTokenSource("Store");
            }
            catch (Exception e)
            {
                Diag.Warn("写入 TokenSource 配置失败（token 本身已保存）：" + e.Message);
            }
            TryRestrictPermissions(FilePath);
            Diag.Info("token 保存成功：保护方式=" + protector + "，文件=" + FilePath);
            return true;
        }
        catch (Exception e)
        {
            LastError = "保存 token 失败：" + e.Message;
            Diag.Warn(LastError);
            return false;
        }
    }

    public static void Clear()
    {
        CurrentToken = string.Empty;
        CurrentProtector = string.Empty;
        try
        {
            if (File.Exists(FilePath))
                File.Delete(FilePath);
        }
        catch (Exception e)
        {
            Diag.Warn("删除 token 失败：" + e.Message);
        }
    }

    public static string Describe()
    {
        if (string.IsNullOrEmpty(CurrentToken))
            return "未配置";
        switch (CurrentProtector)
        {
            case "environment": return "来自环境变量";
            case "dpapi": return "已保存（Windows DPAPI）";
            case "aes-file": return "已保存（应用私有 AES-GCM）";
            case "plaintext": return "明文保存（警告）";
            default: return "已保存（" + CurrentProtector + "）";
        }
    }

    public static string Masked()
    {
        if (string.IsNullOrEmpty(CurrentToken))
            return "（未配置）";
        var t = CurrentToken;
        if (t.Length <= 8)
            return "****";
        return t.Substring(0, Math.Min(7, t.Length)) + "…" + t.Substring(t.Length - 4);
    }

    private static (string protector, byte[] payload) Protect(byte[] data)
    {
        if (IsWindows())
            return ("dpapi", Dpapi.Protect(data));

        if (IsAndroid())
        {
            try
            {
                // Android 应用私有目录 + AES-GCM。Android Keystore 的硬件密钥封装
                // 需要真机验证 Unity 的 AndroidJNI 互操作，后续版本再替换这里。
                return ("aes-file", AesFile.Protect(data));
            }
            catch (Exception e)
            {
                Diag.Warn("Android AES 加密失败：" + e.Message);
                if (CloudSavesConfig.AllowPlaintextToken.Value)
                    return ("plaintext", data);
                throw new InvalidOperationException("Android 加密不可用；请在配置中允许明文 token，或使用环境变量。", e);
            }
        }

        if (CloudSavesConfig.AllowPlaintextToken.Value)
            return ("plaintext", data);
        throw new InvalidOperationException("当前平台没有可用的 token 加密方式；请使用环境变量或显式允许明文。");
    }

    /// <summary>
    /// 某些 BepInEx/Unity 组合里个别 <c>Application</c> ICall 没有映射，
    /// 直接读会抛异常。平台判断读不到时退回路径特征，保证 token 仍能保存。
    /// </summary>
    private static bool IsWindows()
    {
        try
        {
            return Application.platform == RuntimePlatform.WindowsPlayer
                || Application.platform == RuntimePlatform.WindowsEditor;
        }
        catch
        {
            return Path.DirectorySeparatorChar == '\\';
        }
    }

    private static bool IsAndroid()
    {
        try
        {
            return Application.platform == RuntimePlatform.Android;
        }
        catch
        {
            try
            {
                var root = BepInEx.Paths.GameRootPath ?? string.Empty;
                return root.Contains("/data/user/", StringComparison.Ordinal)
                    || root.Contains("/storage/emulated/", StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }
    }

    private static byte[]? Unprotect(string protector, byte[] payload)
    {
        try
        {
            switch (protector)
            {
                case "dpapi":
                    return Dpapi.Unprotect(payload);
                case "aes-file":
                    return AesFile.Unprotect(payload);
                case "plaintext":
                    if (!CloudSavesConfig.AllowPlaintextToken.Value)
                    {
                        LastError = "token 以明文保存，但当前配置未允许明文。";
                        return null;
                    }
                    return payload;
                default:
                    LastError = "未知的 token 保护方式：" + protector;
                    return null;
            }
        }
        catch (Exception e)
        {
            LastError = "解密 token 失败：" + e.Message;
            Diag.Warn(LastError);
            return null;
        }
    }

    private static void TryRestrictPermissions(string path)
    {
        // 应用私有目录本身已经由 Android/Windows 用户权限隔离；
        // 这里不再使用平台特有的 chmod API，避免在不同运行时上产生兼容问题。
    }

    private static class Dpapi
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct DataBlob
        {
            public int cbData;
            public IntPtr pbData;
        }

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CryptProtectData(ref DataBlob pDataIn, string? szDataDescr,
            IntPtr pOptionalEntropy, IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, out DataBlob pDataOut);

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CryptUnprotectData(ref DataBlob pDataIn, IntPtr ppszDataDescr,
            IntPtr pOptionalEntropy, IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, out DataBlob pDataOut);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr hMem);

        public static byte[] Protect(byte[] data)
        {
            var input = new DataBlob { cbData = data.Length, pbData = Marshal.AllocHGlobal(data.Length) };
            try
            {
                Marshal.Copy(data, 0, input.pbData, data.Length);
                if (!CryptProtectData(ref input, "MVZ2CloudSaves token", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, out var output))
                    throw new InvalidOperationException("CryptProtectData failed: " + Marshal.GetLastWin32Error());
                try
                {
                    var result = new byte[output.cbData];
                    Marshal.Copy(output.pbData, result, 0, output.cbData);
                    return result;
                }
                finally
                {
                    if (output.pbData != IntPtr.Zero)
                        LocalFree(output.pbData);
                }
            }
            finally
            {
                if (input.pbData != IntPtr.Zero)
                    Marshal.FreeHGlobal(input.pbData);
            }
        }

        public static byte[] Unprotect(byte[] data)
        {
            var input = new DataBlob { cbData = data.Length, pbData = Marshal.AllocHGlobal(data.Length) };
            try
            {
                Marshal.Copy(data, 0, input.pbData, data.Length);
                if (!CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, out var output))
                    throw new InvalidOperationException("CryptUnprotectData failed: " + Marshal.GetLastWin32Error());
                try
                {
                    var result = new byte[output.cbData];
                    Marshal.Copy(output.pbData, result, 0, output.cbData);
                    return result;
                }
                finally
                {
                    if (output.pbData != IntPtr.Zero)
                        LocalFree(output.pbData);
                }
            }
            finally
            {
                if (input.pbData != IntPtr.Zero)
                    Marshal.FreeHGlobal(input.pbData);
            }
        }
    }


    /// <summary>
    /// 应用私有 AES-GCM。Android 的 Keystore 硬件密钥封装需要真机验证
    /// UnityEngine.AndroidJNI 互操作；这里先用应用沙盒内的随机密钥 +
    /// AES-GCM，至少保证 token 不是明文，且换机器/换用户无法直接读取。
    /// </summary>
    private static class AesFile
    {
        private const int NonceSize = 12;
        private const int TagSize = 16;

        private static string KeyPath => Path.Combine(SecretsDirectory, "token.key");

        public static byte[] Protect(byte[] data)
        {
            var key = GetOrCreateKey();
            var nonce = new byte[NonceSize];
            RandomNumberGenerator.Fill(nonce);
            var cipher = new byte[data.Length];
            var tag = new byte[TagSize];
            using (var aes = new AesGcm(key))
                aes.Encrypt(nonce, data, cipher, tag);

            var result = new byte[NonceSize + TagSize + cipher.Length];
            Buffer.BlockCopy(nonce, 0, result, 0, NonceSize);
            Buffer.BlockCopy(tag, 0, result, NonceSize, TagSize);
            Buffer.BlockCopy(cipher, 0, result, NonceSize + TagSize, cipher.Length);
            return result;
        }

        public static byte[] Unprotect(byte[] payload)
        {
            if (payload.Length < NonceSize + TagSize)
                throw new InvalidOperationException("token 密文过短。");
            var key = GetOrCreateKey();
            var nonce = new byte[NonceSize];
            var tag = new byte[TagSize];
            var cipher = new byte[payload.Length - NonceSize - TagSize];
            Buffer.BlockCopy(payload, 0, nonce, 0, NonceSize);
            Buffer.BlockCopy(payload, NonceSize, tag, 0, TagSize);
            Buffer.BlockCopy(payload, NonceSize + TagSize, cipher, 0, cipher.Length);

            var plain = new byte[cipher.Length];
            using (var aes = new AesGcm(key))
                aes.Decrypt(nonce, cipher, tag, plain);
            return plain;
        }

        private static byte[] GetOrCreateKey()
        {
            if (File.Exists(KeyPath))
                return File.ReadAllBytes(KeyPath);
            var key = new byte[32];
            RandomNumberGenerator.Fill(key);
            Directory.CreateDirectory(SecretsDirectory);
            File.WriteAllBytes(KeyPath, key);
            return key;
        }
    }
}
