using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MysticEgo.Distributed;
using MysticEgo.WorldAI;
using MysticEgo.Soul;

internal sealed class QuestFixtureProvider : HttpMessageHandler
{
    public string Reply { get; set; } = "";
    public string? LastRequest { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        LastRequest = await request.Content!.ReadAsStringAsync(ct);
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { choices = new[] { new { message = new { content = Reply } } } })
        };
    }
}

internal static class QuestProposalSmoke
{
    private const string Route = "/api/v1/world-ai/quest-proposals";

    public static async Task Run(HttpClient http, IServiceProvider services, QuestFixtureProvider provider,
        Guid owner, Guid other, Func<string, string, string, string> token)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = timeout.Token;
        var queue = services.GetRequiredService<WorldAiJobQueue>();
        var cluster = services.GetRequiredService<WorldAiCluster>();
        var config = services.GetRequiredService<IConfiguration>();
        var node = services.GetRequiredService<NodeOptions>();
        var fact = await cluster.AddFact("world", "The river froze.", ct);
        await cluster.AddFact("lore", "unrelated-lore-marker", ct);
        var npc = await cluster.AddFact("npc", "An unrelated NPC", ct);
        var requestId = Guid.NewGuid();
        var request = new { requestId, factId = fact.Id };
        void Sign(Guid who, string role, string kind = "account") =>
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token(who.ToString(), role, kind));
        async Task Expect(HttpResponseMessage response, HttpStatusCode status)
        {
            using (response)
            {
                if (response.StatusCode != status)
                    throw new Exception($"Expected {status}, received {response.StatusCode}: {await response.Content.ReadAsStringAsync(ct)}");
            }
        }

        http.DefaultRequestHeaders.Authorization = null;
        await Expect(await http.PostAsJsonAsync(Route, request, ct), HttpStatusCode.Unauthorized);
        Sign(other, "system.developer");
        await Expect(await http.PostAsJsonAsync(Route, request, ct), HttpStatusCode.Forbidden);
        Sign(owner, "system.developer", "device");
        await Expect(await http.PostAsJsonAsync(Route, request, ct), HttpStatusCode.Forbidden);
        Sign(owner, "system.developer");
        await Expect(await http.PostAsJsonAsync(Route, new { requestId, factId = npc.Id }, ct), HttpStatusCode.BadRequest);
        await Expect(await http.PostAsJsonAsync(Route, new { requestId, factId = fact.Id, owner }, ct), HttpStatusCode.BadRequest);

        async Task<Uri> Create()
        {
            using var response = await http.PostAsJsonAsync(Route, request, ct);
            if (response.StatusCode != HttpStatusCode.Accepted || response.Headers.Location is not { } location)
                throw new Exception("Missing draft receipt");
            return location;
        }

        var receipts = await Task.WhenAll(Create(), Create());
        if (receipts[0] != receipts[1]) throw new Exception("Concurrent submit created two drafts");
        var url = receipts[0];
        var id = url.Segments.Last();
        await Expect(await http.PostAsJsonAsync(Route, new { requestId, factId = Guid.NewGuid().ToString("N") }, ct), HttpStatusCode.Conflict);
        await Expect(await http.GetAsync("/api/v1/world-ai/jobs/" + id, ct), HttpStatusCode.NotFound);

        config["DevelopmentTeam:OwnerId"] = other.ToString();
        Sign(other, "system.developer");
        await Expect(await http.GetAsync(url, ct), HttpStatusCode.NotFound);
        config["DevelopmentTeam:OwnerId"] = owner.ToString();
        Sign(owner, "system.developer");
        var currentWorld = node.NodeId;
        node.NodeId = "another-test-world";
        await Expect(await http.GetAsync(url, ct), HttpStatusCode.NotFound);
        node.NodeId = currentWorld;

        var held = await queue.Take(ct);
        if (held?.Id != id) throw new Exception("Draft not queued");
        try
        {
            await cluster.Chat(new(held.Role, held.Prompt, held.ContextFacts, held.MaxTokens), ct);
            throw new Exception("Expected unavailable provider");
        }
        catch (InvalidOperationException) { }
        await queue.Finish(id, null, null, "worker_unavailable", ct);
        var saved = await new WorldAiJobQueue(config).Find(id, ct);
        if (saved is not { State: "pending", Result: null, ErrorCode: "worker_unavailable" } ||
            saved.QuestDraft?.FactId != fact.Id || saved.NextAttemptAt <= saved.CreatedAt)
            throw new Exception("Offline draft lost its retry or fact scope");

        var souls = services.GetRequiredService<ISoulService>();
        var player = other.ToString();
        await souls.Register(player, ct);
        var staticQuest = await souls.Quest(player, new("water", "Проверим воду."), ct);
        if (!staticQuest.Passed) throw new Exception("Offline static quest failed");
        var progressBefore = JsonSerializer.Serialize(await souls.Get(player, ct));

        var payload = new { version = 1, title = "Crossing", description = "Inspect the river.",
            objective = "Describe the ice.", difficulty = "easy", factId = fact.Id };
        provider.Reply = JsonSerializer.Serialize(payload);
        await cluster.Register(new("http://127.0.0.1:1234", "fixture-model", "world", 1, 1), ct);
        using var worker = new WorldAiQueueWorker(queue, cluster, services.GetRequiredService<ILogger<WorldAiQueueWorker>>());
        await worker.StartAsync(ct);
        try
        {
            while ((await queue.Find(id, ct))?.State != "completed") await Task.Delay(50, ct);
            var view = await http.GetFromJsonAsync<JsonElement>(url, ct);
            if (view.GetProperty("proposal").GetProperty("title").GetString() != payload.title ||
                provider.LastRequest is not { } prompt || !prompt.Contains(fact.Id) ||
                !prompt.Contains("The river froze.") || prompt.Contains("unrelated-lore-marker"))
                throw new Exception("Draft readback or model context isolation failed");
        }
        finally { await worker.StopAsync(CancellationToken.None); }

        foreach (var invalid in new[] { "not-json", "null", "{}", provider.Reply.Replace("easy", "legendary"),
            provider.Reply.Replace(fact.Id, npc.Id), provider.Reply.Replace("Crossing", new string('x', 101)),
            provider.Reply.Replace("\"version\":1", "\"version\":1,\"version\":1"),
            provider.Reply.Replace("\"version\":1", "\"version\":1,\"reward\":999") })
        {
            var submitted = await queue.EnqueueQuest(held.QuestDraft! with { RequestId = Guid.NewGuid() }, cluster, ct);
            var running = await queue.Take(ct);
            if (running?.Id != submitted.Id) throw new Exception("Unexpected reservation");
            await queue.Finish(running.Id, invalid, "fixture", null, ct);
            var failed = await new WorldAiJobQueue(config).Find(running.Id, ct);
            if (failed is not { State: "failed", Result: null, ErrorCode: "invalid_quest_proposal" })
                throw new Exception("Invalid model output persisted as a proposal");
        }
        if (JsonSerializer.Serialize(await souls.Get(player, ct)) != progressBefore)
            throw new Exception("Draft generation mutated player progress");
        Console.WriteLine("PASS quest draft HTTP, owner/world isolation, retry, provider context, schema and persistence");
    }
}
