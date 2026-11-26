using System.Buffers.Binary;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MysticEgo.Distributed;
using Models.Data;

namespace MysticEgo.Api.Orchestra.Controllers;

[Entity("mysticego.collective.sticker", 1)]
public sealed class Sticker
{
    [EntityKey] public Guid Id { get; set; }
    public Guid Owner { get; set; }
    public string Title { get; set; } = "";
    public string Mime { get; set; } = "";
    public int Width { get; set; }
    public int Height { get; set; }
    public bool Saved { get; set; } = true;
}

[ApiController, Authorize(Policy = "operator"), Route("api/v1/collective/stickers")]
public sealed class StickersController(NodeState state, StickerStore stickers) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) => Ok(await stickers.List(Actor, ct));

    [HttpPost, RequestSizeLimit(524288)]
    public async Task<IActionResult> Upload([FromQuery] string? name, CancellationToken ct)
    {
        try { return Ok(await stickers.Add(Actor, name, Request.Body, ct)); }
        catch (NodeApiException error) { return StatusCode(error.StatusCode, new { error = error.Message }); }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Remove(Guid id, CancellationToken ct)
    {
        var saved = await state.Data.GetAsync<Sticker>(id, ct);
        if (saved?.Owner != Actor) return NotFound();
        saved.Saved = false;
        await state.Data.PutAsync(saved, ct);
        return NoContent();
    }

    [HttpGet("{id:guid}/image")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> View(Guid id, CancellationToken ct)
    {
        var saved = await state.Data.GetAsync<Sticker>(id, ct);
        if (saved is null) return NotFound();
        var actor = Actor.ToString();
        if (saved.Owner != Actor && !(await state.Data.ListAsync<SocialMessage>(5000, ct))
            .Any(m => m.DeletedAt is null && m.StickerId == id && (m.Recipient == actor || m.Sender == actor)))
            return NotFound();
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        var file = stickers.PathFor(id);
        return System.IO.File.Exists(file) ? PhysicalFile(file, saved.Mime) : NotFound();
    }
}

public sealed class StickerStore(NodeState state)
{
    private readonly SemaphoreSlim writes = new(1, 1);
    private readonly string folder = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(state.Options.DataPath))!, "stickers");
    public string PathFor(Guid id) => Path.Combine(folder, id.ToString("N") + ".image");
    public async Task<Sticker[]> List(Guid actor, CancellationToken ct) =>
        (await state.Data.ListAsync<Sticker>(5000, ct)).Where(x => x.Owner == actor && x.Saved).Take(80).ToArray();

    public async Task<Sticker> Add(Guid actor, string? title, Stream request, CancellationToken ct)
    {
        using var image = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = await request.ReadAsync(buffer, ct)) != 0)
        {
            if (image.Length + count > 524288) throw new NodeApiException(413, "Размер стикера до 512 КиБ.");
            image.Write(buffer, 0, count);
        }
        var size = Inspect(image.ToArray());
        if (size is not { Width: >= 32 and <= 512, Height: >= 32 and <= 512 })
            throw new NodeApiException(400, "Стикер должен быть PNG, GIF или WebP от 32 до 512 пикселей по каждой стороне.");
        title = (title ?? "").Trim();
        if (title.Length > 60 || title.Any(char.IsControl)) throw new NodeApiException(400, "Название должно содержать до 60 символов.");
        await writes.WaitAsync(ct);
        try
        {
            if ((await List(actor, ct)).Length >= 80) throw new NodeApiException(409, "В коллекции помещается 80 стикеров.");
            var sticker = new Sticker
            {
                Id = Guid.NewGuid(), Owner = actor, Title = title.Length == 0 ? "Стикер" : title,
                Mime = size.Value.Mime, Width = size.Value.Width, Height = size.Value.Height
            };
            Directory.CreateDirectory(folder);
            var path = PathFor(sticker.Id);
            try
            {
                await System.IO.File.WriteAllBytesAsync(path, image.ToArray(), ct);
                await state.Data.PutAsync(sticker, ct);
                return sticker;
            }
            catch { System.IO.File.Delete(path); throw; }
        }
        finally { writes.Release(); }
    }

    private static (string Mime, int Width, int Height)? Inspect(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 33 && bytes[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) &&
            bytes.Slice(12, 4).SequenceEqual("IHDR"u8))
            return ("image/png", BinaryPrimitives.ReadInt32BigEndian(bytes[16..]), BinaryPrimitives.ReadInt32BigEndian(bytes[20..]));
        if (bytes.Length >= 14 && (bytes[..6].SequenceEqual("GIF87a"u8) || bytes[..6].SequenceEqual("GIF89a"u8)))
            return ("image/gif", BinaryPrimitives.ReadUInt16LittleEndian(bytes[6..]), BinaryPrimitives.ReadUInt16LittleEndian(bytes[8..]));
        if (bytes.Length < 30 || !bytes[..4].SequenceEqual("RIFF"u8) || !bytes.Slice(8, 4).SequenceEqual("WEBP"u8)) return null;
        var kind = bytes.Slice(12, 4);
        if (kind.SequenceEqual("VP8X"u8)) return ("image/webp", 1 + Size24(bytes[24..]), 1 + Size24(bytes[27..]));
        if (kind.SequenceEqual("VP8 "u8) && bytes.Slice(23, 3).SequenceEqual(new byte[] { 157, 1, 42 }))
            return ("image/webp", BinaryPrimitives.ReadUInt16LittleEndian(bytes[26..]) & 0x3fff, BinaryPrimitives.ReadUInt16LittleEndian(bytes[28..]) & 0x3fff);
        if (kind.SequenceEqual("VP8L"u8) && bytes[20] == 0x2f)
        {
            var packed = BinaryPrimitives.ReadUInt32LittleEndian(bytes[21..]);
            return ("image/webp", 1 + (int)(packed & 0x3fff), 1 + (int)((packed >> 14) & 0x3fff));
        }
        return null;
        static int Size24(ReadOnlySpan<byte> b) => b[0] | b[1] << 8 | b[2] << 16;
    }
}
