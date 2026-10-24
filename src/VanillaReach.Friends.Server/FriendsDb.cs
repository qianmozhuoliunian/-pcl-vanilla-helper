using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using PclVersionBridge.Core.Friends;
namespace VanillaReach.Friends.Server;

public sealed class FriendUser : IdentityUser
{
    public string DisplayName { get; set; } = "";
    public string FriendCode { get; set; } = "";
    public FriendProfile Profile() => new(Id, DisplayName, FriendCode);
}
public sealed class AccountSession
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string UserId { get; set; } = "";
    public string RefreshNonce { get; set; } = "";
    public long Expires { get; set; }
}
public sealed class Friendship { public string Pair { get; set; } = ""; public string First { get; set; } = ""; public string Second { get; set; } = ""; }
public sealed class UserBlock { public string Id { get; set; } = ""; public string FromId { get; set; } = ""; public string ToId { get; set; } = ""; }
public sealed class RequestRow
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N"); public string FromId { get; set; } = ""; public string ToId { get; set; } = "";
    public string? PendingPair { get; set; }
    public string State { get; set; } = "pending";
    public long Created { get; set; }
    public long Expires { get; set; }
    public long Updated { get; set; }
}
public sealed class InvitationRow
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N"); public string FromId { get; set; } = ""; public string ToId { get; set; } = "";
    public string ProjectId { get; set; } = ""; public string VersionId { get; set; } = ""; public string Hash { get; set; } = ""; public string Key { get; set; } = "";
    public string State { get; set; } = "pending"; public long Created { get; set; }
    public long Expires { get; set; }
}
public sealed class RateRow { public string Id { get; set; } = ""; public int Count { get; set; } public long Expires { get; set; } }
public sealed class FriendsDb(DbContextOptions<FriendsDb> options) : IdentityDbContext<FriendUser>(options)
{
    public DbSet<EmailOutboxRow> EmailOutbox => Set<EmailOutboxRow>();
    public DbSet<AccountSession> Sessions => Set<AccountSession>();
    public DbSet<Friendship> Friendships => Set<Friendship>();
    public DbSet<UserBlock> Blocks => Set<UserBlock>();
    public DbSet<RequestRow> Requests => Set<RequestRow>();
    public DbSet<InvitationRow> Invitations => Set<InvitationRow>();
    public DbSet<RateRow> Rates => Set<RateRow>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.Entity<FriendUser>().HasIndex(x => x.FriendCode).IsUnique();
        b.Entity<FriendUser>().HasIndex(x => x.NormalizedEmail).IsUnique();
        b.Entity<Friendship>().HasKey(x => x.Pair);
        b.Entity<RequestRow>().HasIndex(x => x.PendingPair).IsUnique();
        b.Entity<RequestRow>().HasIndex(x => new { x.FromId, x.ToId, x.Created });
        b.Entity<InvitationRow>().HasIndex(x => new { x.FromId, x.Key }).IsUnique();
        b.Entity<EmailOutboxRow>().HasOne<FriendUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<AccountSession>().HasOne<FriendUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
public sealed class ApiFailure(int status, string code, string message, int? retry = null) : Exception(message)
{
    public int Status { get; } = status; public string Code { get; } = code; public int? Retry { get; } = retry;
    public static ApiFailure Invalid() => new(400, "invalid_input", "输入内容无效，请检查后重试。");
    public static ApiFailure Unauthorized() => new(401, "unauthorized", "登录已失效，请重新登录。");
    public static ApiFailure Conflict() => new(409, "state_conflict", "此操作已失效，请刷新后重试。");
    public static ApiFailure Forbidden() => new(403, "forbidden", "没有权限执行此操作。");
    public static ApiFailure Missing() => new(404, "unavailable", "暂时无法找到或使用该项目。");
}
