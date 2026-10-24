using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
namespace VanillaReach.Friends.Server;

public sealed class EmailOutboxRow
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string UserId { get; set; } = "";
    public string ProtectedPayload { get; set; } = "";
    public long Created { get; set; }
    public long NextAttempt { get; set; }
}
internal sealed record AccountEmail(string Email, string Code, string Action);
internal sealed class EmailQueue(FriendsDb db, IDataProtectionProvider protection, TimeProvider clock, FriendsSettings settings, RequestOperation operation, EmailDispatcher dispatcher)
{
    public async Task Enqueue(string userId, string email, string code, string action, CancellationToken ct)
    {
        var now = clock.GetUtcNow().ToUnixTimeMilliseconds();
        var row = new EmailOutboxRow { UserId = userId, Created = now, NextAttempt = now, ProtectedPayload = protection.CreateProtector("VanillaReach.Friends.email.v1").Protect(JsonSerializer.Serialize(new AccountEmail(email, code, action))) };
        db.EmailOutbox.Add(row); await db.SaveChangesAsync(ct);
        // Development inbox is immediate and explicit; production uses the durable background queue.
        if (settings.IsLocalDevelopment) await operation.External(() => dispatcher.Deliver(row.Id, ct));
    }
}
internal sealed class EmailDispatcher(IServiceScopeFactory scopes, IDataProtectionProvider protection, IAccountEmailSender sender, TimeProvider clock, ILogger<EmailDispatcher> logger)
{
    public async Task<bool> Deliver(string? id, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FriendsDb>(); await using var operation = scope.ServiceProvider.GetRequiredService<RequestOperation>(); await operation.Begin(ct);
        var now = clock.GetUtcNow().ToUnixTimeMilliseconds();
        await db.EmailOutbox.Where(x => x.Created < now - (long)TimeSpan.FromHours(2).TotalMilliseconds).ExecuteDeleteAsync(ct);
        var row = await db.EmailOutbox.Where(x => (id == null || x.Id == id) && x.NextAttempt <= now).OrderBy(x => x.Created).FirstOrDefaultAsync(ct);
        if (row is null) { await operation.Commit(); return false; }
        AccountEmail message;
        try { message = JsonSerializer.Deserialize<AccountEmail>(protection.CreateProtector("VanillaReach.Friends.email.v1").Unprotect(row.ProtectedPayload))!; }
        catch (Exception ex) when (ex is CryptographicException or JsonException) { db.EmailOutbox.Remove(row); await db.SaveChangesAsync(ct); await operation.Commit(); logger.LogWarning("An unreadable account email was discarded."); return true; }
        row.NextAttempt = now + (long)TimeSpan.FromMinutes(1).TotalMilliseconds; await db.SaveChangesAsync(ct);
        var sent = false;
        await operation.External(async () =>
        {
            try { using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(15)); await sender.SendAsync(message.Email, message.Code, message.Action, timeout.Token); sent = true; }
            catch (Exception ex) when (ex is System.Net.Mail.SmtpException or IOException or OperationCanceledException) { logger.LogWarning("Account email delivery deferred; the durable queue will retry."); }
        });
        if (sent) await db.EmailOutbox.Where(x => x.Id == row.Id).ExecuteDeleteAsync(CancellationToken.None);
        await operation.Commit(); return true;
    }
}
internal sealed class EmailDeliveryWorker(EmailDispatcher dispatcher, ILogger<EmailDeliveryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        do
        {
            try { for (var i = 0; i < 20 && await dispatcher.Deliver(null, ct); i++) { } }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception) { logger.LogWarning("Account email queue is temporarily unavailable."); }
        } while (await timer.WaitForNextTickAsync(ct));
    }
}
