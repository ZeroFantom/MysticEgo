using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace MysticEgo.Avatar;

// Temporary compatibility adapter for existing ReplicaFiles domain delivery (already returns IResult).
public static class LegacyReplicaResult
{
    public static IActionResult ToActionResult(this IResult result) => new ReplicaActionResult(result);
    private sealed class ReplicaActionResult(IResult response) : IActionResult
    {
        public Task ExecuteResultAsync(ActionContext context) => response.ExecuteAsync(context.HttpContext);
    }
}
