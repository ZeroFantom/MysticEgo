// Static architecture contracts until full integration testing is available.
using System;
using System.IO;
using System.Linq;
var root = Directory.GetCurrentDirectory();
while (!File.Exists(Path.Combine(root, "MysticEgo.Distributed.slnx")))
{
    var parent = Directory.GetParent(root)?.FullName ?? throw new InvalidOperationException("Project root not found");
    root = parent;
}
string Read(string rel) => File.ReadAllText(Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar)));
void Check(bool condition, string message) { if (!condition) throw new Exception(message); Console.WriteLine("PASS " + message); }
foreach (var host in new[] { "Agent", "AuditOrchestra", "Global", "Local", "Orchestra" })
{
    var program = Read($"services/Api.{host}/Program.cs");
    Check(program.Contains("AddControllers(") && program.Contains("MapControllers("), $"MVC enabled: {host}");
}
foreach (var file in new[] { "LmStudioBridge.cs", "NetworkRepair.cs", "Endpoints.cs", "VendorInstaller.cs", "LangamePlugin.cs", "AgentUpdate.cs", "SetupOnboarding.cs", "ProfilesSocial.cs", "VpnManagement.cs" })
    Check(!Read("services/MysticEgo.Distributed/" + file).Contains(".MapPost("), $"No HTTP endpoints in {file}");
Check(!Read("services/MysticEgo.Soul/SoulEngine.cs").Contains(".MapPost("), "Soul service not an endpoint map");
Check(!Read("services/MysticEgo.Avatar/AvatarEndpoints.cs").Contains(".MapPost("), "Avatar service not an endpoint map");
foreach (var name in new[] { "Agent", "AuditOrchestra" })
{
    var controller = Read($"services/Api.{name}/Controllers/AssistantController.cs");
    Check(controller.Contains("[HttpPost(\"chat\")]") && controller.Contains("[HttpPost(\"proposals/{id:guid}/confirm\")]") && controller.Contains("[Authorize]"), $"Assistant routes retained: {name}");
}
Check(Read("services/Api.Orchestra/Program.cs").Contains("AddApplicationPart"), "WorldAI controller application part");
Check(Read("services/MysticEgo.Distributed/Bootstrap.cs").Contains("AddNodeApiServices"), "Node services registered");
Check(Read("services/MysticEgo.Distributed/NodeControllers.cs").Contains("class NodeCommandsController"), "Core node controllers present");
Console.WriteLine("API structural smoke passed (does not replace compilation/e2e)");

foreach(var f in Directory.GetFiles(Path.Combine(root,"services"),"*.cs",SearchOption.AllDirectories))
{
    var body=File.ReadAllText(f);
    Check(!System.Text.RegularExpressions.Regex.IsMatch(body,@"\.Map(Get|Post|Put|Delete|Patch)\s*\("),"Attribute MVC routes: "+Path.GetRelativePath(root,f));
}
