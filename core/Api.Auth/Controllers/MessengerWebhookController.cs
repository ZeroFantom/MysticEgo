using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Models.Autorization.Attributes;
using Models.Autorization.Services;

namespace Api.Auth.Controllers;

/// <summary>Disabled-by-default ingress. Provider acknowledgement is not world delivery.
/// No raw update, code, identity or credentials appear in the HTTP response.</summary>
[ApiController, AllowAnonymous, SkipPermission, Route("api/auth/messenger/webhook")]
[EnableRateLimiting("messenger-webhook")]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class MessengerWebhookController(IConfiguration configuration, MessengerIngress ingress, MessengerOutbound outbound, MessengerIdentityService identities, TimeProvider clock) : ControllerBase
{
    private (MessengerRealm Realm,string Secret)? Settings(string provider) => MessengerConfiguration.Read(configuration,provider);
    private async Task Process(MessengerRealm realm,PrivateMessengerEvent message,CancellationToken ct)
    {
        var result=await ingress.Process(realm,message,ct);
        var token=MessengerConfiguration.ReplyToken(configuration,realm);
        if(token is null||MessengerOutbound.ReplyText(result) is null)return;
        // Re-read live binding before reporting linked status. No personal data in the reply.
        if(result is MessengerIngressStatus.Linked or MessengerIngressStatus.Unlinked)
            result=await identities.Resolve(realm,message.Sender,ct) is null?MessengerIngressStatus.Unlinked:MessengerIngressStatus.Linked;
        // Receipt committed before network I/O. Duplicate callbacks never cause another send.
        // A timeout is ambiguous; intentionally do not retry here.
        await outbound.Reply(realm,message,result,token,ct);
    }

    [HttpPost("telegram"), RequestSizeLimit(65536)]
    public async Task<IActionResult> Telegram([FromBody] JsonElement update,CancellationToken ct)
    {
        var config=Settings("telegram");
        if(config is null)return NotFound();
        var header=Request.Headers["X-Telegram-Bot-Api-Secret-Token"];
        if(header.Count!=1||!MessengerWebhookProof.SecretMatches(config.Value.Secret,header.ToString()))return Unauthorized();
        var message=MessengerWebhookProof.Telegram(config.Value.Realm,config.Value.Secret,header.ToString(),update,clock.GetUtcNow());
        if(message is not null)await Process(config.Value.Realm,message,ct);
        return Ok();
    }

    [HttpPost("vk"), RequestSizeLimit(65536)]
    public async Task<IActionResult> Vk([FromBody] JsonElement update,CancellationToken ct)
    {
        var config=Settings("vk");
        if(config is null)return NotFound();
        if(!MessengerWebhookProof.VkEnvelope(config.Value.Realm,config.Value.Secret,update))return Unauthorized();
        if(update.TryGetProperty("type",out var kind)&&kind.ValueKind==JsonValueKind.String&&kind.GetString()=="confirmation")
        {
            var confirmation=configuration["Messenger:Vk:Confirmation"];
            return string.IsNullOrEmpty(confirmation)||confirmation.Length>256||confirmation.Any(char.IsControl)
                ?NotFound():Content(confirmation,"text/plain");
        }
        var message=MessengerWebhookProof.Vk(config.Value.Realm,config.Value.Secret,update,clock.GetUtcNow());
        if(message is not null)await Process(config.Value.Realm,message,ct);
        return Content("ok","text/plain");
    }
}
