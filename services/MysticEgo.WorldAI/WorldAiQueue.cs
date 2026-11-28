using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MysticEgo.WorldAI;

// Beta 2: persistent single-Orchestra queue. Not a distributed message broker.
// All state changes are stored atomically. After restart running jobs become pending.
public sealed record EnqueueWorldAiRequest(string Role, string Prompt, string[]? ContextFacts, int MaxTokens = 400, string? IdempotencyKey = null);
public sealed record WorldAiJob(string Id, string Role, string Prompt, string[] ContextFacts, int MaxTokens,
    string? IdempotencyKey, string State, int Attempts, DateTimeOffset CreatedAt, DateTimeOffset NextAttemptAt,
    string? Result, string? WorkerId, string? ErrorCode, QuestDraftScope? QuestDraft = null);

public sealed class QuestRequestConflictException : Exception { }

public sealed class WorldAiStorageUnavailableException(string message):InvalidOperationException(message) { }

public sealed class WorldAiJobQueue
{
    private readonly SemaphoreSlim _gate = new(1,1);
    private readonly List<WorldAiJob> _jobs = new();
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _path;
    private readonly string? _recoveryError;
    private const int MaxJobs = 1000;
    private const int MaxAttempts = 5;
    public WorldAiJobQueue(IConfiguration cfg)
    {
        var dir = Path.GetFullPath(cfg["WorldAI:StoragePath"] ?? Path.Combine(AppContext.BaseDirectory,"world-ai-data"));
        _path = Path.Combine(dir,"jobs.json");
        try
        {
            Directory.CreateDirectory(dir);
            if(!File.Exists(_path)) return;
            var saved = JsonSerializer.Deserialize<WorldAiJob[]>(File.ReadAllText(_path), _json) ?? [];
            _jobs.AddRange(saved.Select(j => j.State == "running" ? j with { State="pending", NextAttemptAt=DateTimeOffset.UtcNow } : j));
        }
        catch (Exception e) when(e is JsonException or IOException or UnauthorizedAccessException)
        {
            // Do not silently overwrite an unparseable journal, which would lose pending work.
            _recoveryError="WorldAI job journal unreadable ("+e.GetType().Name+"); operator recovery required";
        }
    }
    private async Task Save(CancellationToken ct)
    {
        var temp=_path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            await File.WriteAllTextAsync(temp,JsonSerializer.Serialize(_jobs,_json),ct);
            if(!OperatingSystem.IsWindows()) File.SetUnixFileMode(temp,UnixFileMode.UserRead|UnixFileMode.UserWrite);
            File.Move(temp,_path,true);
        }
        finally {if(File.Exists(temp)) File.Delete(temp);}
    }
    public async Task<WorldAiJob> Enqueue(EnqueueWorldAiRequest request,CancellationToken ct)
    {
        if(!WorldAiCluster.SupportsRole(request.Role) ||
            string.IsNullOrWhiteSpace(request.Prompt) || request.Prompt.Length>5000 ||
            request.MaxTokens is <1 or >1200 || request.ContextFacts?.Length>50 ||
            (request.ContextFacts?.Any(x=>x is null || x.Length>2000) ?? false) ||
            request.IdempotencyKey?.Length>120)
            throw new ArgumentException("Invalid inference job");
        if(_recoveryError is not null) throw new WorldAiStorageUnavailableException(_recoveryError);
        await _gate.WaitAsync(ct);
        try
        {
            if(!string.IsNullOrWhiteSpace(request.IdempotencyKey))
            {
                var existing=_jobs.FirstOrDefault(x=>x.QuestDraft is null && x.IdempotencyKey==request.IdempotencyKey);
                if(existing is not null)
                {
                    if(existing.Role!=request.Role || existing.Prompt!=request.Prompt ||
                       existing.MaxTokens!=request.MaxTokens ||
                       !existing.ContextFacts.SequenceEqual(request.ContextFacts ?? []))
                        throw new ArgumentException("Idempotency key already used for a different request");
                    return existing;
                }
            }
            return await AddLocked(request, null, ct);
        }
        finally {_gate.Release();}
    }

    public async Task<WorldAiJob> EnqueueQuest(QuestDraftScope scope, WorldAiCluster cluster, CancellationToken ct)
    {
        if (_recoveryError is not null) throw new WorldAiStorageUnavailableException(_recoveryError);
        await _gate.WaitAsync(ct);
        try
        {
            var existing = _jobs.FirstOrDefault(job => job.QuestDraft is { } draft &&
                draft.Account == scope.Account && draft.World == scope.World && draft.RequestId == scope.RequestId);
            if (existing is not null)
            {
                if (existing.QuestDraft != scope) throw new QuestRequestConflictException();
                return existing;
            }

            var fact = cluster.Memory().FirstOrDefault(f => f.Id == scope.FactId && f.Kind is "world" or "lore");
            if (fact is null) throw new ArgumentException("Unknown world fact.");
            string[] snapshot = [$"factId: {fact.Id}", fact.Content];
            return await AddLocked(new("world", QuestProposal.Prompt, snapshot, 600), scope, ct);
        }
        finally { _gate.Release(); }
    }

    // The caller holds the queue lock through the journal write.
    private async Task<WorldAiJob> AddLocked(EnqueueWorldAiRequest request, QuestDraftScope? scope, CancellationToken ct)
    {
        if (_jobs.Count(job => job.State is "pending" or "running") >= MaxJobs)
            throw new InvalidOperationException("WorldAI queue is full");
        var now = DateTimeOffset.UtcNow;
        var job = new WorldAiJob(Guid.NewGuid().ToString("N"), request.Role, request.Prompt,
            request.ContextFacts ?? [], request.MaxTokens, request.IdempotencyKey,
            "pending", 0, now, now, null, null, null, scope);
        _jobs.Add(job);
        try { await Save(ct); }
        catch { _jobs.Remove(job); throw; }
        return job;
    }

    public async Task<WorldAiJob?> Find(string id,CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try{return _jobs.FirstOrDefault(j=>j.Id==id);}
        finally{_gate.Release();}
    }
    /// <summary>Return queue backlog only; do not export prompts or results to monitoring.</summary>
    public async Task<int?> PendingCount(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try{return _recoveryError is null?_jobs.Count(j=>j.State=="pending"):null;}
        finally{_gate.Release();}
    }
    public async Task<object> Status(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try {return new {degraded=_recoveryError is not null,reason=_recoveryError,pending=_jobs.Count(j=>j.State=="pending"),running=_jobs.Count(j=>j.State=="running"),
            completed=_jobs.Count(j=>j.State=="completed"),failed=_jobs.Count(j=>j.State=="failed"),capacity=MaxJobs};}
        finally {_gate.Release();}
    }
    public async Task<WorldAiJob?> Take(CancellationToken ct)
    {
        if(_recoveryError is not null)return null;
        await _gate.WaitAsync(ct);
        try
        {
            var current=_jobs.Where(j=>j.State=="pending"&&j.NextAttemptAt<=DateTimeOffset.UtcNow)
                .OrderBy(j=>j.CreatedAt).FirstOrDefault();
            if(current is null)return null;
            var updated=current with {State="running",Attempts=current.Attempts+1};
            var index=_jobs.FindIndex(j=>j.Id==current.Id);
            _jobs[index]=updated;
            try{await Save(ct);}catch{_jobs[index]=current;throw;}
            return updated;
        }
        finally {_gate.Release();}
    }
    public async Task Finish(string id,string? result,string? workerId,string? error,CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var index=_jobs.FindIndex(j=>j.Id==id);
            if(index<0) return;
            var old=_jobs[index];
            if (old.State != "running") return;
            if (old.QuestDraft is { } draft && error is null)
            {
                result = QuestProposal.Validate(result, draft.FactId);
                if (result is null) error = "invalid_quest_proposal";
            }
            var updated = error is null
                ? old with { State = "completed", Result = result, WorkerId = workerId, ErrorCode = null }
                : old.Attempts >= MaxAttempts || error == "invalid_quest_proposal"
                    ? old with { State = "failed", ErrorCode = error }
                    : old with { State = "pending", ErrorCode = error,
                        NextAttemptAt = DateTimeOffset.UtcNow.AddSeconds(Math.Min(300, Math.Pow(2, old.Attempts) * 5)) };

            // Keep the reservation and result consistent with the durable journal on a failed write.
            var beforePruning = _jobs.Count > MaxJobs * 2 ? _jobs.ToArray() : null;
            _jobs[index] = updated;
            if (beforePruning is not null)
            {
                foreach (var item in _jobs.Where(j => j.State is "completed" or "failed")
                    .OrderBy(j => j.CreatedAt).Take(_jobs.Count - MaxJobs * 2).ToArray()) _jobs.Remove(item);
            }
            try { await Save(ct); }
            catch
            {
                if (beforePruning is null) _jobs[index] = old;
                else { _jobs.Clear(); _jobs.AddRange(beforePruning); }
                throw;
            }
        }
        finally{_gate.Release();}
    }
}

