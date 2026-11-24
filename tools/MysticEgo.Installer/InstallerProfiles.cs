using System.Reflection;
using System.Security.Cryptography;
using System.Text;

// Profiles are intentionally mutually exclusive. The test signing key is PUBLICLY KNOWN.
// Never use --profile test with externally reachable endpoints or production data.
internal static class InstallerProfiles
{
    internal const string TestLogin = "developer";
    internal const string TestPassword = "MysticEgo-Test-Only-2026!";
    internal const string ProductionLogin = "owner";
    private const string Marker = ".mysticego-profile";

    internal static string Get(IReadOnlyDictionary<string,string> args)
    {
        var profile=args.GetValueOrDefault("profile")??"production";
        if(profile is not ("test" or "production"))throw new ArgumentException("--profile must be test or production.");
        return profile;
    }

    internal static void ValidateTarget(string output,string profile)
    {
        var marker=Path.Combine(output,Marker);
        if(File.Exists(marker))
        {
            if(!string.Equals(File.ReadAllText(marker).Trim(),profile,StringComparison.Ordinal))
                throw new InvalidOperationException("Deployment profile mismatch: refusing to mix test and production data/keys.");
            return;
        }
        if(Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
            throw new InvalidOperationException("Existing installation has no profile marker. Use a fresh output directory or migrate it explicitly; automatic adoption is unsafe.");
    }

    internal static void Mark(string output,string profile)
    {
        File.WriteAllText(Path.Combine(output,Marker),profile+"\n");
        Restrict(Path.Combine(output,Marker));
    }

    internal static void ValidateTestScope(string profile,IReadOnlyDictionary<string,string> flags,bool remote=false)
    {
        if(profile!="test")return;
        if(remote || flags.ContainsKey("plan-id") || flags.ContainsKey("manifest") || new[]{"auth-url","orchestra-url","global-url","local-url","audit-url"}.Any(flags.ContainsKey))
            throw new ArgumentException("Test profile is restricted to an isolated local installation without remote endpoints or manifests.");
    }

    internal static string ResolveBootstrapPassword(string profile,string? existing=null)
    {
        if(profile=="test")return TestPassword;
        if(!string.IsNullOrWhiteSpace(existing))return existing;
        var supplied=Environment.GetEnvironmentVariable("MYSTICEGO_BOOTSTRAP_PASSPHRASE");
        if(string.IsNullOrWhiteSpace(supplied))
        {
            if(Console.IsInputRedirected)throw new InvalidOperationException("Production bootstrap requires an interactive secret phrase or MYSTICEGO_BOOTSTRAP_PASSPHRASE environment variable.");
            Console.Write("Секретная фраза владельца Orchestra (не отображается): ");
            var builder=new StringBuilder();
            ConsoleKeyInfo key;
            while((key=Console.ReadKey(intercept:true)).Key!=ConsoleKey.Enter)
            {
                if(key.Key==ConsoleKey.Backspace){if(builder.Length>0)builder.Length--;continue;}
                if(!char.IsControl(key.KeyChar))builder.Append(key.KeyChar);
            }
            Console.WriteLine();supplied=builder.ToString();
        }
        if(supplied.Length<16 || supplied.Length>256 || supplied.Any(char.IsControl))
            throw new ArgumentException("Production passphrase must be 16–256 non-control characters. Use a unique high-entropy phrase.");
        return supplied;
    }

    internal static void WriteSigningKeys(string profile,string privatePath,string publicPath)
    {
        if(File.Exists(privatePath)||File.Exists(publicPath))
        {
            if(!File.Exists(privatePath)||!File.Exists(publicPath))throw new InvalidOperationException("Incomplete Auth keypair, refusing replacement.");
            using var savedPrivate=RSA.Create();savedPrivate.ImportFromPem(File.ReadAllText(privatePath));
            using var savedPublic=RSA.Create();savedPublic.ImportFromPem(File.ReadAllText(publicPath));
            if(!CryptographicOperations.FixedTimeEquals(savedPrivate.ExportSubjectPublicKeyInfo(),savedPublic.ExportSubjectPublicKeyInfo()))
                throw new InvalidOperationException("Existing Auth RSA public and private keys do not match.");
            if(profile=="production" && TestKeyMatches(privatePath))throw new InvalidOperationException("Public test signing key detected in production.");
            if(profile=="test" && !TestKeyMatches(privatePath))throw new InvalidOperationException("Test installation must use the documented test signing key.");
            return;
        }
        if(profile=="test")
        {
            File.WriteAllText(privatePath,ReadResource("test-auth-private.pem"));
            File.WriteAllText(publicPath,ReadResource("test-auth-public.pem"));
        }
        else
        {
            using var rsa=RSA.Create(3072);
            File.WriteAllText(privatePath,rsa.ExportPkcs8PrivateKeyPem());
            File.WriteAllText(
                publicPath,
                rsa.ExportSubjectPublicKeyInfoPem());
        }
        Restrict(privatePath);Restrict(publicPath);
    }

    private static bool TestKeyMatches(string path)
    {
        using var test=RSA.Create();test.ImportFromPem(ReadResource("test-auth-private.pem"));
        using var actual=RSA.Create();actual.ImportFromPem(File.ReadAllText(path));
        return CryptographicOperations.FixedTimeEquals(test.ExportSubjectPublicKeyInfo(),actual.ExportSubjectPublicKeyInfo());
    }

    private static string ReadResource(string suffix)
    {
        var assembly=typeof(InstallerProfiles).Assembly;
        var name=assembly.GetManifestResourceNames().Single(n=>n.EndsWith(suffix,StringComparison.Ordinal));
        using var stream=assembly.GetManifestResourceStream(name)!;
        using var reader=new StreamReader(stream,Encoding.UTF8);
        return reader.ReadToEnd();
    }

    internal static void Restrict(string path)
    {
        if(!OperatingSystem.IsWindows())File.SetUnixFileMode(path,UnixFileMode.UserRead|UnixFileMode.UserWrite);
    }
}
