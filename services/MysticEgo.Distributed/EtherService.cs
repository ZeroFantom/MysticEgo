using Models.Data;
using Microsoft.Extensions.DependencyInjection;

namespace MysticEgo.Distributed;

public interface IEtherService
{
    Task<EtherAccount?> Get(string user,CancellationToken ct);
    Task<EtherAccount> Register(string user,CreateEtherAccount request,CancellationToken ct);
}
public sealed class EtherService(NodeState state) : IEtherService
{
    private readonly SemaphoreSlim _gate=new(1,1);
    public async Task<EtherAccount?> Get(string user,CancellationToken ct) =>
        (await state.Data.ListAsync<EtherAccount>(5000,ct)).FirstOrDefault(x=>x.UserId==user);
    public async Task<EtherAccount> Register(string user,CreateEtherAccount request,CancellationToken ct)
    {
        var nickname=(request.Nickname??"").Trim();
        if(nickname.Length is <3 or >40)throw new NodeApiException(400,"Никнейм должен содержать 3–40 символов.");
        await _gate.WaitAsync(ct);
        try
        {
            if(await Get(user,ct) is not null)throw new NodeApiException(409,"Игровой аккаунт уже создан.");
            var item=new EtherAccount{Id=Guid.NewGuid(),UserId=user,Nickname=nickname,GameAccountId=Guid.NewGuid().ToString("N")};
            await state.Data.PutAsync(item,ct);
            state.Log(user,"ether.account.create",item.Id.ToString(),true);
            return item;
        }
        finally{_gate.Release();}
    }
}
public static class EtherDependencyInjection
{
    public static IServiceCollection AddEther(this IServiceCollection services)
    {
        services.AddSingleton<IEtherService,EtherService>();return services;
    }
}
