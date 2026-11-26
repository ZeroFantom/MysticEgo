using System.Security.AccessControl;
using System.Security.Principal;
using System.Security.Cryptography;
using System.Text;
using Renci.SshNet;

sealed partial class CloudDesk
{
    static string LocalState(string userHome)
    {
        var legacy=Path.Combine(userHome,".local","share","mysticego-clouddesk");
        if(!OperatingSystem.IsWindows()||Directory.Exists(legacy))return legacy;
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"MysticEgo","CloudDesk");
    }

    // Explicit ACL: do not silently skip permissions when porting chmod to NTFS.
    static void SetPrivateMode(string path, UnixFileMode mode)
    {
        if(!OperatingSystem.IsWindows()){File.SetUnixFileMode(path,mode);return;}
        if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new IOException("Небезопасная ссылка: "+path);
        using var identity=WindowsIdentity.GetCurrent();
        var user=identity.User??throw new IOException("Нет SID текущего пользователя Windows.");
        if(Directory.Exists(path))
        {
            var acl=new DirectorySecurity();
            acl.SetOwner(user);acl.SetAccessRuleProtection(true,false);
            acl.AddAccessRule(new FileSystemAccessRule(user,FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));
            new DirectoryInfo(path).SetAccessControl(acl);
        }
        else
        {
            var acl=new FileSecurity();
            acl.SetOwner(user);acl.SetAccessRuleProtection(true,false);
            acl.AddAccessRule(new FileSystemAccessRule(user,FileSystemRights.FullControl,AccessControlType.Allow));
            new FileInfo(path).SetAccessControl(acl);
        }
    }
    static string LocalTool(string command)
    {
        if(!OperatingSystem.IsWindows())return command;
        if(command is not ("ssh" or "scp" or "ssh-keygen" or "ssh-keyscan" or "gh"))return command;
        var tool=command+".exe";
        var app=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,".."));
        // System OpenSSH first if the full client toolset exists; otherwise the pinned portable client.
        if(command!="gh")
        {
            var system=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"OpenSSH");
            var required=new[]{"ssh.exe","scp.exe","ssh-keygen.exe","ssh-keyscan.exe"};
            if(required.All(name=>File.Exists(Path.Combine(system,name))))return Path.Combine(system,tool);
            var bundled=Path.Combine(app,"windows-tools","openssh",tool);
            if(File.Exists(bundled))return bundled;
        }
        else
        {
            var bundled=Path.Combine(app,"windows-tools","github",tool);
            if(File.Exists(bundled))return bundled;
            var installed=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"GitHub CLI",tool);
            if(File.Exists(installed))return installed;
        }
        return tool;
    }
    static string SshConfigPath(string path)
    {
        if(path.Any(c=>c=='"'||c=='\n'||c=='\r'||c=='\0'))throw new IOException("Некорректный путь SSH.");
        return "\""+path.Replace('\\','/').Replace("%","%%")+"\"";
    }
    static string WriteSshConfig(string directory,string known)
    {
        var empty=Path.Combine(directory,"empty_known_hosts");
        var config=Path.Combine(directory,"ssh_config");
        foreach(var p in new[]{empty,config})
            if(File.Exists(p)&&(File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0)throw new IOException("Небезопасный файл SSH.");
        // No global known_hosts or user ssh_config can override the application's trust policy.
        var value="Host *\n  StrictHostKeyChecking yes\n  UserKnownHostsFile "+SshConfigPath(known)+
            "\n  GlobalKnownHostsFile "+SshConfigPath(empty)+
            "\n  HostKeyAlgorithms ssh-ed25519\n  UpdateHostKeys no\n  CheckHostIP no\n  ConnectTimeout 10\n  ServerAliveInterval 15\n  ServerAliveCountMax 2\n  ForwardAgent no\n  ClearAllForwardings yes\n";
        if(!File.Exists(empty)||new FileInfo(empty).Length!=0)WritePrivateFile(empty,[]);
        if(!File.Exists(config)||File.ReadAllText(config)!=value)WritePrivateFile(config,Encoding.UTF8.GetBytes(value));
        return config;
    }
    static void WritePrivateFile(string path,byte[] bytes)
    {
        var tmp=path+"."+Guid.NewGuid().ToString("N");
        try
        {
            using(var output=new FileStream(tmp,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            {
                SetPrivateMode(tmp,Private);
                output.Write(bytes);
            }
            File.Move(tmp,path,true);
        }
        finally{if(File.Exists(tmp))File.Delete(tmp);}
    }
    static byte[] ProtectWindows(byte[] bytes)=>ProtectedData.Protect(bytes,null,DataProtectionScope.CurrentUser);
    static byte[] UnprotectWindows(byte[] bytes)=>ProtectedData.Unprotect(bytes,null,DataProtectionScope.CurrentUser);
    async Task SaveTimewebToken(string value)
    {
        if(!OperatingSystem.IsWindows()){
            await Exec("secret-tool",["store","--label=MysticEgo CloudDesk Timeweb","application","mysticego-clouddesk","provider","timeweb"],value,1);return;
        }
        Init();
        var bytes=Encoding.UTF8.GetBytes(value);
        try{WritePrivateFile(Path.Combine(State,"timeweb.dpapi"),ProtectWindows(bytes));}
        finally{CryptographicOperations.ZeroMemory(bytes);}
    }
    async Task<string> ReadTimewebToken()
    {
        if(!OperatingSystem.IsWindows())return await Exec("secret-tool",["lookup","application","mysticego-clouddesk","provider","timeweb"],minutes:1);
        Init();var p=Path.Combine(State,"timeweb.dpapi");if(!File.Exists(p))return "";
        var plain=UnprotectWindows(File.ReadAllBytes(p));
        try{return Encoding.UTF8.GetString(plain);}finally{CryptographicOperations.ZeroMemory(plain);}
    }
    async Task ClearTimewebToken()
    {
        if(!OperatingSystem.IsWindows()){await Exec("secret-tool",["clear","application","mysticego-clouddesk","provider","timeweb"],minutes:1);return;}
        Init();File.Delete(Path.Combine(State,"timeweb.dpapi"));
    }
    static HashSet<string> TrustedHostKeys(IEnumerable<string> known)
    {
        var keys=new HashSet<string>(StringComparer.Ordinal);
        foreach(var row in known)
        {
            var columns=Parts(row);
            if(columns.Length<3||columns[1]!="ssh-ed25519")continue;
            try{keys.Add(Convert.ToBase64String(Convert.FromBase64String(columns[2])));}catch(FormatException){}
        }
        return keys;
    }
    static void InstallPublicKeyWithPassword(string host,int port,string user,string password,string publicKey,HashSet<string> expected)
    {
        using var client=new SshClient(host,port,user,password);
        client.ConnectionInfo.Timeout=TimeSpan.FromSeconds(15);
        foreach(var algorithm in client.ConnectionInfo.HostKeyAlgorithms.Keys.ToArray())
            if(algorithm!="ssh-ed25519")client.ConnectionInfo.HostKeyAlgorithms.Remove(algorithm);
        client.HostKeyReceived+=(_,e)=>e.CanTrust=e.HostKeyName=="ssh-ed25519"&&expected.Contains(Convert.ToBase64String(e.HostKey));
        client.Connect();
        // publicKey is parsed and validated by PublicPart. ShellQuote handles the fixed public value.
        var script="set -eu; umask 077; mkdir -p \"$HOME/.ssh\"; chmod 700 \"$HOME/.ssh\"; touch \"$HOME/.ssh/authorized_keys\"; chmod 600 \"$HOME/.ssh/authorized_keys\"; k="+ShellQuote(publicKey)+"; grep -qxF -- \"$k\" \"$HOME/.ssh/authorized_keys\" || printf '\\n%s\\n' \"$k\" >> \"$HOME/.ssh/authorized_keys\"";
        using var command=client.CreateCommand(script);
        command.CommandTimeout=TimeSpan.FromSeconds(30);command.Execute();
        if(command.ExitStatus!=0)throw new IOException("SSH command failed.");
        client.Disconnect();
    }
    async Task<object> PlatformSelftest()
    {
        var dir=Path.Combine(Path.GetTempPath(),"MysticEgo platform "+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        int passed=0;
        void Test(bool ok){if(!ok)throw new Exception("Platform selftest #"+(passed+1));passed++;}
        try
        {
            SetPrivateMode(dir,Private|UnixFileMode.UserExecute);
            var file=Path.Combine(dir,"private");
            WritePrivateFile(file,Encoding.UTF8.GetBytes("selftest"));
            Test(File.ReadAllText(file)=="selftest");
            if(OperatingSystem.IsWindows())
            {
                using var identity=WindowsIdentity.GetCurrent();
                var acl=new FileInfo(file).GetAccessControl();
                Test(acl.AreAccessRulesProtected&&acl.GetAccessRules(true,true,typeof(SecurityIdentifier)).OfType<FileSystemAccessRule>().All(rule=>rule.IdentityReference.Equals(identity.User)));
                var plain=Encoding.UTF8.GetBytes("synthetic-test-not-a-token");
                Test(UnprotectWindows(ProtectWindows(plain)).SequenceEqual(plain));
            }
            else Test(File.GetUnixFileMode(file)==Private);
            var known=Path.Combine(dir,"known_hosts");WritePrivateFile(known,[]);
            var cfg=WriteSshConfig(dir,known);
            Test(File.ReadAllText(cfg).Contains("StrictHostKeyChecking yes"));
            var generated=Path.Combine(dir,"test-key");
            await Exec("ssh-keygen",["-q","-t","ed25519","-N","","-f",generated],minutes:1);
            SetPrivateMode(generated,Private);
            Test(PublicPart(await Exec("ssh-keygen",["-y","-P","","-f",generated],minutes:1)).StartsWith("ssh-ed25519 "));
            var resolved=await Exec("ssh",["-G","-F",cfg,"localhost"],minutes:1);
            Test(resolved.Contains("stricthostkeychecking true")||resolved.Contains("stricthostkeychecking yes"));
            Test(resolved.Contains("hostkeyalgorithms ssh-ed25519"));
            Test(TrustedHostKeys(["host ssh-ed25519 YQ==","host ssh-rsa Yg==","invalid"]).SetEquals(["YQ=="]));
            return new{passed,platform=OperatingSystem.IsWindows()?"windows":"linux"};
        }
        finally{Directory.Delete(dir,true);}
    }
}
