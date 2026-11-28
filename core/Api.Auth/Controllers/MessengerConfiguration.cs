using Models.Autorization.Services;

namespace Api.Auth.Controllers;

/// <summary>Only server-owned configuration may select a bot and its credentials.</summary>
internal static class MessengerConfiguration
{
    internal static string Prefix(string provider) => provider == "telegram" ? "Messenger:Telegram:" : "Messenger:Vk:";
    internal static (MessengerRealm Realm,string Secret)? Read(IConfiguration config,string provider)
    {
        if(provider is not ("telegram" or "vk"))return null;
        var prefix=Prefix(provider);
        if(!config.GetValue<bool>(prefix+"Enabled"))return null;
        var realm=new MessengerRealm(provider,config[prefix+"BotId"]??"");
        var secret=config[prefix+"WebhookSecret"];
        return realm.Valid&&MessengerWebhookProof.ValidSecret(secret)?(realm,secret!):null;
    }
    internal static string? ReplyToken(IConfiguration config,MessengerRealm realm)
    {
        var current=Read(config,realm.Provider);
        var prefix=Prefix(realm.Provider);
        if(current is null||current.Value.Realm!=realm||!config.GetValue<bool>(prefix+"ReplyEnabled"))return null;
        return config[prefix+"AccessToken"];
    }
}
