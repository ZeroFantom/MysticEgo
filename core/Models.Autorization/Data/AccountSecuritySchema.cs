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
            CREATE TABLE IF NOT EXISTS account_mobile_agents (
                "Id" {guid} NOT NULL PRIMARY KEY,
                "UserId" {guid} NOT NULL REFERENCES users("Id") ON DELETE CASCADE,
                "SecretHash" varchar(64) NOT NULL,
                "Name" varchar(100) NOT NULL,
                "Os" varchar(40) NOT NULL,
                "CreatedUtc" {timestamp} NOT NULL,
                "LastSeenUtc" {timestamp} NULL,
                "RevokedUtc" {timestamp} NULL
            );
            CREATE INDEX IF NOT EXISTS "IX_account_mobile_agents_UserId" ON account_mobile_agents ("UserId");
            CREATE TABLE IF NOT EXISTS mobile_agent_actions (
                "Id" {guid} NOT NULL PRIMARY KEY,
                "UserId" {guid} NOT NULL REFERENCES users("Id") ON DELETE CASCADE,
                "Source" {guid} NOT NULL,
                "Target" {guid} NOT NULL,
                "Action" varchar(32) NOT NULL,
                "State" varchar(32) NOT NULL,
                "Result" varchar(8) NULL,
                "CreatedUtc" {timestamp} NOT NULL,
                "UpdatedUtc" {timestamp} NOT NULL,
                "ExpiresUtc" {timestamp} NOT NULL
            );
            CREATE INDEX IF NOT EXISTS "IX_mobile_agent_actions_UserId_CreatedUtc" ON mobile_agent_actions ("UserId","CreatedUtc");
            CREATE INDEX IF NOT EXISTS "IX_mobile_agent_actions_Target_ExpiresUtc" ON mobile_agent_actions ("Target","ExpiresUtc");
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
