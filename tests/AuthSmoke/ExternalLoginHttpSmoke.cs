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
        Console.WriteLine("External login HTTP configuration and callback isolation checks passed");
    }
}
