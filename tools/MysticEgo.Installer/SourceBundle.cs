using System.IO.Compression;

internal static class SourceBundle
{
    private static readonly HashSet<string> Trees=["core","services","shared","tools","frontend","desktop","tests"];
    private static readonly HashSet<string> Ignored=new(StringComparer.OrdinalIgnoreCase){"bin","obj","dist","node_modules","wwwroot","published","data","secrets","TestResults"};
    private static readonly HashSet<string> SourceExtensions=new(StringComparer.OrdinalIgnoreCase){".cs",".csproj",".ts",".tsx",".js",".mjs",".cjs",".css",".html"};
    private static readonly HashSet<string> Inputs=new(StringComparer.OrdinalIgnoreCase){"Dockerfile","package.json","package-lock.json","appsettings.json","tsconfig.json","tsconfig.base.json"};
    internal static void Pack(string root,string output)
    {
        using var archive=ZipFile.Open(output,ZipArchiveMode.Create);
        foreach(var name in new[]{"MysticEgo.Distributed.slnx",".dockerignore"})
            if(File.Exists(Path.Combine(root,name)))archive.CreateEntryFromFile(Path.Combine(root,name),name);
        foreach(var tree in Trees)
        {
            var dir=Path.Combine(root,tree);
            if(Directory.Exists(dir))Visit(dir);
        }
        void Visit(string dir)
        {
            foreach(var info in new DirectoryInfo(dir).EnumerateFileSystemInfos())
            {
                if(info.Attributes.HasFlag(FileAttributes.ReparsePoint)||info.Name.StartsWith('.')||Ignored.Contains(info.Name))continue;
                if(info is DirectoryInfo child){Visit(child.FullName);continue;}
                var rel=Path.GetRelativePath(root,info.FullName).Replace('\\','/');
                bool fixture=rel is "tools/MysticEgo.Installer/TestAssets/test-auth-private.pem" or "tools/MysticEgo.Installer/TestAssets/test-auth-public.pem";
                if(fixture||Inputs.Contains(info.Name)||SourceExtensions.Contains(info.Extension))
                    archive.CreateEntryFromFile(info.FullName,rel,CompressionLevel.Fastest);
            }
        }
    }
}
