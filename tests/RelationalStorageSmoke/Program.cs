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
