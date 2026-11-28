using Api.Auth.Controllers;
using Microsoft.Extensions.Configuration;

internal static class ExternalLoginHttpSmoke
{
    public static void Run()
    {
        var c = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
        {
            ["ExternalIdentity:Telegram:ClientId"] = "123",
            ["ExternalIdentity:Telegram:ClientSecret"] = "fixture",
            ["ExternalIdentity:Telegram:LoginRedirectUri"] = "https://example.invalid" + ExternalLoginController.CallbackPath
        }).Build();
        void Check(bool ok,string label){if(!ok)throw new Exception("External login HTTP: "+label);}
        Check(ExternalLoginController.ReadConfiguration(c) is null,"disabled default");
        c["ExternalIdentity:Telegram:LoginEnabled"]="true";
        Check(ExternalLoginController.ReadConfiguration(c)?.Provider=="telegram","valid login config");
        c["ExternalIdentity:Telegram:LoginRedirectUri"]="https://example.invalid/api/auth/external/telegram/callback";
        Check(ExternalLoginController.ReadConfiguration(c) is null,"link redirect cannot be used for sign-in");
        c["ExternalIdentity:Telegram:LoginRedirectUri"]="http://example.invalid"+ExternalLoginController.CallbackPath;
        Check(ExternalLoginController.ReadConfiguration(c) is null,"HTTPS");
        c["ExternalIdentity:Telegram:LoginRedirectUri"]="https://example.invalid"+ExternalLoginController.CallbackPath+"?next=evil";
        Check(ExternalLoginController.ReadConfiguration(c) is null,"query rejected");
        c["ExternalIdentity:Vk:ClientId"]="456";
        c["ExternalIdentity:Vk:LoginRedirectUri"]="https://example.invalid/api/auth/browser/external/vk/callback";
        Check(ExternalLoginController.ReadConfiguration(c,"vk") is null,"VK disabled default");
        c["ExternalIdentity:Vk:LoginEnabled"]="true";
        var vk=ExternalLoginController.ReadConfiguration(c,"vk");
        Check(vk?.Provider=="vk"&&vk.ClientId=="456","VK config independent");
        var url=Models.Autorization.Services.VkIdentityProof.AuthorizationUri("456",vk!.RedirectUri,
            new(new string('s',43),new string('c',43),new string('n',43)));
        Check(url?.Host=="id.vk.com"&&url.AbsolutePath=="/authorize"&&url.Query.Contains("code_challenge_method=s256"),"VK authorization");
        c["ExternalIdentity:Vk:LoginRedirectUri"]="https://example.invalid/api/auth/browser/external/telegram/callback";
        Check(ExternalLoginController.ReadConfiguration(c,"vk") is null,"cross-provider callback rejected");
        Check(ExternalLoginController.ReadConfiguration(c,"other") is null,"unknown provider");
        Console.WriteLine("External login HTTP configuration and callback isolation checks passed");
    }
}
