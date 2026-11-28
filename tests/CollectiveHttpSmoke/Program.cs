using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using MysticEgo.Distributed;
using MysticEgo.Soul;
using MysticEgo.Api.Orchestra.Controllers;
using Models.Data;
using MysticEgo.Api.Orchestra;
using MysticEgo.WorldAI;
using Microsoft.Data.Sqlite;

var tmp=Directory.CreateTempSubdirectory("collective-http-").FullName;
void Check(bool ok,string message){if(!ok)throw new Exception(message);}
var key=Encoding.UTF8.GetBytes("isolated-collective-test-secret-32-bytes");
var builder=WebApplication.CreateBuilder();
builder.WebHost.UseUrls("http://127.0.0.1:0");
builder.Configuration["Soul:StoragePath"]=Path.Combine(tmp,"souls");
builder.Services.AddSingleton(new NodeOptions{NodeId="test",DataPath=Path.Combine(tmp,"node.db")});
builder.Services.AddSingleton<NodeState>();builder.Services.AddSocial();builder.Services.AddSoul();
builder.Services.AddHttpClient();builder.Services.AddScoped<CollectiveVoice>();
builder.Configuration["WorldAI:StoragePath"] = Path.Combine(tmp, "world-ai");
builder.Services.AddSingleton<WorldAiJobQueue>();
builder.Services.AddSingleton<WorldAiCluster>();
builder.Services.AddSingleton<DevelopmentTeam>();
var provider = new QuestFixtureProvider();
builder.Services.AddHttpClient("world-ai").ConfigurePrimaryHttpMessageHandler(() => provider);
builder.Services.AddControllers().AddApplicationPart(typeof(CollectiveController).Assembly)
    .AddApplicationPart(typeof(WorldAiController).Assembly);
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o=>{
    o.TokenValidationParameters=new(){ValidateIssuer=true,ValidIssuer="test",ValidateAudience=true,ValidAudience="test",
        ValidateIssuerSigningKey=true,IssuerSigningKey=new SymmetricSecurityKey(key),ValidateLifetime=true};
});
builder.Services.AddAuthorization(o=>o.AddPolicy("operator",p=>p.RequireAuthenticatedUser().RequireAssertion(c=>!c.User.HasClaim("token_use","device"))));
await using var app=builder.Build();
app.UseAuthentication();app.UseAuthorization();app.MapControllers();
try{
 await app.StartAsync();
 var origin=app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
 using var http=new HttpClient{BaseAddress=new Uri(origin)};
 var actor=Guid.NewGuid();var other=Guid.NewGuid();
 string Token(string id, string role = "audit", string kind = "account")=>new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken("test","test",
    [new Claim(ClaimTypes.NameIdentifier,id),new Claim(ClaimTypes.Name,"smoke-user"), new Claim(ClaimTypes.Role,role), new Claim("token_use",kind)],expires:DateTime.UtcNow.AddMinutes(5),
    signingCredentials:new SigningCredentials(new SymmetricSecurityKey(key),SecurityAlgorithms.HmacSha256)));
 http.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",Token(actor.ToString()));
 const string route="/api/v1/collective/";
 async Task<JsonElement> Read(string path){using var r=await http.GetAsync(route+path);var body=await r.Content.ReadAsStringAsync();Check(r.IsSuccessStatusCode,path+" "+r.StatusCode+" "+body);return JsonDocument.Parse(body).RootElement.Clone();}
 var me=await Read("me");
 Check(me.GetProperty("profile").GetProperty("id").GetGuid()==actor,"profile identity");
 Check(me.GetProperty("game").GetProperty("level").GetInt32()==1,"real game progress");
 var store=app.Services.GetRequiredService<NodeState>().Data;
 await store.PutAsync(new SocialNotification{Id=Guid.NewGuid(),Recipient=actor.ToString(),Text="mine"});
 await store.PutAsync(new SocialNotification{Id=Guid.NewGuid(),Recipient=other.ToString(),Text="private"});
 var notices=await Read("notifications");
 Check(notices.GetArrayLength()==1&&notices[0].GetProperty("text").GetString()=="mine","notification isolation");
 // Existing schemas must be readable without any metadata write.
 var readOnly=new DynamicEntityStore(new SqliteConnectionStringBuilder{DataSource=Path.Combine(tmp,"node.db"),Mode=SqliteOpenMode.ReadOnly}.ConnectionString);
 Check((await readOnly.GetAsync<SocialProfile>(actor))?.Id==actor,"read-only existing profile");
 Check((await readOnly.ListAsync<SocialNotification>()).Count==2,"read-only existing notifications");
 var file=Path.Combine(tmp,"souls",Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(actor.ToString()))).ToLowerInvariant()+".json");
 var legacy=JsonSerializer.Serialize(new{userId=actor.ToString(),soulId="legacy",created=DateTimeOffset.UtcNow,state="EgoShard"});
 await File.WriteAllTextAsync(file,legacy);
 me=await Read("me");Check(me.GetProperty("game").GetProperty("level").GetInt32()==1,"legacy missing collections");
 await File.WriteAllTextAsync(file,"{damaged");
 using(var failed=await http.GetAsync(route+"me"))Check((int)failed.StatusCode==503,"storage fault must be traceable");
 Check(await File.ReadAllTextAsync(file)=="{damaged","damaged data must never be overwritten");
 Check((await Read("profile")).GetProperty("id").GetGuid()==actor,"basic profile independent from game file");
 http.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",Token("not-a-guid"));
 using(var invalid=await http.GetAsync(route+"notifications"))Check((int)invalid.StatusCode==401,"invalid account identity must not produce 500");
 builder.Configuration["DevelopmentTeam:OwnerId"] = actor.ToString();
 await QuestProposalSmoke.Run(http, app.Services, provider, actor, other, Token);
 Console.WriteLine("Collective HTTP, notifications isolation, read-only schemas and legacy profile recovery passed");
}finally{await app.StopAsync();SqliteConnection.ClearAllPools();Directory.Delete(tmp,true);}
