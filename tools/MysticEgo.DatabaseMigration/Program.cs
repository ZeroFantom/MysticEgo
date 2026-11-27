using Models.Data;
using System.Text.Json;

if(args.Length!=5||args[0]!="--stage-offline-confirmed")
{
    Console.Error.WriteLine("Usage: --stage-offline-confirmed NODE_DB AUDIT_DB PRIVATE_POSTGRES_CONNECTION_FILE PRIVATE_BACKUP_DIRECTORY");
    Console.Error.WriteLine("Stop all writers first. Destination must be a dedicated empty database. Does not switch the running service.");
    return 2;
}
try
{
    if(!OperatingSystem.IsWindows()&&(File.GetUnixFileMode(args[3])&(UnixFileMode.GroupRead|UnixFileMode.GroupWrite|UnixFileMode.OtherRead|UnixFileMode.OtherWrite))!=0)
        throw new InvalidOperationException("Connection file must be private.");
    var report=NodeDatabaseMigration.StageOffline(args[1],args[2],File.ReadAllText(args[3]).Trim(),args[4]);
    Console.WriteLine(JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
    Console.WriteLine("Staging verified. Original database and active configuration unchanged.");
    return 0;
}
catch(Exception error)
{
    // Never print connection strings, SQL values or provider exception details.
    Console.Error.WriteLine("Migration failed ("+error.GetType().Name+"). Source and configuration unchanged. Check protected backups and destination before retrying.");
    return 1;
}
