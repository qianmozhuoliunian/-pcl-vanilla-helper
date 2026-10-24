using System.Net;
using System.Net.Mail;
using System.Text.Json;
using PclVersionBridge.Core;
using PclVersionBridge.Core.Friends;
using PclVersionBridge.Core.Modpacks;
namespace VanillaReach.Friends.Server;

public sealed class FriendsSettings
{
    public string PublicOrigin { get; set; } = ""; public string DataDirectory { get; set; } = ""; public string? DevelopmentInbox { get; set; }
    public SmtpSettings Smtp { get; set; } = new();
    public bool IsLocalDevelopment { get; private set; }
    public void Validate(bool development)
    {
        if (!Uri.TryCreate(PublicOrigin, UriKind.Absolute, out var origin) || !string.IsNullOrEmpty(origin.UserInfo) || origin.AbsolutePath != "/" || !string.IsNullOrEmpty(origin.Query) || !string.IsNullOrEmpty(origin.Fragment)) throw new InvalidOperationException("Friends:PublicOrigin must be an absolute HTTPS origin.");
        IsLocalDevelopment = development && IPAddress.TryParse(origin.Host.Trim('[', ']'), out var ip) && IPAddress.IsLoopback(ip);
        if (origin.Scheme != "https" && !(origin.Scheme == "http" && IsLocalDevelopment)) throw new InvalidOperationException("HTTP is only allowed for explicit Development with a literal loopback origin.");
        if (string.IsNullOrWhiteSpace(DataDirectory) || !Path.IsPathFullyQualified(DataDirectory)) throw new InvalidOperationException("Friends:DataDirectory must be an absolute persistent directory.");
        if (!string.IsNullOrWhiteSpace(DevelopmentInbox))
        {
            if (!IsLocalDevelopment || !Path.IsPathFullyQualified(DevelopmentInbox)) throw new InvalidOperationException("Development inbox requires Development and literal loopback.");
        }
        else if (string.IsNullOrWhiteSpace(Smtp.Host) || Smtp.Port is < 1 or > 65535 || !MailAddress.TryCreate(Smtp.From, out _) || string.IsNullOrWhiteSpace(Smtp.User) || string.IsNullOrWhiteSpace(Smtp.Password)) throw new InvalidOperationException("Configure an authenticated TLS SMTP sender before starting the service.");
    }
}
public sealed class SmtpSettings { public string Host { get; set; } = ""; public int Port { get; set; } = 587; public string User { get; set; } = ""; public string Password { get; set; } = ""; public string From { get; set; } = ""; }
public interface IAccountEmailSender { Task SendAsync(string email, string code, string action, CancellationToken ct); }
internal sealed class AccountEmailSender(FriendsSettings options) : IAccountEmailSender
{
    public async Task SendAsync(string email, string code, string action, CancellationToken ct)
    {
        if (options.DevelopmentInbox is { } path && !string.IsNullOrWhiteSpace(path))
        {
            Directory.CreateDirectory(path);
            await File.WriteAllTextAsync(Path.Combine(path, Guid.NewGuid().ToString("N") + ".json"), JsonSerializer.Serialize(new { email, code, action, createdAtUtc = DateTimeOffset.UtcNow }), ct); return;
        }
        var s = options.Smtp;
        using var client = new SmtpClient(s.Host, s.Port) { EnableSsl = true, Credentials = new NetworkCredential(s.User, s.Password), Timeout = 15000 };
        using var message = new MailMessage(s.From, email, "原版直达账号验证", $"用途：{action}\n请在原版直达账号页面输入下列验证码：\n{code}\n如果不是你发起的请求，请忽略此邮件。");
        await client.SendMailAsync(message, ct);
    }
}
public interface IPackReferenceValidator { Task ValidateAsync(PackReference pack, CancellationToken ct); }
internal sealed class ModrinthPackValidator : IPackReferenceValidator, IDisposable
{
    private readonly HttpClient client = ModpackNetwork.CreateClient();
    public async Task ValidateAsync(PackReference pack, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try { await new ModrinthCatalog(new ModpackNetwork(client)).GetPinnedReleaseAsync(pack.ProjectId, pack.VersionId, pack.ArchiveSha512, timeout.Token); }
        catch (Exception ex) when (ex is InstallException or HttpRequestException or OperationCanceledException)
        { if (ct.IsCancellationRequested) throw; throw new ApiFailure(503, "source_unavailable", "暂时无法验证这个发布版本，请稍后重试。"); }
    }
    public void Dispose() => client.Dispose();
}
