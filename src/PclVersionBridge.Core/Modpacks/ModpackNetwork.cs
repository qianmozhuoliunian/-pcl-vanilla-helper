using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using PclVersionBridge.Core.Paths;

namespace PclVersionBridge.Core.Modpacks;

public sealed class ModpackNetwork(HttpClient client)
{
    public const string UserAgent = "VanillaReach/0.2.0 (qianmozhuoliunian; +https://github.com/qianmozhuoliunian/-pcl-vanilla-helper)";
    public static HttpClient CreateClient()
    {
        var result = new HttpClient(new ModpackHostHandler(new HttpClientHandler { AllowAutoRedirect = false }))
        { Timeout = TimeSpan.FromMinutes(5) };
        result.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        return result;
    }
    public async Task<JsonDocument> GetJsonAsync(Uri uri, int maxBytes, CancellationToken token)
    {
        ModpackHostHandler.ValidateUri(uri);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        CheckResponse(response);
        if (response.Content.Headers.ContentLength > maxBytes) throw new InstallException("来源返回的列表过大。");
        await using var input = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var buffer = new MemoryStream();
        await MrpackReader.CopyBoundedAsync(input, buffer, maxBytes, timeout.Token);
        try { return JsonDocument.Parse(buffer.ToArray()); }
        catch (JsonException ex) { throw new InstallException("来源返回的数据格式无效，请稍后重试。", ex); }
    }
    public async Task<string> GetTextAsync(Uri uri, int maxBytes, CancellationToken token)
    {
        ModpackHostHandler.ValidateUri(uri);
        using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token);
        CheckResponse(response);
        if (response.Content.Headers.ContentLength > maxBytes) throw new InstallException("来源返回的校验信息过大。");
        await using var input = await response.Content.ReadAsStreamAsync(token);
        using var buffer = new MemoryStream();
        await MrpackReader.CopyBoundedAsync(input, buffer, maxBytes, token);
        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }
    public async Task<byte[]> GetImageBytesAsync(Uri uri, CancellationToken token)
    {
        ModpackRules.ContentUri(uri.AbsoluteUri);
        const int limit=2*1024*1024;
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        using var response=await client.GetAsync(uri,HttpCompletionOption.ResponseHeadersRead,timeout.Token);
        CheckResponse(response);
        if(response.Content.Headers.ContentLength>limit) throw new InstallException("项目图片过大，已跳过加载。");
        var type=response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant();
        if(type is not ("image/png" or "image/jpeg" or "image/gif" or "image/bmp" or "image/x-icon" or "image/webp"))
            throw new InstallException("项目图片格式暂不支持。");
        await using var input=await response.Content.ReadAsStreamAsync(timeout.Token);
        using var buffer=new MemoryStream();
        await MrpackReader.CopyBoundedAsync(input,buffer,limit,timeout.Token);
        return buffer.ToArray();
    }
    public async Task<long> GetLengthAsync(Uri uri, CancellationToken token)
    {
        ModpackHostHandler.ValidateUri(uri);
        using var request = new HttpRequestMessage(HttpMethod.Head, uri);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        CheckResponse(response);
        var size = response.Content.Headers.ContentLength;
        if (size is null or < 0 || size > ModpackRules.FileLimit) throw new InstallException("依赖库缺少有效的文件大小。");
        return size.Value;
    }
    public async Task DownloadAsync(Uri uri, string destination, long size, string sha1, string? sha512, CancellationToken token)
    {
        ModpackHostHandler.ValidateUri(uri);
        ModpackRules.Hash(sha1, 40);
        if (sha512 is not null) ModpackRules.Hash(sha512, 128);
        if (size < 0 || size > ModpackRules.FileLimit) throw new InstallException("文件大小超出允许范围。");
        var directory = Path.GetDirectoryName(Path.GetFullPath(destination))!;
        GamePathValidator.RejectReparsePoints(directory);
        Directory.CreateDirectory(directory);
        for (var attempt = 0; ; attempt++)
        {
            var temporary = Path.Combine(directory, ".vr-download-" + Guid.NewGuid().ToString("N"));
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TimeSpan.FromMinutes(5));
                using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                CheckResponse(response);
                if (response.Content.Headers.ContentLength is long actualLength && actualLength != size)
                    throw new InstallException("下载文件大小与清单不一致。");
                await using (var input = await response.Content.ReadAsStreamAsync(timeout.Token))
                await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    await MrpackReader.CopyBoundedAsync(input, output, size, timeout.Token);
                await VerifyAsync(temporary, size, sha1, sha512, token);
                token.ThrowIfCancellationRequested();
                GamePathValidator.RejectReparsePoints(directory);
                File.Move(temporary, destination, overwrite: false);
                return;
            }
            catch (HttpRequestException) when (attempt < 2) { await Task.Delay(500 * (attempt + 1), token); }
            catch (OperationCanceledException) when (!token.IsCancellationRequested && attempt < 2)
            { await Task.Delay(500 * (attempt + 1), token); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
    internal static async Task VerifyAsync(string path, long size, string sha1, string? sha512, CancellationToken token)
    {
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length != size) throw new InstallException("下载文件大小与清单不一致。");
        var actualSha1 = Convert.ToHexString(await SHA1.HashDataAsync(input, token));
        if (!actualSha1.Equals(sha1, StringComparison.OrdinalIgnoreCase))
            throw new InstallException("下载文件 SHA-1 校验失败。");
        if (sha512 is not null)
        {
            input.Position = 0;
            var actualSha512 = Convert.ToHexString(await SHA512.HashDataAsync(input, token));
            if (!actualSha512.Equals(sha512, StringComparison.OrdinalIgnoreCase))
                throw new InstallException("下载文件 SHA-512 校验失败。");
        }
    }
    private static void CheckResponse(HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
            throw new InstallException("来源暂时限制了请求频率，请稍后重试。");
        if (response.StatusCode == HttpStatusCode.Gone)
            throw new InstallException("来源接口已更新，请检查工具是否有新版本。");
        response.EnsureSuccessStatusCode();
    }
}
