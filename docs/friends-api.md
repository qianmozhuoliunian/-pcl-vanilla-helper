# Friends API v1 (implementation contract)

All DTOs are `PclVersionBridge.Core.Friends` records with camelCase JSON. Page is zero-based, size20. Errors are FriendApiError with Chinese safe text; status401 revoked/invalid auth,403 unauthorized operation,404 unknown,409 state conflict,429 rate limit (Retry-After),503 source unavailable. Mutation success without body is204. FriendSession includes current public profile. Never expose emails through public profiles. Case-insensitive friend-code input strips hyphens/spaces and matches12 Crockford characters.

Unauthenticated:

`GET /health` returns service/status; explicit local Development also returns its process ID for preview-kit ownership checks. This is a health marker, not authentication.

| Method | Route | Body | Result |
|---|---|---|---|
|POST|/api/account/register|RegisterFriendAccount|204, confirmation email queued|
|POST|/api/account/confirm|ConfirmFriendEmail|204|
|POST|/api/account/resend-confirmation|FriendEmail|204 generic|
|POST|/api/account/login|FriendLogin|FriendSession|
|POST|/api/account/refresh|FriendRefresh|FriendSession|
|POST|/api/account/forgot-password|FriendEmail|204 generic|
|POST|/api/account/reset-password|ResetFriendPassword|204|

Authenticated (verified email required):

|Method|Route|Body|Result|
|---|---|---|---|
|POST|/api/account/logout|{}|204, revoke all sessions for simple v1|
|POST|/api/account/logout-all|{}|204, revoke all sessions|
|POST|/api/account/change-password|ChangeFriendPassword|204, revoke all sessions|
|POST|/api/account/delete|DeleteFriendAccount|204, remove user and social state|
|GET|/api/me||FriendProfile|
|PATCH|/api/me|UpdateFriendProfile|FriendProfile|
|POST|/api/me/reset-code|{}|FriendProfile|
|GET|/api/profiles/{code}||FriendProfile, only minimal data|
|GET|/api/friends?page=0||FriendPage<FriendProfile>|
|DELETE|/api/friends/{userId}||204|
|GET|/api/friend-requests?page=0||FriendPage<FriendRequest>, both directions|
|POST|/api/friend-requests|CreateFriendRequest|204|
|POST|/api/friend-requests/{id}/{accept,reject,withdraw}|{}|204|
|GET|/api/blocks?page=0||FriendPage<FriendProfile>|
|PUT|/api/blocks/{userId}|{}|204|
|DELETE|/api/blocks/{userId}||204|
|GET|/api/invitations?page=0||FriendPage<PackInvitation>, both directions|
|POST|/api/invitations|SendPackInvitation|PackInvitation|
|GET|/api/invitations/{id}||PackInvitation, active permission recheck|
|POST|/api/invitations/{id}/{ignore,withdraw}|{}|204|

Only IDs, source SHA512, and IdempotencyKey cross invitation transport. Pack name/image/description resolve from Modrinth at client; server also validates type/Fabric/project/version/hash before accepting. No arbitrary URL accepted. State strings are pending/accepted/rejected/withdrawn/expired/invalidated for requests, and pending/ignored/withdrawn/expired/invalidated for invitations. Block/remove invalidates both users' pending requests/cards. Server source-validation injection for tests must not be enabled by production settings.

Server configuration: `Friends:PublicOrigin` HTTPS; `Friends:DataDirectory`; `Friends:DevelopmentInbox` path only in Development+loopback. SMTP via `Friends:Smtp:{Host,Port,User,Password,From}` (secrets environment only). Client configuration file beside exe `friends-service.json`: `{ "serviceUrl": "https://.../", "allowLoopbackDevelopment": false }`. Optional `VANILLAREACH_FRIENDS_SERVICE_URL`, `VANILLAREACH_FRIENDS_DEVELOPMENT` overrides for explicit local kit; app displays local-test warning. Default no config means friends unavailable without affecting installer.
