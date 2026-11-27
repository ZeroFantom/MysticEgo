using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using MysticEgo.Api.Orchestra;
using MysticEgo.Api.Orchestra.Development;
using MysticEgo.Distributed;

static void Check(bool result,string message){if(!result)throw new Exception(message);}
var directory=Path.Combine(Path.GetTempPath(),"mysticego-workspace-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
try
{
    var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
    {
        ["DevelopmentWorkspace:WriteToken"]="test-token-not-real"
    }).Build();
    var state=new NodeState(new NodeOptions{NodeId="workspace-smoke",DataPath=Path.Combine(directory,"node.db")});
    var handler=new FakeGitHub();
    var factory=new FakeFactory(handler);
    var panel=new GitHubPanel(factory,config,state);
    var transport=new GitHubWorkspaceClient(factory,config,panel);
    var github=new GitHubWorkspace(transport,config,state);
    var page=await github.List("issue",1,null,default);
    Check(page.Items.Length==1&&page.Items[0].Number==1,"Pull requests leaked into issue list");
    var again=await github.List("issue",1,null,default);
    Check(handler.Calls==1&&again.FetchedAt==page.FetchedAt,"Shared read cache not used");
    var actor=Guid.NewGuid();
    var local=new InternalWorkspace(state,github);
    var item=await local.Create(new("issue","Private task","Private description",null),actor,"developer",default);
    Check(handler.Calls==1,"Private creation contacted GitHub");
    var claimed=await local.Change(item.Id,new("claim"),actor,"developer",false,default);
    Check(claimed.AssigneeId==actor,"Claim missing");
    try{await local.Change(item.Id,new("claim"),Guid.NewGuid(),"other",false,default);throw new Exception("Second actor claimed task");}
    catch(WorkspaceProviderException e) when(e.Status==409){}
    Check((await local.List(default)).Any(x=>x.Id==item.Id),"Internal task not persisted");
    var published=await local.Publish(item.Id,null,actor,"developer",false,default);
    Check(published.Publication=="published"&&published.GitHubNumber==3,"Publication mapping missing");
    Check(handler.LastBody.Contains("mysticego-workspace:"+item.Id.ToString("D")),"Recovery marker missing");
    try{await local.Publish(item.Id,null,actor,"developer",false,default);throw new Exception("Duplicate publication");}
    catch(WorkspaceProviderException e) when(e.Status==409){}
    try{await transport.Rest(HttpMethod.Get,"../../user",null,default);throw new Exception("Arbitrary route accepted");}
    catch(ArgumentException){}
    try{await github.Change("issue",2,new("close"),actor,"developer",true,default);throw new Exception("PR modified through issue route");}
    catch(WorkspaceProviderException e) when(e.Status==404){}
    Console.WriteLine("Developer workspace cache, local claim, publication and route checks passed.");
}
finally{try{Directory.Delete(directory,true);}catch(IOException){}}
sealed class FakeFactory(FakeGitHub handler):IHttpClientFactory
{
    public HttpClient CreateClient(string name)=>new(handler,false);
}
sealed class FakeGitHub:HttpMessageHandler
{
    public int Calls;
    public string LastBody="";
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
    {
        Calls++;
        if(request.RequestUri?.Host!="api.github.com")throw new Exception("Unexpected GitHub host");
        if(request.Headers.Authorization?.Parameter!="test-token-not-real")throw new Exception("Missing test credential");
        string body;
        if(request.Method==HttpMethod.Post)
        {
            LastBody=await request.Content!.ReadAsStringAsync(ct);
            body="""{"number":3,"title":"Private task","body":"Private description","state":"open","user":{"login":"bot"},"assignees":[],"labels":[],"comments":0}""";
        }
        else if(request.RequestUri.AbsolutePath.EndsWith("/2"))
            body="""{"number":2,"title":"PR","pull_request":{}}""";
        else body="""[{"number":1,"title":"Task","state":"open","assignees":[],"labels":[]},{"number":2,"title":"PR","pull_request":{}}]""";
        return new(HttpStatusCode.OK){Content=new StringContent(body,Encoding.UTF8,"application/json")};
    }
}
