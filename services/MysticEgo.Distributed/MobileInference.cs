using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using System.IO;

namespace MysticEgo.Distributed;

// Explicit server-side remote inference. No forwarding of account tokens or device tools.
internal static class MobileInference
{
    private static readonly HttpClient Http=new(new SocketsHttpHandler{AllowAutoRedirect=false,UseProxy=false}){Timeout=TimeSpan.FromSeconds(60),MaxResponseContentBufferSize=1024*1024};
    private static readonly ConcurrentDictionary<Guid,byte> Active=new();
    private static readonly SemaphoreSlim Slots=new(4);
    private static readonly object BudgetGate=new();
    private static DateTimeOffset Window=DateTimeOffset.UtcNow;
    private static int Used;
    private static bool Budget()
    {
        lock(BudgetGate){var now=DateTimeOffset.UtcNow;if(now-Window>=TimeSpan.FromMinutes(1)){Window=now;Used=0;}return Used<30&&++Used<=30;}
    }
    private static Uri Endpoint(IConfiguration config,string prefix,bool loopback=false)
    {
        if(!Uri.TryCreate(config[prefix+":Endpoint"],UriKind.Absolute,out var uri)||uri.UserInfo.Length>0||uri.Fragment.Length>0||uri.Query.Length>0||
            !(uri.Scheme=="https"||loopback&&uri.Scheme=="http"&&uri.IsLoopback))throw new AssistantApiException(503,"Провайдер не настроен.");
        return uri;
    }
    private static void Key(HttpRequestMessage request,IConfiguration config,string prefix)
    {
        var key=config[prefix+":ApiKey"];if(!string.IsNullOrWhiteSpace(key))request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",key);
    }
    private static async Task<string> Run(Guid account,Func<CancellationToken,Task<string>> action,CancellationToken ct)
    {
        if(!Active.TryAdd(account,0))throw new AssistantApiException(409,"Дождитесь предыдущего запроса.");
        bool acquired=false;
        try{
            acquired=await Slots.WaitAsync(0,ct);
            if(!acquired||!Budget())throw new AssistantApiException(429,"Лимит запросов сервера. Повторите позднее.");
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromSeconds(55));
            return await action(timeout.Token);
        }catch(Exception error) when(error is HttpRequestException or IOException or JsonException or InvalidOperationException or KeyNotFoundException or IndexOutOfRangeException||error is OperationCanceledException&&!ct.IsCancellationRequested)
        {throw new AssistantApiException(502,"Провайдер не вернул ответ.");}
        finally{if(acquired)Slots.Release();Active.TryRemove(account,out _);}
    }
    public static bool ChatConfigured(IConfiguration config)=>Uri.TryCreate(config["MobileCloud:Endpoint"],UriKind.Absolute,out var uri)&&uri.Scheme=="https"&&!string.IsNullOrWhiteSpace(config["MobileCloud:Model"]);
    public static bool SpeechConfigured(IConfiguration config)=>!string.IsNullOrWhiteSpace(config["VoiceTranscription:Endpoint"]);
    public static Task<string> Chat(Guid account,string text,IConfiguration config,CancellationToken ct)=>Run(account,async token=>{
        var endpoint=Endpoint(config,"MobileCloud");var model=config["MobileCloud:Model"];
        if(string.IsNullOrWhiteSpace(model))throw new AssistantApiException(503,"Облачная модель не настроена.");
        using var request=new HttpRequestMessage(HttpMethod.Post,endpoint);
        Key(request,config,"MobileCloud");
        request.Content=JsonContent.Create(new{model,stream=false,max_tokens=720,messages=new[]{new{role="system",content="Ты помощник MysticEgo. Отвечай текстом. Не утверждай, что выполнил действия на устройствах."},new{role="user",content=text}}});
        using var response=await Http.SendAsync(request,token);
        if(!response.IsSuccessStatusCode)throw new AssistantApiException(502,"Облачная модель недоступна.");
        var json=await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken:token);
        var answer=json.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()?.Trim();
        if(string.IsNullOrWhiteSpace(answer))throw new AssistantApiException(422,"Модель не вернула текст.");
        return answer[..Math.Min(8000,answer.Length)];
    },ct);
    public static Task<string> Speech(Guid account,Stream source,IConfiguration config,CancellationToken ct)=>Run(account,async token=>{
        var endpoint=Endpoint(config,"VoiceTranscription",true);
        using var audio=new MemoryStream();var buffer=new byte[8192];int count;
        while((count=await source.ReadAsync(buffer,token))>0){if(audio.Length+count>5*1024*1024)throw new AssistantApiException(413,"Аудио до 5 МиБ.");audio.Write(buffer,0,count);}
        var bytes=audio.GetBuffer();var size=(int)audio.Length;
        var mime=size>=12&&bytes.AsSpan(0,4).SequenceEqual(new byte[]{0x1a,0x45,0xdf,0xa3})?"audio/webm":size>=12&&bytes.AsSpan(0,4).SequenceEqual("OggS"u8)?"audio/ogg":size>=12&&bytes.AsSpan(4,4).SequenceEqual("ftyp"u8)?"audio/mp4":null;
        if(mime is null)throw new AssistantApiException(415,"Ожидается WebM, Ogg или MP4.");
        audio.Position=0;using var request=new HttpRequestMessage(HttpMethod.Post,endpoint);Key(request,config,"VoiceTranscription");
        using var form=new MultipartFormDataContent();using var file=new StreamContent(audio);file.Headers.ContentType=new MediaTypeHeaderValue(mime);
        form.Add(file,"file","voice."+mime.Split('/')[1]);form.Add(new StringContent(config["VoiceTranscription:Model"]??"Systran/faster-whisper-small"),"model");form.Add(new StringContent("json"),"response_format");request.Content=form;
        using var response=await Http.SendAsync(request,token);
        if(!response.IsSuccessStatusCode)throw new AssistantApiException(502,"Распознавание недоступно.");
        var json=await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken:token);var text=json.GetProperty("text").GetString()?.Trim();
        if(string.IsNullOrWhiteSpace(text))throw new AssistantApiException(422,"Речь не распознана.");
        return text[..Math.Min(6000,text.Length)];
    },ct);
}
