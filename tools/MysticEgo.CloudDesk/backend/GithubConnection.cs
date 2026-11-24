using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Security.Cryptography;

sealed partial class CloudDesk {
    // The CLI is used for local credentials and release download only. Never print the token.
    readonly HttpClient githubHttp = new(new SocketsHttpHandler {
        AllowAutoRedirect=false, ConnectTimeout=TimeSpan.FromSeconds(7),
        PooledConnectionLifetime=TimeSpan.FromMinutes(2)
    }) { Timeout=TimeSpan.FromSeconds(10) };
    string githubCredentialFingerprint="";
    JsonObject? githubUserCache, githubRepoCache;
    long githubVerifiedAt;
    sealed class GithubFailure(string kind, string message, int retryMs=0) : Exception(message) {
        public string Kind {get;}=kind;
        public int RetryMs {get;}=retryMs;
    }
    static string GithubHttpKind(int code, bool limited) => code switch {
        401=>"unauthorized", 429=>"rate-limit", 403 when limited=>"rate-limit",
        403=>"forbidden", 404=>"not-found", 408 or >=500 and <=599=>"temporary", _=>"http"
    };
    static string GithubHttpMessage(int code,string kind) => kind switch {
        "unauthorized"=>"GitHub отклонил учётные данные (HTTP 401). Требуется повторный вход.",
        "rate-limit"=>"GitHub ограничил частоту запросов. Автоматический опрос приостановлен.",
        "forbidden"=>"GitHub HTTP 403: нет доступа или действует ограничение аккаунта.",
        "not-found"=>"GitHub HTTP 404: ресурс не найден или закрыт для аккаунта.",
        _=>$"GitHub HTTP {code}."
    };
    async Task<string> GithubLocalToken() {
        if(File.Exists(GitDisabled)) throw new GithubFailure("disabled","GitHub отключён в CloudDesk.");
        try {
            var secret=await Exec("gh",["auth","token","--hostname","github.com"],sensitive:true,timeoutSeconds:8);
            if(string.IsNullOrWhiteSpace(secret)||secret.Any(char.IsWhiteSpace)) throw new Exception();
            return secret;
        } catch { throw new GithubFailure("local-auth","Локальные данные GitHub CLI недоступны. Проверьте вход gh и системное хранилище."); }
    }
    async Task<JsonObject> GithubGet(string path,string secret) {
        for(int attempt=0;;attempt++) {
            try {
                using var request=new HttpRequestMessage(HttpMethod.Get,"https://api.github.com/"+path);
                request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",secret);
                request.Headers.UserAgent.ParseAdd("MysticEgo-CloudDesk/0.4.7");
                request.Headers.Accept.ParseAdd("application/vnd.github+json");
                request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version","2022-11-28");
                using var response=await githubHttp.SendAsync(request);
                int code=(int)response.StatusCode;
                if(!response.IsSuccessStatusCode) {
                    bool limited=response.Headers.TryGetValues("X-RateLimit-Remaining",out var remaining)&&remaining.Contains("0")||response.Headers.RetryAfter!=null;
                    string kind=GithubHttpKind(code,limited);
                    int wait=120000;
                    if(kind=="rate-limit") {
                        double? seconds=response.Headers.RetryAfter?.Delta?.TotalSeconds;
                        if(seconds==null&&response.Headers.RetryAfter?.Date is DateTimeOffset date) seconds=(date-DateTimeOffset.UtcNow).TotalSeconds;
                        if(response.Headers.TryGetValues("X-RateLimit-Reset",out var reset)&&long.TryParse(reset.FirstOrDefault(),out var unix)) seconds=Math.Max(seconds??0,unix-DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                        wait=(int)Math.Clamp((seconds??120)*1000,60000,3600000);
                    }
                    throw new GithubFailure(kind,GithubHttpMessage(code,kind),kind=="rate-limit"?wait:0);
                }
                return JsonNode.Parse(await response.Content.ReadAsStringAsync()) as JsonObject??throw new GithubFailure("http","GitHub вернул некорректный JSON.");
            } catch(GithubFailure e) when(e.Kind=="temporary"&&attempt==0) { await Task.Delay(900); }
            catch(Exception e) when(e is HttpRequestException or OperationCanceledException) {
                if(attempt==0) { await Task.Delay(900); continue; }
                throw new GithubFailure("temporary","GitHub временно недоступен: тайм-аут сети, DNS, TLS или прокси. Сессия сохранена; повтор автоматически.");
            }
        }
    }
    static JsonObject GithubRepoView(JsonObject raw)=>new() {
        ["nameWithOwner"]=raw["full_name"]?.DeepClone(),
        ["visibility"]=raw["visibility"]?.ToString().ToUpperInvariant()??"UNKNOWN",
        ["defaultBranchRef"]=new JsonObject{["name"]=raw["default_branch"]?.DeepClone()}
    };
    static bool GithubPrivate(JsonObject view)=>S(view,"nameWithOwner").Equals(Repo,StringComparison.OrdinalIgnoreCase)&&S(view,"visibility")=="PRIVATE";
    void ClearGithubCache() { githubUserCache=null;githubRepoCache=null;githubVerifiedAt=0; }
    async Task<object> GitProfile() {
        string secret;
        try { secret=await GithubLocalToken(); }
        catch(GithubFailure e) { ClearGithubCache();githubCredentialFingerprint="";return new {connected=false,online=false,repoReady=false,warning=e.Message}; }
        var fingerprint=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
        if(fingerprint!=githubCredentialFingerprint){ClearGithubCache();githubCredentialFingerprint=fingerprint;}
        bool userOk=false;
        try {
            var user=await GithubGet("user",secret);
            if(string.IsNullOrWhiteSpace(S(user,"login"))) throw new GithubFailure("http","GitHub вернул профиль без логина.");
            githubUserCache=Pick(user,"login","name","id","public_repos","followers","created_at");
            userOk=true;
            githubRepoCache=GithubRepoView(await GithubGet("repos/"+Repo,secret));
            githubVerifiedAt=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            bool valid=GithubPrivate(githubRepoCache);
            return new {connected=true,online=true,repoReady=valid,profile=githubUserCache,repo=githubRepoCache,verifiedAt=githubVerifiedAt,warning=valid?"":"Ожидался доступ к приватному репозиторию "+Repo};
        } catch(GithubFailure e) {
            if(e.Kind=="unauthorized") { ClearGithubCache();return new {connected=false,online=true,repoReady=false,warning=e.Message}; }
            if(e.Kind is "forbidden" or "not-found") { githubRepoCache=null;githubVerifiedAt=0;if(!userOk) githubUserCache=null; }
            return new {connected=true,online=e.Kind is not ("temporary" or "rate-limit"),repoReady=false,profile=githubUserCache,repo=githubRepoCache,verifiedAt=githubVerifiedAt,warning=e.Message,retryAfterMs=e.RetryMs>0?e.RetryMs:30000};
        } catch(System.Text.Json.JsonException) {
            return new {connected=true,online=false,repoReady=false,profile=githubUserCache,repo=githubRepoCache,verifiedAt=githubVerifiedAt,warning="GitHub вернул некорректный ответ. Повтор автоматически.",retryAfterMs=30000};
        }
    }
    async Task GitCheck() {
        var secret=await GithubLocalToken();
        var repo=GithubRepoView(await GithubGet("repos/"+Repo,secret));
        if(!GithubPrivate(repo)) throw new Exception("Ожидался приватный репозиторий "+Repo);
    }
    async Task<string> GithubReleaseDigest() {
        var secret=await GithubLocalToken();
        var repository=GithubRepoView(await GithubGet("repos/"+Repo,secret));
        if(!GithubPrivate(repository)) throw new Exception("Ожидался приватный репозиторий "+Repo);
        var release=await GithubGet("repos/"+Repo+"/releases/tags/"+Tag,secret);
        var file=(release["assets"] as JsonArray)?.OfType<JsonObject>().SingleOrDefault(item=>S(item,"name")==Asset);
        var digest=file is null ? "" : S(file,"digest");
        if(file is null || S(file,"state")!="uploaded" || !System.Text.RegularExpressions.Regex.IsMatch(digest,@"^sha256:[a-f0-9]{64}$"))
            throw new Exception("В релизе "+Tag+" нет готового ZIP с SHA-256.");
        return digest["sha256:".Length..];
    }
    static object GithubSelftest() {
        int passed=0;void Test(bool ok){if(!ok)throw new Exception("GitHub selftest #"+(passed+1));passed++;}
        Test(GithubHttpKind(401,false)=="unauthorized");Test(GithubHttpKind(403,false)=="forbidden");
        Test(GithubHttpKind(403,true)=="rate-limit");Test(GithubHttpKind(429,false)=="rate-limit");
        Test(GithubHttpKind(404,false)=="not-found");Test(GithubHttpKind(503,false)=="temporary");
        var repo=GithubRepoView(new JsonObject{["full_name"]=Repo,["visibility"]="private",["default_branch"]="main"});
        Test(GithubPrivate(repo)&&repo["defaultBranchRef"]?["name"]?.ToString()=="main");
        repo["visibility"]="PUBLIC";Test(!GithubPrivate(repo));
        return new {passed};
    }
}
