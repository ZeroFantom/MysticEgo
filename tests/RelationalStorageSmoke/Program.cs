using Models.Data;
using MysticEgo.Distributed;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Npgsql;

var tmp=Directory.CreateTempSubdirectory("mystic-db-").FullName;
void Check(bool value,string label){if(!value)throw new Exception(label);}
void Reject(Action action,string label){try{action();}catch(InvalidOperationException){return;}throw new Exception(label);}
async Task Exercise(string connection,string label)
{
    var store=new DynamicEntityStore(connection);
    var id=Guid.NewGuid();
    var record=new RecordDocument{Id=id,Collection="smoke",Name="first",Data=JsonSerializer.SerializeToElement(new{value=1})};
    await store.PutAsync(record);
    Check((await store.GetAsync<RecordDocument>(id))?.Name=="first",label+" put/get");
    record.Name="changed";
    await store.PutAsync(record);
    Check((await store.ListAsync<RecordDocument>()).Any(x=>x.Id==id&&x.Name=="changed"),label+" update/list");
    Check(await store.DeleteAsync<RecordDocument>(id)&&await store.GetAsync<RecordDocument>(id)==null,label+" delete");
    var journal=new AuditJournal(Path.Combine(tmp,label+".audit.db"),RelationalStorage.IsPostgres(connection)?connection:null);
    var entry=new AuditEntry(Guid.NewGuid(),DateTimeOffset.UtcNow,"database-test","storage.smoke",id.ToString(),true,null);
    journal.Append(entry);
    journal.Append(entry with {Id=Guid.NewGuid(),Success=false,Detail="test detail"});
    var page=await journal.Read(1,5,"DATABASE-TEST",true,id.ToString(),CancellationToken.None);
    Check(page.Total==1&&page.Items.Single().Id==entry.Id&&page.Items[0].Detail==null,label+" filtered journal");
    var failed=await journal.Read(1,5,"test detail",false,id.ToString(),CancellationToken.None);
    Check(failed.Total==1&&failed.Items[0].Detail=="test detail",label+" failure journal");
    Console.WriteLine(label+" CRUD and journal passed");
}
try
{
    var local=Path.Combine(tmp,"node.db");
    var sqlite=RelationalStorage.NodeConnection(local,null,null);
    await Exercise(sqlite,"SQLite");
    Reject(()=>RelationalStorage.NodeConnection(local,"Host=invalid;Database=test",null),"existing SQLite must not switch");
    Reject(()=>RelationalStorage.NodeConnection(Path.Combine(tmp,"new.db"),null,Path.Combine(tmp,"missing.connection")),"missing config must fail");
    var pg=Environment.GetEnvironmentVariable("MYSTICEGO_TEST_POSTGRES");
    if(string.IsNullOrEmpty(pg))throw new Exception("Integration PostgreSQL is required");
    // A disposable database must be empty before staging. Use genuine records, non-ASCII text,
    // nullable details and a deliberately failed copy to exercise transactional rollback.
    var sourcePath=Path.Combine(tmp,"migration.db");
    var sourceConnection=RelationalStorage.NodeConnection(sourcePath,null,null);
    var sourceStore=new DynamicEntityStore(sourceConnection);
    var identity=Guid.NewGuid();
    var migrated=new RecordDocument{Id=identity,Collection="migration",Name="Мир MysticEgo",Data=JsonSerializer.SerializeToElement(new{value=42,note="Эфир"})};
    await sourceStore.PutAsync(migrated);
    var auditPath=sourcePath+".audit.db";
    var sourceJournal=new AuditJournal(auditPath);
    var eventId=Guid.NewGuid();
    var entry=new AuditEntry(eventId,DateTimeOffset.UtcNow,"пользователь","migration.smoke",identity.ToString(),true,null);
    sourceJournal.Append(entry);
    sourceJournal.Append(entry with { Id=Guid.NewGuid(), Success=false, Detail="Проверка" });
    var badEventId=Guid.NewGuid();
    sourceJournal.Append(entry with { Id=badEventId, Detail="bad"+(char)0+"text" });
    SqliteConnection.ClearAllPools();
    Reject(()=>NodeDatabaseMigration.StageOffline(sourcePath,auditPath,pg,tmp),"bad text must roll back staged tables");
    using(var conn=new NpgsqlConnection(pg))
    {
        conn.Open();
        using var count=new NpgsqlCommand("SELECT count(*) FROM pg_catalog.pg_class c JOIN pg_catalog.pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public'",conn);
        Check(Convert.ToInt64(count.ExecuteScalar())==0,"failed migration left partial destination");
    }
    using(var conn=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=auditPath}.ConnectionString))
    {
        conn.Open();
        using var delete=conn.CreateCommand();
        delete.CommandText="DELETE FROM action_journal WHERE event_id=@id";
        delete.Parameters.AddWithValue("@id",badEventId.ToString());
        Check(delete.ExecuteNonQuery()==1,"source row repair in fixture");
    }
    SqliteConnection.ClearAllPools();
    var report=NodeDatabaseMigration.StageOffline(sourcePath,auditPath,pg,tmp);
    Check(report.Tables.Count==3&&report.Tables.Sum(t=>t.Rows)==4,"verified migration counts");
    Check(report.Tables.All(t=>t.Sha256.Length==64),"missing data digest");
    Check(File.Exists(Path.Combine(report.BackupDirectory,"node.db"))&&File.Exists(Path.Combine(report.BackupDirectory,"node.db.audit.db")),"missing protected backup");
    var migratedStore=new DynamicEntityStore(pg);
    var transferred=await migratedStore.GetAsync<RecordDocument>(identity);
    Check(transferred?.Name==migrated.Name&&transferred.Data.GetProperty("value").GetInt32()==42,"migrated entity data/identity");
    var migratedJournal=new AuditJournal(auditPath,pg);
    var journalPage=await migratedJournal.Read(1,10,"migration.smoke",null,identity.ToString(),CancellationToken.None);
    Check(journalPage.Total==2&&journalPage.Items.Any(x=>x.Id==eventId&&x.Detail==null&&x.Success),"migrated audit data/identity");
    var backupStore=new DynamicEntityStore(new SqliteConnectionStringBuilder{DataSource=Path.Combine(report.BackupDirectory,"node.db")}.ConnectionString);
    Check((await backupStore.GetAsync<RecordDocument>(identity))?.Name==migrated.Name,"backup restore/read verification");
    Check((await sourceStore.GetAsync<RecordDocument>(identity))?.Name==migrated.Name,"original source lost");
    Reject(()=>NodeDatabaseMigration.StageOffline(sourcePath,auditPath,pg,tmp),"nonempty destination must not be overwritten");
    Console.WriteLine("Verified offline migration: rollback, backups, hashes, IDs and audit passed");
    var remote=Path.Combine(tmp,"remote.db");
    var file=Path.Combine(tmp,"postgres.connection");
    File.WriteAllText(file,pg);
    var configured=RelationalStorage.NodeConnection(remote,null,file);
    await Exercise(configured,"PostgreSQL");
    Reject(()=>RelationalStorage.NodeConnection(remote,null,null),"must not fall back to SQLite");
    Check(!File.Exists(remote),"remote must not create local DB");
    Check(File.Exists(remote+".postgres-configured"),"provider marker missing");
}
finally{SqliteConnection.ClearAllPools();NpgsqlConnection.ClearAllPools();Directory.Delete(tmp,true);}