public sealed class WorldAiQueueWorker(WorldAiJobQueue queue,WorldAiCluster cluster,ILogger<WorldAiQueueWorker> logger):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken token)
    {
        while(!token.IsCancellationRequested)
        {
            try
            {
                var job=await queue.Take(token);
                if(job is null){await Task.Delay(1000,token);continue;}
                try
                {
                    var response=await cluster.Chat(new ChatRequest(job.Role,job.Prompt,job.ContextFacts,job.MaxTokens),token, includeSharedMemory: job.QuestDraft is null);
                    var json=JsonSerializer.SerializeToElement(response);
                    await PersistOutcome(job.Id, json.GetProperty("text").GetString(), json.GetProperty("workerId").GetString(), null, token);
                }
                catch(OperationCanceledException) when(token.IsCancellationRequested){throw;}
                catch(Exception ex) when(ex is InvalidOperationException or HttpRequestException or JsonException or TaskCanceledException)
                {
                    logger.LogWarning("WorldAI job {JobId} delayed: {Error}",job.Id,ex.GetType().Name);
                    await PersistOutcome(job.Id, null, null, "worker_unavailable", token);
                }
            }
            catch(OperationCanceledException) when(token.IsCancellationRequested){break;}
            catch(Exception ex)
            {
                logger.LogError(ex,"WorldAI queue processor error; queue will retry, Orchestra remains online");
                try{await Task.Delay(3000,token);}catch(OperationCanceledException){break;}
            }
        }
    }

    // Retry the journal write with the same outcome so a transient I/O failure
    // cannot strand a running reservation or send the prompt to a model twice.
    private async Task PersistOutcome(string id, string? result, string? worker, string? error, CancellationToken ct)
    {
        while (true)
        {
            try
            {
                await queue.Finish(id, result, worker, error, ct);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning("WorldAI job {JobId} is waiting for its journal write", id);
                await Task.Delay(TimeSpan.FromSeconds(3), ct);
            }
        }
    }

}

public static class WorldAiQueueRegistration
{
    public static IServiceCollection AddWorldAiQueue(this IServiceCollection services)
    {
        services.AddSingleton<WorldAiJobQueue>();
        services.AddHostedService<WorldAiQueueWorker>();
        return services;
    }

}
