using Models.Commands;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MysticEgo.Distributed;
using Models.Data;

namespace MysticEgo.Api.Orchestra.Controllers;

[Entity("mysticego.faq.article", 1)]
public sealed class FaqArticle
{
    [EntityKey] public Guid Id { get; set; }
    public string Slug { get; set; } = "";
    public string Title { get; set; } = "";
    public string Category { get; set; } = "";
    public string Markdown { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; }
}

[ApiController, Authorize(Policy = "operator"), Route("api/v1/faq")]
public sealed class FaqController(NodeState state, IWebHostEnvironment environment, CommandCatalog commands) : ControllerBase
{
    private static readonly (string Slug, string Category)[] Articles =
    [
        ("start", "Экосистема"), ("deployment", "Экосистема"), ("operations", "Экосистема"),
        ("canon", "Игра"), ("shards", "Игра"), ("stats", "Игра"), ("character", "Ассистент")
    ];

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var articles = new List<object>();
        foreach (var (slug, category) in Articles)
        {
            var article = await Read(slug, category, ct);
            if (article is not null) articles.Add(article);
        }
        return Ok(new { articles, canEdit = EcosystemHierarchy.IsDeveloper(User), commands = await commands.Read(ct) });
    }

    [HttpPut("{slug}")]
    [ApiCapability("faq.article.edit", "Обновить статью FAQ", "orchestra", "system.bypass", RequiresConfirmation = true)]
    public async Task<IActionResult> Save(string slug, FaqEdit edit, CancellationToken ct)
    {
        if (!EcosystemHierarchy.IsDeveloper(User)) return Forbid();
        var entry = Articles.FirstOrDefault(x => x.Slug == slug);
        if (entry.Slug is null) return NotFound();
        if (string.IsNullOrWhiteSpace(edit.Title) || edit.Title.Length > 100 || edit.Markdown.Length is < 1 or > 50_000)
            return BadRequest(new { error = "Укажите заголовок и текст статьи до 50 000 символов." });
        var article = new FaqArticle { Id = Key(slug), Slug = slug, Category = entry.Category,
            Title = edit.Title.Trim(), Markdown = edit.Markdown, UpdatedAt = DateTimeOffset.UtcNow };
        await state.Data.PutAsync(article, ct);
        state.Log(User.Identity?.Name ?? "operator", "faq.edit", slug, true);
        return Ok(article);
    }

    private async Task<FaqArticle?> Read(string slug, string category, CancellationToken ct)
    {
        var article = await state.Data.GetAsync<FaqArticle>(Key(slug), ct);
        if (article is not null) return article;
        var source = Path.Combine(environment.WebRootPath ?? "wwwroot", "faq", slug + ".md");
        if (!System.IO.File.Exists(source)) return null;
        var lines = await System.IO.File.ReadAllLinesAsync(source, ct);
        if (lines.Length == 0) return null;
        return new() { Id = Key(slug), Slug = slug, Category = category,
            Title = lines[0].TrimStart('#', ' '), Markdown = string.Join('\n', lines.Skip(1)).Trim(),
            UpdatedAt = System.IO.File.GetLastWriteTimeUtc(source) };
    }

    private static Guid Key(string slug) => new(SHA256.HashData(Encoding.UTF8.GetBytes("mysticego.faq:" + slug)).AsSpan(0, 16));
    public sealed record FaqEdit(string Title, string Markdown);
}
