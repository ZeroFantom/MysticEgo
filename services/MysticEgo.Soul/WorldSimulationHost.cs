using System.Threading.Channels;
using Microsoft.Extensions.Hosting;

namespace MysticEgo.Soul;

public sealed record SimulationBatchResult(bool Applied,long Revision);

/// <summary>Single trusted in-process simulation host. No public endpoint, model tool or
/// player event producer. Producers must authenticate/authorize events before Submit.
/// Queue is bounded and volatile; accepted work is acknowledged only after durable CAS.
/// Heartbeats never reanimate a stale or externally modified snapshot.</summary>
public sealed class WorldSimulationHost(WorldAuthorityStore store,WorldSimulationCore core,TimeProvider clock,NpcRoutinePlanner? routines=null)
    : BackgroundService
{
    private sealed record Batch(long Expected,WorldSimulationEvent[] Events,TaskCompletionSource<SimulationBatchResult> Completion);
    private readonly Channel<Batch> queue=Channel.CreateBounded<Batch>(new BoundedChannelOptions(128)
    {SingleReader=true,SingleWriter=false,FullMode=BoundedChannelFullMode.Wait});
    private long activeRevision;
    private int accepting;
    public bool Running=>Volatile.Read(ref accepting)==1;
    public long ActiveRevision=>Interlocked.Read(ref activeRevision);

    /// <summary>Once queued, cancellation of the caller does not cancel the batch.
    /// Caller must reconcile uncertain results from durable revision; never blindly replay.</summary>
    public Task<SimulationBatchResult> Submit(long expected,IReadOnlyList<WorldSimulationEvent> events)
    {
        if(!Running||expected<0||events is null||events.Count is <1 or >64)
            return Task.FromResult(new SimulationBatchResult(false,ActiveRevision));
        // Copy mutable NPC knowledge before crossing threads.
        var copy=events.Select(e=>e is SpawnWorldNpc npc&&npc.Profile?.KnownFacts is { } facts
            ? npc with {Profile=npc.Profile with {KnownFacts=Array.AsReadOnly(facts.ToArray())}}:e).ToArray();
        var completion=new TaskCompletionSource<SimulationBatchResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        if(!queue.Writer.TryWrite(new(expected,copy,completion)))
            completion.TrySetResult(new(false,ActiveRevision));
        return completion.Task;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // One host instance per private authority DB. External writers are not supported.
        // No heartbeat until an explicitly authorized batch has succeeded in this process.
        Volatile.Write(ref accepting,1);
        try
        {
            using var timer=new PeriodicTimer(TimeSpan.FromSeconds(3),clock);
            while(await timer.WaitForNextTickAsync(stoppingToken))
            {
                // Bound work per tick; keep time from starving under a large queue.
                for(int i=0;i<16&&queue.Reader.TryRead(out var batch);i++)
                {
                    if(stoppingToken.IsCancellationRequested)
                    {batch.Completion.TrySetResult(new(false,ActiveRevision));break;}
                    try
                    {
                        var ok=core.Apply(batch.Expected,batch.Events,out var revision);
                        if(ok)Interlocked.Exchange(ref activeRevision,revision);
                        else if(ActiveRevision!=revision)Interlocked.Exchange(ref activeRevision,0);
                        batch.Completion.TrySetResult(new(ok,revision));
                    }
                    catch(Exception ex) when(ex is Microsoft.Data.Sqlite.SqliteException or IOException or
                        UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException or
                        InvalidOperationException or NullReferenceException or OverflowException)
                    {
                        Interlocked.Exchange(ref activeRevision,0);
                        batch.Completion.TrySetResult(new(false,0));
                    }
                }
                if(ActiveRevision<=0)continue;
                try
                {
                    if(stoppingToken.IsCancellationRequested)break;
                    var expected=ActiveRevision;
                    if(!store.TryTick(expected)){Interlocked.Exchange(ref activeRevision,0);continue;}
                    if(routines is null)continue;
                    var state=store.Current();
                    if(state is null||state.Revision!=expected||
                        state.Feed.PublishedUtc<=clock.GetUtcNow().AddSeconds(-15)||
                        state.Feed.PublishedUtc>clock.GetUtcNow().AddSeconds(2))
                    {Interlocked.Exchange(ref activeRevision,0);continue;}
                    var moves=routines.Plan(state.Feed.Snapshot,state.Feed.SimulationTick);
                    if(moves.Count>0)
                    {
                        if(core.Apply(expected,moves,out var next))Interlocked.Exchange(ref activeRevision,next);
                        else Interlocked.Exchange(ref activeRevision,0);
                    }
                }
                catch(Exception ex) when(ex is Microsoft.Data.Sqlite.SqliteException or IOException or
                    UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException or
                    InvalidOperationException or NullReferenceException or OverflowException)
                {Interlocked.Exchange(ref activeRevision,0);}
            }
        }
        catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested){}
        finally
        {
            Volatile.Write(ref accepting,0);
            Interlocked.Exchange(ref activeRevision,0);
            queue.Writer.TryComplete();
            while(queue.Reader.TryRead(out var pending))pending.Completion.TrySetResult(new(false,0));
        }
    }
}
