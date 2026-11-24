using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

sealed class TimewebApiException(string method,int version,string path,int status,string code,string detail) : Exception(detail) {
    public string Method { get; }=method;
    public int Version { get; }=version;
    public string Path { get; }=path;
    public int Status { get; }=status;
    public string ErrorCode { get; }=code;
}
sealed partial class CloudDesk {
    static string TimewebErrorCode(string raw) {
        try { if(raw.Length<=65536 && JsonNode.Parse(raw) is JsonObject obj && obj["error_code"] is JsonValue val && val.TryGetValue<string>(out var code)) return code; }
        catch(System.Text.Json.JsonException) { }
        return "";
    }
    // Only documented error fields are surfaced; never print headers or raw request/response bodies.
    static string TimewebError(string method,int version,string path,int status,string raw,string credential) {
        string Clean(string value) {
            if(credential.Length>0) value=value.Replace(credential,"[REDACTED]",StringComparison.Ordinal);
            value=Regex.Replace(value,@"(?i)\bBearer\s+\S+","Bearer [REDACTED]");
            value=Regex.Replace(value,@"\beyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\b","[REDACTED]");
            // Omit messages that appear to contain secret values or private key material.
            if(Regex.IsMatch(value,@"(?i)password|passwd|secret|private[ _-]?key|authorization|credential|token|парол|секрет|приватн.*ключ|токен")) return "[сообщение о конфиденциальном поле скрыто]";
            value=Regex.Replace(value,@"[\p{Cc}\p{Cf}]"," ").Trim();
            return value.Length>500?value[..500]+"…":value;
        }
        var parts=new List<string>{$"Timeweb HTTP {status} · {method} /api/v{version}/{path}"};
        try {
            if(raw.Length<=65536 && JsonNode.Parse(raw) is JsonObject obj) {
                if(obj["error_code"] is JsonValue code && code.TryGetValue<string>(out var c)&&Regex.IsMatch(c,@"^[a-zA-Z0-9_.-]{1,100}$")&&!c.Contains(credential.Length==0?"\0":credential,StringComparison.Ordinal)) parts.Add("error_code: "+c);
                var messages=obj["message"] is JsonArray arr ? arr.Take(8).ToArray() : new[]{obj["message"]};
                foreach(var m in messages) if(m is JsonValue v && v.TryGetValue<string>(out var t) && !string.IsNullOrWhiteSpace(t)) parts.Add("Причина: "+Clean(t));
                if(obj["response_id"] is JsonValue rid && rid.TryGetValue<string>(out var id)&&Guid.TryParse(id,out var guid)) parts.Add("response_id: "+guid);
            }
        } catch(System.Text.Json.JsonException) { }
        if(parts.Count==1) parts.Add("API не вернуло распознаваемое описание ошибки.");
        if(status==401) parts.Add("Авторизация недействительна: подключите Timeweb заново.");
        if(status==403) parts.Add("Проверьте разрешения токена для указанной операции.");
        if(status==400 && TimewebErrorCode(raw)!="servers_is_run_out_error") parts.Add("Проверьте параметры указанного запроса; это не обязательно ошибка авторизации.");
        var codeValue=TimewebErrorCode(raw);
        if(IsKnownOrderRejection(new TimewebApiException(method,version,path,status,codeValue,"")))
            parts.Add("Timeweb отклонил заказ. Требуется новый выбор и подтверждение.");
        else if(method=="DELETE") parts.Add("Перед повтором удаления проверьте состояние записи.");
        else if(method!="GET") parts.Add("Перед повтором проверьте состояние ресурса. Автоматического повторного заказа нет.");
        var result=string.Join(" · ",parts);
        return credential.Length>0?result.Replace(credential,"[REDACTED]",StringComparison.Ordinal):result;
    }
    static object TimewebErrorSelftest() {
        int passed=0;
        void Test(bool condition){if(!condition)throw new Exception("Timeweb diagnostic selftest failed");passed++;}
        var id="94608d15-8672-4eed-8ab6-28bd6fa3cdf7";
        var e=TimewebError("POST",1,"servers",400,"{\"error_code\":\"bad_request\",\"message\":[\"Invalid preset_id\",\"Invalid os_id\"],\"response_id\":\""+id+"\"}","test-credential");
        Test(e.Contains("POST /api/v1/servers")&&e.Contains("bad_request")&&e.Contains("Invalid preset_id")&&e.Contains("Invalid os_id")&&e.Contains(id));
        Test(!TimewebError("GET",1,"servers",401,"{\"message\":\"test-credential\"}","test-credential").Contains("test-credential"));
        Test(!TimewebError("GET",1,"servers",400,"{\"message\":\"Bearer eyJabc.def.ghi\"}","").Contains("eyJabc"));
        Test(!TimewebError("GET",1,"servers",400,"{\"message\":\"password: secret-value\",\"password\":\"secret-value\"}","").Contains("secret-value"));
        Test(TimewebError("GET",2,"domains/example.org/dns-records",400,"<html>secret-value</html>","").Contains("не вернуло распознаваемое"));
        Test(TimewebError("POST",1,"servers",409,"{\"error_code\":\"location_not_available\"}","").Contains("Timeweb отклонил заказ"));
        return new{passed};
    }
}
