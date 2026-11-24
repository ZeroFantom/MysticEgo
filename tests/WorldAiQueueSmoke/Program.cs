using Microsoft.Extensions.Configuration;
using MysticEgo.WorldAI;

var temp=Path.Combine(Path.GetTempPath(),"mysticego-queue-smoke-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temp);
try
{
    var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
    { ["WorldAI:StoragePath"]=temp }).Build();
    var queue=new WorldAiJobQueue(config);
    var command=new EnqueueWorldAiRequest("npc","Explain the quest",["Player found scroll"],200,"unique-1");
    var first=await queue.Enqueue(command,CancellationToken.None);
    var duplicate=await queue.Enqueue(command,CancellationToken.None);
    if(first.Id!=duplicate.Id)throw new Exception("Idempotency broken");
    try {await queue.Enqueue(command with { Prompt="different" },CancellationToken.None);
        throw new Exception("Conflicting idempotency key accepted");}
    catch(ArgumentException){}
    var picked=await queue.Take(CancellationToken.None);
    if(picked?.Id!=first.Id||picked.State!="running")throw new Exception("Queue did not reserve pending job");
    // Simulate process restart in the middle of inference; job must be recoverable.
    var recovered=new WorldAiJobQueue(config);
    var resumed=await recovered.Take(CancellationToken.None);
    if(resumed?.Id!=first.Id||resumed.Attempts!=2)throw new Exception("In-flight task not recovered");
    await recovered.Finish(first.Id,"Reply","worker-a",null,CancellationToken.None);
    var result=await new WorldAiJobQueue(config).Find(first.Id,CancellationToken.None);
    if(result?.State!="completed"||result.Result!="Reply")throw new Exception("Completed result not durable");
    Console.WriteLine("PASS queue persistence, idempotency, restart recovery, completion");
}
finally{Directory.Delete(temp,true);}
