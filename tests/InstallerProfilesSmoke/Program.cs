using System.Security.Cryptography;

var scratch=Path.Combine(Path.GetTempPath(),"mysticego-profile-tests-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(scratch);
try
{
    void Check(bool result,string name){if(!result)throw new Exception("FAILED: "+name);Console.WriteLine("PASS: "+name);}
    void ExpectThrow<T>(Action action,string name) where T:Exception
    {try{action();throw new Exception("FAILED: "+name);}catch(T){Console.WriteLine("PASS: "+name);}}

    Check(InstallerProfiles.Get(new Dictionary<string,string>())=="production","safe default production");
    Check(InstallerProfiles.Get(new Dictionary<string,string>{{"profile","test"}})=="test","explicit test profile");
    ExpectThrow<ArgumentException>(()=>InstallerProfiles.Get(new Dictionary<string,string>{{"profile","unknown"}}),"unknown profile rejected");
    Check(InstallerProfiles.ResolveBootstrapPassword("test")==InstallerProfiles.TestPassword,"test password stable");
    ExpectThrow<ArgumentException>(()=>InstallerProfiles.ValidateTestScope("test",new Dictionary<string,string>{{"auth-url","https://example.invalid"}}),"test remote URL blocked");
    ExpectThrow<ArgumentException>(()=>InstallerProfiles.ValidateTestScope("test",new Dictionary<string,string>(),remote:true),"test SSH blocked");

    var testDir=Path.Combine(scratch,"test");Directory.CreateDirectory(testDir);
    InstallerProfiles.Mark(testDir,"test");InstallerProfiles.ValidateTarget(testDir,"test");
    ExpectThrow<InvalidOperationException>(()=>InstallerProfiles.ValidateTarget(testDir,"production"),"profile cross-grade blocked");

    var first=Path.Combine(testDir,"private.pem");var firstPub=Path.Combine(testDir,"public.pem");
    InstallerProfiles.WriteSigningKeys("test",first,firstPub);
    var testKey=File.ReadAllText(first);
    InstallerProfiles.WriteSigningKeys("test",first,firstPub);
    Check(File.ReadAllText(first)==testKey,"test signing key stable across reinstall");
    ExpectThrow<InvalidOperationException>(()=>InstallerProfiles.WriteSigningKeys("production",first,firstPub),"test key prohibited in production");
    File.Delete(firstPub);
    ExpectThrow<InvalidOperationException>(()=>InstallerProfiles.WriteSigningKeys("test",first,firstPub),"incomplete keypair rejected");

    var prodDir=Path.Combine(scratch,"prod");Directory.CreateDirectory(prodDir);
    var prodPrivate=Path.Combine(prodDir,"private.pem");var prodPublic=Path.Combine(prodDir,"public.pem");
    InstallerProfiles.WriteSigningKeys("production",prodPrivate,prodPublic);
    Check(!File.ReadAllText(prodPrivate).Equals(testKey,StringComparison.Ordinal),"production key freshly generated");
    var unchanged=File.ReadAllText(prodPrivate);
    InstallerProfiles.WriteSigningKeys("production",prodPrivate,prodPublic);
    Check(unchanged==File.ReadAllText(prodPrivate),"production key never rotates silently");

    var oldDir=Path.Combine(scratch,"old");Directory.CreateDirectory(oldDir);File.WriteAllText(Path.Combine(oldDir,"existing.env"),"legacy");
    ExpectThrow<InvalidOperationException>(()=>InstallerProfiles.ValidateTarget(oldDir,"production"),"legacy unmarked directory rejected");
    Console.WriteLine("All installer profile smoke tests passed.");
}
finally{Directory.Delete(scratch,recursive:true);}
