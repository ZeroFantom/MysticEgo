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

var queue = new MysticEgo.Distributed.AvatarHandoffQueue();
Check(queue.Request("owner", new("shell", .5)) is null, "Reject unknown avatar motion");
Check(queue.Request("owner", new("walk", double.NaN)) is null, "Reject invalid position");
Check(queue.Request("owner", new("walk", .5, double.NaN)) is null, "Reject invalid motion phase");
Check(queue.Request("owner", new("walk", .5, 0, "unknown")) is null, "Reject unknown visual mood");
var handoff = queue.Request("owner", new("walk", .5, 1.25, "happy")) ?? throw new Exception("Handoff not created");
Check(queue.Status("other", handoff.Id) is null && queue.Claim("other") is null, "Handoff owner isolation");
Check(queue.Request("owner", new("perch", .3)) is null, "Single pending handoff");
var claim = queue.Claim("owner") ?? throw new Exception("No claim");
Check(claim.Request.PhaseSeconds == 1.25 && claim.Request.Mood == "happy", "Handoff preserves motion phase and visual mood");
Check(queue.Claim("owner") is null, "No duplicate claim");
Check(!queue.Complete("owner", handoff.Id, new("wrong", true)), "Reject invalid claim proof");
Check(!queue.Complete("other", handoff.Id, new(claim.Proof, true)), "Reject wrong owner completion");
Check(queue.Complete("owner", handoff.Id, new(claim.Proof, true)), "Accept valid handoff completion");
Check(queue.Status("owner", handoff.Id)?.Status == "started", "Started state visible to owner");
Check(!queue.Complete("owner", handoff.Id, new(claim.Proof, true)), "No replay of completion");
