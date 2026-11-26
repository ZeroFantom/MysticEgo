using System.Net.Http.Headers;
using System.Net.Http.Json;
using MysticEgo.Distributed;

namespace MysticEgo.Api.Orchestra.Controllers;

/// <summary>Stores a voice clip per message. Callers must check conversation access before opening a clip.</summary>
public sealed class CollectiveVoice(NodeState state, IHttpClientFactory clients, IConfiguration configuration)
{
    public const int MaxBytes = 5 * 1024 * 1024;
    private readonly string folder = Path.Combine(
        Path.GetDirectoryName(Path.GetFullPath(state.Options.DataPath))!, "voice");

    private string PathFor(Guid id) => Path.Combine(folder, id.ToString("N") + ".audio");

    public async Task<string> Save(Guid id, Stream source, CancellationToken ct)
    {
        using var audio = new MemoryStream();
        var buffer = new byte[8192];
        int size;
        while ((size = await source.ReadAsync(buffer, ct)) != 0)
        {
            if (audio.Length + size > MaxBytes)
                throw new NodeApiException(413, "Голосовое сообщение должно быть до 5 МиБ.");
            audio.Write(buffer, 0, size);
        }

        var mime = DetectMime(audio.GetBuffer().AsSpan(0, (int)audio.Length));
        Directory.CreateDirectory(folder);
        await using var output = new FileStream(PathFor(id), FileMode.CreateNew, FileAccess.Write);
        audio.Position = 0;
        await audio.CopyToAsync(output, ct);
        return mime;
    }

    private static string DetectMime(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 12)
        {
            if (bytes[..4].SequenceEqual(new byte[] { 0x1a, 0x45, 0xdf, 0xa3 }))
                return "audio/webm";
            if (bytes[..4].SequenceEqual("OggS"u8)) return "audio/ogg";
            if (bytes.Slice(4, 4).SequenceEqual("ftyp"u8)) return "audio/mp4";
        }
        throw new NodeApiException(415, "Ожидается голосовое сообщение WebM, Ogg или MP4.");
    }

    public FileStream Open(Guid id)
    {
        try { return new FileStream(PathFor(id), FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete); }
        catch (IOException) { throw new NodeApiException(404, "Аудио не найдено."); }
    }

    public void Delete(Guid id)
    {
        try { File.Delete(PathFor(id)); }
        catch (IOException) { state.Log("orchestra", "voice.cleanup", id.ToString(), false); }
        catch (UnauthorizedAccessException) { state.Log("orchestra", "voice.cleanup", id.ToString(), false); }
    }

    public async Task<string> Transcribe(SocialMessage message, CancellationToken ct)
    {
        if (!Uri.TryCreate(configuration["VoiceTranscription:Endpoint"], UriKind.Absolute, out var endpoint) ||
            (endpoint.Scheme != Uri.UriSchemeHttps && !(endpoint.Scheme == Uri.UriSchemeHttp && endpoint.IsLoopback)) ||
            endpoint.UserInfo.Length > 0)
            throw new NodeApiException(503, "Распознавание речи ещё не настроено администратором.");

        var extension = message.VoiceMime switch
        {
            "audio/webm" => "webm",
            "audio/ogg" => "ogg",
            "audio/mp4" => "mp4",
            _ => throw new NodeApiException(404, "Аудио не найдено.")
        };
        using var http = clients.CreateClient("voice-transcription");
        using var input = Open(message.Id);
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        if (configuration["VoiceTranscription:ApiKey"] is { Length: > 0 } key)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);

        using var form = new MultipartFormDataContent();
        using var file = new StreamContent(input);
        file.Headers.ContentType = new MediaTypeHeaderValue(message.VoiceMime!);
        form.Add(file, "file", "voice." + extension);
        form.Add(new StringContent(configuration["VoiceTranscription:Model"] ?? "Systran/faster-whisper-small"), "model");
        form.Add(new StringContent("json"), "response_format");
        request.Content = form;

        try
        {
            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
                throw new NodeApiException(502, "Сервис распознавания пока недоступен.");
            var text = (await response.Content.ReadFromJsonAsync<TranscriptResult>(cancellationToken: ct))?.Text?.Trim();
            if (string.IsNullOrEmpty(text))
                throw new NodeApiException(422, "Речь не распознана.");
            return text.Length > 6000 ? text[..6000] : text;
        }
        catch (Exception error) when (error is HttpRequestException or System.Text.Json.JsonException ||
            error is OperationCanceledException && !ct.IsCancellationRequested)
        {
            throw new NodeApiException(502, "Не удалось распознать голосовое сообщение.");
        }
    }

    private sealed record TranscriptResult(string? Text);
}
