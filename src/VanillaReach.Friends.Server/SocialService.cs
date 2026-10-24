using Microsoft.EntityFrameworkCore;
using PclVersionBridge.Core.Friends;
namespace VanillaReach.Friends.Server;

internal sealed class SocialService(FriendsDb db, TimeProvider clock, IPackReferenceValidator validator, RequestOperation operation, SessionTickets tickets, IHttpContextAccessor contexts)
{
    private long Now => clock.GetUtcNow().ToUnixTimeMilliseconds();
    private static long Days(int days) => (long)TimeSpan.FromDays(days).TotalMilliseconds;
    private static string Pair(string a, string b) => string.CompareOrdinal(a, b) < 0 ? a + ":" + b : b + ":" + a;
    public static string Code(string? text)
    { var code = (text ?? "").Replace("-", "").Replace(" ", "").ToUpperInvariant(); if (code.Length != 12 || code.Any(c => !"0123456789ABCDEFGHJKMNPQRSTVWXYZ".Contains(c))) throw ApiFailure.Missing(); return code; }
    private Task<bool> Blocked(string a, string b) => db.Blocks.AnyAsync(x => (x.FromId == a && x.ToId == b) || (x.FromId == b && x.ToId == a));
    private Task<bool> Friends(string a, string b) => db.Friendships.AnyAsync(x => x.Pair == Pair(a, b));
    private async Task<FriendProfile> Profile(string id) => (await db.Users.FindAsync(id))?.Profile() ?? throw ApiFailure.Missing();
    private static void Page(int page) { if (page is < 0 or > 100000) throw ApiFailure.Invalid(); }
    public async Task Cleanup()
    {
        var now = Now; var cutoff = now - Days(30);
        await db.Requests.Where(x => x.State == "pending" && x.Expires <= now).ExecuteUpdateAsync(s => s.SetProperty(x => x.State, "expired").SetProperty(x => x.PendingPair, (string?)null).SetProperty(x => x.Updated, now));
        await db.Invitations.Where(x => x.State == "pending" && x.Expires <= now).ExecuteUpdateAsync(s => s.SetProperty(x => x.State, "expired"));
        await db.Requests.Where(x => x.Expires < cutoff).ExecuteDeleteAsync();
        await db.Invitations.Where(x => x.Expires < cutoff).ExecuteDeleteAsync();
        await db.Sessions.Where(x => x.Expires < now).ExecuteDeleteAsync();
        await db.Rates.Where(x => x.Expires < now).ExecuteDeleteAsync();
    }
    public async Task Rate(string id, int limit, TimeSpan window)
    {
        var now = Now; var row = await db.Rates.FindAsync(id);
        if (row is null) { row = new RateRow { Id = id, Count = 0, Expires = now + (long)window.TotalMilliseconds }; db.Rates.Add(row); }
        if (row.Expires <= now) { row.Count = 0; row.Expires = now + (long)window.TotalMilliseconds; }
        if (row.Count >= limit) throw new ApiFailure(429, "rate_limited", "操作过于频繁，请稍后重试。", Math.Max(1, (int)Math.Ceiling((row.Expires - now) / 1000d)));
        row.Count++; await db.SaveChangesAsync();
    }
    public async Task<FriendProfile> Lookup(string me, string code)
    {
        await Rate("user:" + me + ":lookup", 30, TimeSpan.FromMinutes(1)); var normal = Code(code);
        var other = await db.Users.SingleOrDefaultAsync(x => x.FriendCode == normal && x.EmailConfirmed);
        if (other is null || await Blocked(me, other.Id)) throw ApiFailure.Missing(); return other.Profile();
    }
    public async Task<FriendPage<FriendProfile>> ListFriends(string me, int page)
    {
        Page(page); var query = db.Friendships.Where(x => x.First == me || x.Second == me).OrderBy(x => x.Pair);
        var items = await query.Skip(page * 20).Take(20).ToArrayAsync(); var result = new List<FriendProfile>(); foreach (var x in items) result.Add(await Profile(x.First == me ? x.Second : x.First)); return new(result, page, await query.CountAsync());
    }
    public async Task<FriendPage<FriendProfile>> ListBlocks(string me, int page)
    { Page(page); var query = db.Blocks.Where(x => x.FromId == me).OrderBy(x => x.Id); var rows = await query.Skip(page * 20).Take(20).ToArrayAsync(); var result = new List<FriendProfile>(); foreach (var x in rows) result.Add(await Profile(x.ToId)); return new(result, page, await query.CountAsync()); }
    private async Task<FriendRequest> RequestDto(RequestRow x) => new(x.Id, await Profile(x.FromId), await Profile(x.ToId), x.State, DateTimeOffset.FromUnixTimeMilliseconds(x.Created), DateTimeOffset.FromUnixTimeMilliseconds(x.Expires));
    public async Task<FriendPage<FriendRequest>> ListRequests(string me, int page)
    { Page(page); var query = db.Requests.Where(x => x.FromId == me || x.ToId == me).OrderByDescending(x => x.Created).ThenBy(x => x.Id); var result = new List<FriendRequest>(); foreach (var x in await query.Skip(page * 20).Take(20).ToArrayAsync()) result.Add(await RequestDto(x)); return new(result, page, await query.CountAsync()); }
    private static ApiFailure Unavailable() => new(404, "request_unavailable", "暂时无法向该好友码发送申请。");
    public async Task Request(string me, string input)
    {
        string code; try { code = Code(input); } catch (ApiFailure) { throw Unavailable(); }
        var target = await db.Users.SingleOrDefaultAsync(x => x.FriendCode == code && x.EmailConfirmed);
        if (target is null || await Blocked(me, target.Id)) throw Unavailable();
        if (target.Id == me) throw new ApiFailure(409, "self_request", "不能添加自己为好友。");
        if (await Friends(me, target.Id)) throw new ApiFailure(409, "already_friends", "你们已经是好友。");
        var pair = Pair(me, target.Id); var pending = await db.Requests.SingleOrDefaultAsync(x => x.PendingPair == pair);
        if (pending is not null) { if (pending.FromId == me) return; throw new ApiFailure(409, "incoming_request", "对方已发来申请，请先处理收到的申请。"); }
        var recent = await db.Requests.Where(x => x.FromId == me && x.ToId == target.Id && (x.State == "rejected" || x.State == "withdrawn")).OrderByDescending(x => x.Updated).FirstOrDefaultAsync();
        if (recent is not null && recent.Updated + Days(1) > Now) throw new ApiFailure(429, "request_cooldown", "请在24小时后重新发送申请。", (int)Math.Ceiling((recent.Updated + Days(1) - Now) / 1000d));
        if (await db.Requests.CountAsync(x => x.FromId == me && x.State == "pending") >= 20) throw new ApiFailure(429, "pending_limit", "待处理申请过多，请先处理已有申请。", 3600);
        if (await FriendCount(me) >= 200 || await FriendCount(target.Id) >= 200) throw new ApiFailure(409, "friend_limit", "好友数量已达到上限。");
        await Rate("user:" + me + ":requests", 10, TimeSpan.FromDays(1));
        db.Requests.Add(new RequestRow { FromId = me, ToId = target.Id, PendingPair = pair, Created = Now, Updated = Now, Expires = Now + Days(7) }); await db.SaveChangesAsync();
    }
    private Task<int> FriendCount(string id) => db.Friendships.CountAsync(x => x.First == id || x.Second == id);
    public async Task ResolveRequest(string me, string id, string action)
    {
        var row = await db.Requests.FindAsync(id) ?? throw ApiFailure.Missing();
        if (action is not ("accept" or "reject" or "withdraw")) throw ApiFailure.Missing();
        if ((action == "withdraw" ? row.FromId : row.ToId) != me) throw ApiFailure.Forbidden();
        var state = action switch { "accept" => "accepted", "reject" => "rejected", _ => "withdrawn" };
        if (row.State == state) return;
        if (row.State != "pending" || row.Expires <= Now || await Blocked(row.FromId, row.ToId)) throw ApiFailure.Conflict();
        if (action == "accept")
        {
            if (await FriendCount(row.FromId) >= 200 || await FriendCount(row.ToId) >= 200) throw new ApiFailure(409, "friend_limit", "好友数量已达到上限。");
            var pair = Pair(row.FromId, row.ToId); if (!await Friends(row.FromId, row.ToId)) { var parts = pair.Split(':'); db.Friendships.Add(new() { Pair = pair, First = parts[0], Second = parts[1] }); }
        }
        row.State = state; row.Updated = Now; row.PendingPair = null; await db.SaveChangesAsync();
    }
    public async Task Remove(string me, string target)
    {
        await db.Friendships.Where(x => x.Pair == Pair(me, target)).ExecuteDeleteAsync();
        await Invalidate(me, target);
    }
    private async Task Invalidate(string a, string b)
    {
        var now = Now;
        await db.Requests.Where(x => x.State == "pending" && ((x.FromId == a && x.ToId == b) || (x.FromId == b && x.ToId == a))).ExecuteUpdateAsync(s => s.SetProperty(x => x.State, "invalidated").SetProperty(x => x.PendingPair, (string?)null).SetProperty(x => x.Updated, now));
        await db.Invitations.Where(x => x.State == "pending" && ((x.FromId == a && x.ToId == b) || (x.FromId == b && x.ToId == a))).ExecuteUpdateAsync(s => s.SetProperty(x => x.State, "invalidated"));
    }
    public async Task Block(string me, string target, bool block)
    {
        if (me == target) throw ApiFailure.Invalid(); if (!await db.Users.AnyAsync(x => x.Id == target)) throw ApiFailure.Missing(); var key = me + ":" + target;
        if (block) { if (!await db.Blocks.AnyAsync(x => x.Id == key)) db.Blocks.Add(new() { Id = key, FromId = me, ToId = target }); await db.SaveChangesAsync(); await Remove(me, target); }
        else await db.Blocks.Where(x => x.Id == key).ExecuteDeleteAsync();
    }
    private async Task<PackInvitation> InvitationDto(InvitationRow x) => new(x.Id, await Profile(x.FromId), await Profile(x.ToId), new(x.ProjectId, x.VersionId, x.Hash), x.State, DateTimeOffset.FromUnixTimeMilliseconds(x.Created), DateTimeOffset.FromUnixTimeMilliseconds(x.Expires));
    public async Task<FriendPage<PackInvitation>> ListInvitations(string me, int page)
    {
        Page(page); var query = db.Invitations.Where(x => x.FromId == me || x.ToId == me).OrderByDescending(x => x.Created).ThenBy(x => x.Id); var result = new List<PackInvitation>();
        foreach (var x in await query.Skip(page * 20).Take(20).ToArrayAsync()) { if (x.State == "pending" && (!await Friends(x.FromId, x.ToId) || await Blocked(x.FromId, x.ToId))) x.State = "invalidated"; result.Add(await InvitationDto(x)); }
        await db.SaveChangesAsync(); return new(result, page, await query.CountAsync());
    }
    private async Task Active(InvitationRow x)
    { if (x.State != "pending" || x.Expires <= Now || !await Friends(x.FromId, x.ToId) || await Blocked(x.FromId, x.ToId)) throw ApiFailure.Conflict(); }
    public async Task<PackInvitation> GetInvitation(string me, string id)
    { var row = await db.Invitations.FindAsync(id) ?? throw ApiFailure.Missing(); if (row.FromId != me && row.ToId != me) throw ApiFailure.Forbidden(); await Active(row); return await InvitationDto(row); }
    public async Task<PackInvitation> SendInvitation(string me, SendPackInvitation input, CancellationToken ct)
    {
        if (input.Pack is null || input.Pack.ProjectId is null || input.Pack.VersionId is null || input.Pack.ArchiveSha512 is null || input.IdempotencyKey is null || input.IdempotencyKey.Length is < 1 or > 100 || input.IdempotencyKey.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_')) || input.Pack.ProjectId.Length is < 1 or > 128 || input.Pack.VersionId.Length is < 1 or > 128 || input.Pack.ArchiveSha512.Length != 128 || input.Pack.ArchiveSha512.Any(c => !Uri.IsHexDigit(c)) || input.Pack.ProjectId.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_')) || input.Pack.VersionId.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_'))) throw ApiFailure.Invalid();
        if (!await Friends(me, input.ToUserId) || await Blocked(me, input.ToUserId)) throw ApiFailure.Forbidden();
        var existing = await db.Invitations.SingleOrDefaultAsync(x => x.FromId == me && x.Key == input.IdempotencyKey);
        if (existing is not null) { if (existing.ToId != input.ToUserId || existing.ProjectId != input.Pack.ProjectId || existing.VersionId != input.Pack.VersionId || !existing.Hash.Equals(input.Pack.ArchiveSha512, StringComparison.OrdinalIgnoreCase)) throw ApiFailure.Conflict(); await Active(existing); return await InvitationDto(existing); }
        await Rate("user:" + me + ":invitations", 30, TimeSpan.FromDays(1));
        await operation.External(() => validator.ValidateAsync(input.Pack, ct));
        var authorization = contexts.HttpContext?.Request.Headers.Authorization.ToString();
        if (authorization is null || !authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) || await tickets.ValidateAsync(authorization[7..], "access") is null) throw ApiFailure.Unauthorized();
        if (!await Friends(me, input.ToUserId) || await Blocked(me, input.ToUserId)) throw ApiFailure.Forbidden();
        existing = await db.Invitations.SingleOrDefaultAsync(x => x.FromId == me && x.Key == input.IdempotencyKey);
        if (existing is not null) { if (existing.ToId != input.ToUserId || existing.ProjectId != input.Pack.ProjectId || existing.VersionId != input.Pack.VersionId || !existing.Hash.Equals(input.Pack.ArchiveSha512, StringComparison.OrdinalIgnoreCase)) throw ApiFailure.Conflict(); await Active(existing); return await InvitationDto(existing); }
        // Fresh transaction and authorization checks cover persistence after external I/O.
        var row = new InvitationRow { FromId = me, ToId = input.ToUserId, ProjectId = input.Pack.ProjectId, VersionId = input.Pack.VersionId, Hash = input.Pack.ArchiveSha512.ToLowerInvariant(), Key = input.IdempotencyKey, Created = Now, Expires = Now + Days(7) };
        db.Invitations.Add(row); await db.SaveChangesAsync(); return await InvitationDto(row);
    }
    public async Task ResolveInvitation(string me, string id, string action)
    {
        var row = await db.Invitations.FindAsync(id) ?? throw ApiFailure.Missing();
        if (action is not ("ignore" or "withdraw")) throw ApiFailure.Missing(); if ((action == "ignore" ? row.ToId : row.FromId) != me) throw ApiFailure.Forbidden(); var state = action == "ignore" ? "ignored" : "withdrawn";
        if (row.State == state) return; await Active(row); row.State = state; await db.SaveChangesAsync();
    }
}
