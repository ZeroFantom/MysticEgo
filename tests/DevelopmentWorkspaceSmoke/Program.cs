using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using MysticEgo.Api.Orchestra.Development;

static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
var secret=new string('s',40);
var body=Encoding.UTF8.GetBytes("{\"repository\":{\"full_name\":\"ZeroFantom/MysticEgo\"}}");
var signature="sha256="+Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret),body)).ToLowerInvariant();
Check(WorkspaceWebhookSignature.Verify(secret,signature,body),"Valid webhook rejected");
Check(!WorkspaceWebhookSignature.Verify(secret,signature,Encoding.UTF8.GetBytes("{}")),"Modified payload accepted");
Check(!WorkspaceWebhookSignature.Verify("wrong",signature,body),"Wrong secret accepted");
Check(!WorkspaceWebhookSignature.Verify(secret,"sha256="+new string('x',64),body),"Malformed signature accepted");
Check(!WorkspaceWebhookSignature.Verify(secret,null,body),"Missing signature accepted");
var signal=new WorkspaceSyncSignal();
Check(!signal.Consume(),"Unexpected signal");signal.Request();signal.Request();
Check(signal.Consume()&&!signal.Consume(),"Webhook bursts not coalesced");

var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{
 ["DevelopmentGitHub:Token"]="test-only",["DevelopmentGitHub:AllowWrites"]="false"
}).Build();
var handler=new FakeHandler();
var client=new WorkspaceGitHubClient(new FakeFactory(handler),config);
await client.Rest(HttpMethod.Get,"issues/1/comments?per_page=100&page=1",null,default);
Check(handler.Calls==1&&handler.LastUri=="https://api.github.com/repos/ZeroFantom/MysticEgo/issues/1/comments?per_page=100&page=1","Fixed GitHub route not preserved");
try{await client.Rest(HttpMethod.Get,"../other",null,default);throw new Exception("Invalid route accepted");}catch(ArgumentException){}
try{await client.Rest(HttpMethod.Post,"issues",new{title="test"},default);throw new Exception("Write accepted without consent");}catch(InvalidOperationException){}
Check(handler.Calls==1,"Rejected request reached transport");
var pending=new WorkspaceItem{Id=Guid.NewGuid(),Title="Original title",Body="Original body",SyncState="publishing"};
var exact=pending.Body+"\n\n<!-- mysticego-workspace:"+pending.Id.ToString("D")+" -->";
Check(DevelopmentWorkspace.MatchesPublication(pending,pending.Title,exact),"Exact pending publication rejected");
Check(!DevelopmentWorkspace.MatchesPublication(pending,"Other title",exact),"Marker alone accepted");
Check(!DevelopmentWorkspace.MatchesPublication(pending,pending.Title,"Changed "+exact),"Changed body accepted");
pending.SyncState="local";
Check(!DevelopmentWorkspace.MatchesPublication(pending,pending.Title,exact),"Unpublished record matched");
Console.WriteLine("Workspace signature, coalescing, reconciliation matching and GitHub transport checks passed.");

sealed class FakeFactory(FakeHandler handler):IHttpClientFactory
{
 public HttpClient CreateClient(string name)=>new(handler,false);
}
sealed class FakeHandler:HttpMessageHandler
{
 public int Calls{get;private set;}
 public string? LastUri{get;private set;}
 protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
 {
  Calls++;LastUri=request.RequestUri?.AbsoluteUri;
  return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("[]")});
 }
}
