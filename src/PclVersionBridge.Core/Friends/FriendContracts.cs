namespace PclVersionBridge.Core.Friends;

public sealed record FriendProfile(string UserId, string DisplayName, string FriendCode);
public sealed record FriendPage<T>(IReadOnlyList<T> Items, int Page, int Total);
public sealed record FriendRequest(string Id, FriendProfile From, FriendProfile To, string State,
    DateTimeOffset CreatedAtUtc, DateTimeOffset ExpiresAtUtc);
public sealed record PackReference(string ProjectId, string VersionId, string ArchiveSha512);
public sealed record PackInvitation(string Id, FriendProfile From, FriendProfile To, PackReference Pack,
    string State, DateTimeOffset CreatedAtUtc, DateTimeOffset ExpiresAtUtc);
public sealed record FriendSession(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAtUtc, FriendProfile Me);
public sealed record RegisterFriendAccount(string Email, string Password, string DisplayName);
public sealed record ConfirmFriendEmail(string Email, string Code);
public sealed record FriendLogin(string Email, string Password);
public sealed record FriendEmail(string Email);
public sealed record FriendRefresh(string RefreshToken);
public sealed record ResetFriendPassword(string Email, string Code, string NewPassword);
public sealed record ChangeFriendPassword(string CurrentPassword, string NewPassword);
public sealed record DeleteFriendAccount(string Password);
public sealed record UpdateFriendProfile(string DisplayName);
public sealed record CreateFriendRequest(string FriendCode);
public sealed record SendPackInvitation(string ToUserId, PackReference Pack, string IdempotencyKey);
public sealed record FriendApiError(string Code, string Message, int? RetryAfterSeconds = null);

public interface IFriendSessionStore
{
    Task<FriendSession?> ReadAsync(CancellationToken token);
    Task WriteAsync(FriendSession session, CancellationToken token);
    Task ClearAsync(CancellationToken token);
}
public interface IFriendNotesStore
{
    Task<IReadOnlyDictionary<string,string>> ReadAsync(string ownerUserId, CancellationToken token);
    Task WriteAsync(string ownerUserId, IReadOnlyDictionary<string,string> notes, CancellationToken token);
    Task ClearAsync(CancellationToken token);
}
public interface IFriendService
{
    FriendSession? Session { get; }
    Task RestoreAsync(CancellationToken token);
    Task RegisterAsync(RegisterFriendAccount request, CancellationToken token);
    Task ConfirmEmailAsync(ConfirmFriendEmail request, CancellationToken token);
    Task ResendConfirmationAsync(FriendEmail request, CancellationToken token);
    Task LoginAsync(FriendLogin request, bool remember, CancellationToken token);
    Task LogoutAsync(CancellationToken token);
    Task ForgotPasswordAsync(FriendEmail request, CancellationToken token);
    Task ResetPasswordAsync(ResetFriendPassword request, CancellationToken token);
    Task ChangePasswordAsync(ChangeFriendPassword request, CancellationToken token);
    Task DeleteAccountAsync(DeleteFriendAccount request, CancellationToken token);
    Task LogoutAllAsync(CancellationToken token);
    Task<FriendProfile> GetMeAsync(CancellationToken token);
    Task<FriendProfile> UpdateProfileAsync(UpdateFriendProfile request, CancellationToken token);
    Task<FriendProfile> ResetFriendCodeAsync(CancellationToken token);
    Task<FriendProfile> LookupAsync(string code, CancellationToken token);
    Task<FriendPage<FriendProfile>> GetFriendsAsync(int page, CancellationToken token);
    Task<FriendPage<FriendRequest>> GetRequestsAsync(int page, CancellationToken token);
    Task<FriendPage<FriendProfile>> GetBlocksAsync(int page, CancellationToken token);
    Task SendRequestAsync(string code, CancellationToken token);
    Task ResolveRequestAsync(string id, string action, CancellationToken token);
    Task RemoveFriendAsync(string userId, CancellationToken token);
    Task BlockAsync(string userId, CancellationToken token);
    Task UnblockAsync(string userId, CancellationToken token);
    Task<FriendPage<PackInvitation>> GetInvitationsAsync(int page, CancellationToken token);
    Task<PackInvitation> GetInvitationAsync(string id, CancellationToken token);
    Task<PackInvitation> SendInvitationAsync(SendPackInvitation request, CancellationToken token);
    Task ResolveInvitationAsync(string id, string action, CancellationToken token);
}
