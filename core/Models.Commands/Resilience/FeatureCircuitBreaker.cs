using System.Collections.Concurrent;

namespace Models.Commands.Resilience;

/// <summary>
/// Gate used around an OPTIONAL subsystem. It never shuts down a host.
/// Concurrency-safe, bounded backoff; caller must explicitly catch
/// FeatureTemporarilyUnavailableException and render a 503 or disabled feature.
/// </summary>
public sealed class FeatureTemporarilyUnavailableException : Exception
{
    public string Feature { get; }
    public FeatureTemporarilyUnavailableException(string feature)
        : base($"Optional feature '{feature}' is temporarily unavailable") => Feature=feature;
}

public sealed record FeatureCircuitState(string Feature,int ConsecutiveFailures,
    DateTimeOffset? RetryAfterUtc,bool Open);

public sealed class FeatureCircuitBreaker
{
    private sealed class Entry
    {
        public readonly object Sync=new();
        public int Failures;
        public DateTimeOffset RetryAt;
    }
    private readonly ConcurrentDictionary<string,Entry> _entries=new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeProvider _clock;
    private readonly TimeSpan _baseDelay;
    private readonly TimeSpan _maxDelay;
    public FeatureCircuitBreaker(TimeProvider? clock=null,TimeSpan? baseDelay=null,TimeSpan? maxDelay=null)
    {
        _clock=clock??TimeProvider.System;
        _baseDelay=baseDelay??TimeSpan.FromSeconds(3);
        _maxDelay=maxDelay??TimeSpan.FromMinutes(2);
        if(_baseDelay<=TimeSpan.Zero||_maxDelay<_baseDelay)throw new ArgumentOutOfRangeException(nameof(baseDelay));
    }
    public FeatureCircuitState Status(string feature)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(feature);
        if(!_entries.TryGetValue(feature,out var e))return new(feature,0,null,false);
        lock(e.Sync){var open=e.RetryAt>_clock.GetUtcNow();
            return new(feature,e.Failures,open?e.RetryAt:null,open);}
    }
    public void Success(string feature)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(feature);
        _entries.TryRemove(feature,out _);
    }
    public void Failure(string feature)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(feature);
        var e=_entries.GetOrAdd(feature,_=>new Entry());
        lock(e.Sync)
        {
            e.Failures=Math.Min(e.Failures+1,16);
            var seconds=Math.Min(_maxDelay.TotalSeconds,_baseDelay.TotalSeconds*Math.Pow(2,e.Failures-1));
            e.RetryAt=_clock.GetUtcNow().AddSeconds(seconds);
        }
    }
    public async Task<T> ExecuteAsync<T>(string feature,Func<CancellationToken,Task<T>> operation,
        Func<Exception,bool>? isTransient=null,CancellationToken cancellationToken=default)
    {
        if(Status(feature).Open)throw new FeatureTemporarilyUnavailableException(feature);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var result=await operation(cancellationToken).ConfigureAwait(false);
            Success(feature);return result;
        }
        catch(Exception e) when(e is not OperationCanceledException && (isTransient?.Invoke(e)??true))
        {Failure(feature);throw;}
    }
}
