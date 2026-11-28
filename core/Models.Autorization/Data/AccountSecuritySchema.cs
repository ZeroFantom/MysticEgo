using Microsoft.EntityFrameworkCore;
using System.Data;

namespace Models.Autorization.Data;

public static class AccountSecuritySchema
{
    // Additive compatibility upgrade for existing SQLite and PostgreSQL installations.
    public static async Task UpgradeAsync(AuthDbContext db,CancellationToken ct)
    {
        if(db.Database.IsNpgsql())
        {
            await db.Database.ExecuteSqlRawAsync("""
                ALTER TABLE refresh_tokens ADD COLUMN IF NOT EXISTS "SessionId" uuid NULL;
                ALTER TABLE refresh_tokens ADD COLUMN IF NOT EXISTS "LastSeenUtc" timestamp with time zone NULL;
                """,ct);
        }
        else
        {
            var connection=db.Database.GetDbConnection();
            var opened=connection.State!=ConnectionState.Open;
            if(opened)await connection.OpenAsync(ct);
            try
            {
                var columns=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                await using(var cmd=connection.CreateCommand())
                {
                    cmd.CommandText="PRAGMA table_info(refresh_tokens)";
                    await using var reader=await cmd.ExecuteReaderAsync(ct);
                    while(await reader.ReadAsync(ct))columns.Add(reader.GetString(1));
                }
                if(!columns.Contains("SessionId"))await db.Database.ExecuteSqlRawAsync("ALTER TABLE refresh_tokens ADD COLUMN \"SessionId\" TEXT NULL",ct);
                if(!columns.Contains("LastSeenUtc"))await db.Database.ExecuteSqlRawAsync("ALTER TABLE refresh_tokens ADD COLUMN \"LastSeenUtc\" TEXT NULL",ct);
            }
            finally{if(opened)await connection.CloseAsync();}
        }
        await db.Database.ExecuteSqlRawAsync("CREATE UNIQUE INDEX IF NOT EXISTS \"IX_refresh_tokens_SessionId\" ON refresh_tokens (\"SessionId\")",ct);
        var guid=db.Database.IsNpgsql()?"uuid":"TEXT";
        var timestamp=db.Database.IsNpgsql()?"timestamp with time zone":"TEXT";
        var boolean=db.Database.IsNpgsql()?"boolean":"INTEGER";
        var sql=$"""
            CREATE TABLE IF NOT EXISTS external_identity_audit (
                "Id" {guid} NOT NULL PRIMARY KEY,
                "UserId" {guid} NOT NULL REFERENCES users("Id") ON DELETE CASCADE,
                "SessionId" {guid} NOT NULL,
                "Provider" varchar(16) NOT NULL,
                "Realm" varchar(128) NOT NULL,
                "Action" varchar(16) NOT NULL,
                "OccurredUtc" {timestamp} NOT NULL,
                "RevokedSessions" INTEGER NOT NULL
            );
            CREATE INDEX IF NOT EXISTS "IX_external_identity_audit_UserId_OccurredUtc"
                ON external_identity_audit ("UserId","OccurredUtc");
            CREATE TABLE IF NOT EXISTS external_link_intents (
                "CodeHash" varchar(64) NOT NULL PRIMARY KEY,
                "UserId" {guid} NOT NULL REFERENCES users("Id") ON DELETE CASCADE,
                "SessionId" {guid} NOT NULL,
                "Provider" varchar(16) NOT NULL,
                "Realm" varchar(128) NOT NULL,
                "ExpiresUtc" {timestamp} NOT NULL,
                "VerifiedSubject" varchar(128) NULL
            );
            CREATE INDEX IF NOT EXISTS "IX_external_link_intents_ExpiresUtc"
                ON external_link_intents ("ExpiresUtc");
            CREATE INDEX IF NOT EXISTS "IX_external_link_intents_UserId"
                ON external_link_intents ("UserId");
            CREATE TABLE IF NOT EXISTS external_oauth_flows (
                "StateHash" varchar(64) NOT NULL PRIMARY KEY,
                "BrowserHash" varchar(64) NOT NULL,
                "LinkHash" varchar(64) NOT NULL REFERENCES external_link_intents("CodeHash") ON DELETE CASCADE,
                "Provider" varchar(16) NOT NULL,
                "ClientId" varchar(20) NOT NULL,
                "RedirectUri" varchar(2048) NOT NULL,
                "ProtectedSecrets" varchar(4096) NOT NULL,
                "ExpiresUtc" {timestamp} NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_external_oauth_flows_LinkHash"
                ON external_oauth_flows ("LinkHash");
            CREATE INDEX IF NOT EXISTS "IX_external_oauth_flows_ExpiresUtc"
                ON external_oauth_flows ("ExpiresUtc");
            CREATE TABLE IF NOT EXISTS external_identities (
                "Provider" varchar(16) NOT NULL,
                "Realm" varchar(128) NOT NULL,
                "Subject" varchar(128) NOT NULL,
                "UserId" {guid} NOT NULL REFERENCES users("Id") ON DELETE CASCADE,
                "LinkedUtc" {timestamp} NOT NULL,
                PRIMARY KEY ("Provider","Realm","Subject")
            );
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_external_identities_UserId_Provider_Realm"
                ON external_identities ("UserId","Provider","Realm");
            CREATE TABLE IF NOT EXISTS account_license_acceptances (
                "UserId" {guid} NOT NULL REFERENCES users("Id") ON DELETE CASCADE,
                "Version" varchar(80) NOT NULL,
                "AcceptedUtc" {timestamp} NOT NULL,
                PRIMARY KEY ("UserId","Version")
            );
            CREATE TABLE IF NOT EXISTS account_feature_consents (
                "UserId" {guid} NOT NULL REFERENCES users("Id") ON DELETE CASCADE,
                "Scope" varchar(40) NOT NULL,
                "PolicyVersion" varchar(40) NOT NULL,
                "Enabled" {boolean} NOT NULL,
                "UpdatedUtc" {timestamp} NOT NULL,
                PRIMARY KEY ("UserId","Scope")
            );
            CREATE TABLE IF NOT EXISTS account_device_grants (
                "UserId" {guid} NOT NULL REFERENCES users("Id") ON DELETE CASCADE,
                "DeviceId" {guid} NOT NULL,
                "GrantId" {guid} NOT NULL,
                "Scope" varchar(40) NOT NULL,
                "PolicyVersion" varchar(40) NOT NULL,
                "GrantedUtc" {timestamp} NOT NULL,
                "RevokedUtc" {timestamp} NULL,
                PRIMARY KEY ("UserId","DeviceId","Scope")
            );
            """;
        await db.Database.ExecuteSqlRawAsync(sql,ct);
    }
}
