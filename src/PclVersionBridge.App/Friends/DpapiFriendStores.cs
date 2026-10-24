using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PclVersionBridge.Core.Friends;

namespace PclVersionBridge.App.Friends;

/// <summary>Optional remembered session, protected for this Windows user. Passwords are never stored.</summary>
public sealed class DpapiFriendSessionStore : IFriendSessionStore
{
    private readonly ProtectedFriendFiles _files;
    public string StorageDirectory => _files.DirectoryPath;
    public DpapiFriendSessionStore(string? directory = null) => _files = new(directory);
    public async Task<FriendSession?> ReadAsync(CancellationToken token)
    {
        var value = await _files.ReadAsync<FriendSession>("session.dat", "session", token);
        return value is { Me: not null } && !string.IsNullOrWhiteSpace(value.AccessToken)
            && value.AccessToken.Length <= 16384 && !string.IsNullOrWhiteSpace(value.RefreshToken)
            && value.RefreshToken.Length <= 16384 && !string.IsNullOrWhiteSpace(value.Me.UserId) ? value : null;
    }
    public Task WriteAsync(FriendSession session, CancellationToken token) => _files.WriteAsync("session.dat", "session", session, token);
    public Task ClearAsync(CancellationToken token) => _files.DeleteAsync("session.dat", token);
}

/// <summary>Local notes are separated by account and never sent to the friend service.</summary>
public sealed class DpapiFriendNotesStore : IFriendNotesStore
{
    private readonly ProtectedFriendFiles _files;
    public string StorageDirectory => _files.DirectoryPath;
    public DpapiFriendNotesStore(string? directory = null) => _files = new(directory);
    private static string AccountFile(string owner) => "notes-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(owner))) + ".dat";
    public async Task<IReadOnlyDictionary<string, string>> ReadAsync(string ownerUserId, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(ownerUserId) || ownerUserId.Length > 256) return new Dictionary<string, string>();
        var notes = await _files.ReadAsync<Dictionary<string, string>>(AccountFile(ownerUserId), "notes:" + ownerUserId, token);
        if (notes is null || notes.Count > 200 || notes.Any(p => string.IsNullOrWhiteSpace(p.Key) || p.Key.Length > 256 || p.Value is null || p.Value.Length > 500))
            return new Dictionary<string, string>();
        return notes;
    }
    public Task WriteAsync(string ownerUserId, IReadOnlyDictionary<string, string> notes, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(ownerUserId) || ownerUserId.Length > 256 || notes.Count > 200
            || notes.Any(p => string.IsNullOrWhiteSpace(p.Key) || p.Key.Length > 256 || p.Value is null || p.Value.Length > 500))
            throw new ArgumentException("本机备注超过允许的长度或数量。");
        return _files.WriteAsync(AccountFile(ownerUserId), "notes:" + ownerUserId, notes, token);
    }
    public Task ClearAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (Directory.Exists(StorageDirectory))
            foreach (var path in Directory.EnumerateFiles(StorageDirectory, "notes-*.dat").Take(1000))
            { token.ThrowIfCancellationRequested(); File.Delete(path); }
        return Task.CompletedTask;
    }
}

internal sealed class ProtectedFriendFiles
{
    private const int MaximumBytes = 512 * 1024;
    public string DirectoryPath { get; }
    public ProtectedFriendFiles(string? directory)
    {
        var profile = Environment.GetEnvironmentVariable("VANILLAREACH_PROFILE_DIRECTORY");
        DirectoryPath = Path.GetFullPath(directory ?? Path.Combine(string.IsNullOrWhiteSpace(profile)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VanillaReach") : profile, "friends"));
    }
    public async Task<T?> ReadAsync<T>(string name, string purpose, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        byte[]? plain = null;
        try
        {
            var path = Path.Combine(DirectoryPath, name);
            if (!File.Exists(path)) return default;
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
            if (stream.Length is < 1 or > MaximumBytes) return default;
            var encrypted = new byte[(int)stream.Length];
            await stream.ReadExactlyAsync(encrypted, token);
            plain = WindowsDpapi.Unprotect(encrypted, purpose);
            if (plain.Length > MaximumBytes) return default;
            token.ThrowIfCancellationRequested();
            return JsonSerializer.Deserialize<T>(plain);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or JsonException or Win32Exception or ArgumentException)
        { return default; }
        finally { if (plain is not null) CryptographicOperations.ZeroMemory(plain); }
    }
    public async Task WriteAsync<T>(string name, string purpose, T value, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        byte[] plain = JsonSerializer.SerializeToUtf8Bytes(value);
        string? temporary = null;
        try
        {
            if (plain.Length > MaximumBytes - 8192) throw new IOException("好友本机数据过大。");
            var encrypted = WindowsDpapi.Protect(plain, purpose);
            if (encrypted.Length > MaximumBytes) throw new IOException("好友本机数据过大。");
            Directory.CreateDirectory(DirectoryPath);
            temporary = Path.Combine(DirectoryPath, name + "." + Guid.NewGuid().ToString("N") + ".tmp");
            await File.WriteAllBytesAsync(temporary, encrypted, token);
            token.ThrowIfCancellationRequested();
            File.Move(temporary, Path.Combine(DirectoryPath, name), true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
            if (temporary is not null && File.Exists(temporary)) File.Delete(temporary);
        }
    }
    public Task DeleteAsync(string name, CancellationToken token)
    { token.ThrowIfCancellationRequested(); File.Delete(Path.Combine(DirectoryPath, name)); return Task.CompletedTask; }
}

internal static class WindowsDpapi
{
    [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Length; public IntPtr Data; }
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref Blob input, string description, ref Blob entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref Blob input, IntPtr description, ref Blob entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
    internal static byte[] Protect(byte[] input, string purpose) => Transform(input, purpose, true);
    internal static byte[] Unprotect(byte[] input, string purpose) => Transform(input, purpose, false);
    private static byte[] Transform(byte[] bytes, string purpose, bool protect)
    {
        var entropyBytes = SHA256.HashData(Encoding.UTF8.GetBytes("VanillaReach.Friends.v1:" + purpose));
        Blob input = new() { Length = bytes.Length, Data = Marshal.AllocHGlobal(bytes.Length) };
        Blob entropy = new() { Length = entropyBytes.Length, Data = Marshal.AllocHGlobal(entropyBytes.Length) };
        Blob output = default;
        try
        {
            Marshal.Copy(bytes, 0, input.Data, bytes.Length);
            Marshal.Copy(entropyBytes, 0, entropy.Data, entropyBytes.Length);
            var success = protect
                ? CryptProtectData(ref input, "VanillaReach Friends", ref entropy, IntPtr.Zero, IntPtr.Zero, 1, out output)
                : CryptUnprotectData(ref input, IntPtr.Zero, ref entropy, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!success) throw new CryptographicException(Marshal.GetLastWin32Error());
            if (output.Length is < 1 or > 524288) throw new CryptographicException("受保护数据大小无效。");
            var result = new byte[output.Length]; Marshal.Copy(output.Data, result, 0, result.Length); return result;
        }
        finally
        {
            for (var i = 0; i < input.Length; i++) Marshal.WriteByte(input.Data, i, 0);
            Marshal.FreeHGlobal(input.Data); Marshal.FreeHGlobal(entropy.Data);
            if (output.Data != IntPtr.Zero) { for (var i = 0; i < output.Length; i++) Marshal.WriteByte(output.Data, i, 0); LocalFree(output.Data); }
        }
    }
}
