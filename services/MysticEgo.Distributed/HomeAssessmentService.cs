using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace MysticEgo.Distributed;

public interface IHomeAssessmentService
{
    Task<HomeAssessment?> Get(string owner,CancellationToken ct);
    Task<HomeAssessment> Save(string owner,HomeAssessmentEdit request,CancellationToken ct);
}
public sealed class HomeAssessmentService(NodeState state) : IHomeAssessmentService
{
    public async Task<HomeAssessment?> Get(string owner,CancellationToken ct) =>
        (await state.Data.ListAsync<HomeAssessment>(1000,ct)).FirstOrDefault(x=>x.UserId==owner);
    public async Task<HomeAssessment> Save(string owner,HomeAssessmentEdit request,CancellationToken ct)
    {
        if(string.IsNullOrWhiteSpace(request.Nickname)||request.Nickname.Length>60||
           request.Answers is null||request.Answers.Count>30||
           request.Answers.Any(x=>x.Key.Length>80||x.Value is <1 or >5)||
           request.Achievements is null||request.Achievements.Length>100||request.Achievements.Any(x=>x.Length>200))
            throw new NodeApiException(400,"Недопустимые значения самооценки.");
        var item=await Get(owner,ct)??new HomeAssessment {Id=Guid.NewGuid(),UserId=owner};
        item.Nickname=request.Nickname.Trim();item.AnswersJson=JsonSerializer.Serialize(request.Answers);
        item.AchievementsJson=JsonSerializer.Serialize(request.Achievements);
        item.Rank="F";item.Level=1;item.UpdatedAt=DateTimeOffset.UtcNow;
        await state.Data.PutAsync(item,ct);state.Log(owner,"audit.assessment.update",item.Id.ToString(),true);
        return item;
    }
}
public static class HomeAssessmentDependencyInjection
{
    public static IServiceCollection AddHomeAssessment(this IServiceCollection services)
    {
        services.AddScoped<IHomeAssessmentService,HomeAssessmentService>();return services;
    }
}
