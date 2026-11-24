using System.Collections.Concurrent;
using Microsoft.AspNetCore.Http;
using Models.Data;
using MysticEgo.Deployment;

namespace MysticEgo.Distributed;

public sealed record AssistantMessage(Guid Id, string Role, string Text, DateTimeOffset At);

[Entity("mysticego.assistant.conversation", 1)]
public sealed class AssistantConversation
{
    [EntityKey] public Guid Id { get; set; }
    public AssistantMessage[] Messages { get; set; } = [];
}

public sealed class AssistantConversations(NodeState state)
{
    private readonly ConcurrentDictionary<Guid, byte> _pending = new();

    public async Task<AssistantMessage[]> Read(Guid account, CancellationToken ct) =>
        (await state.Data.GetAsync<AssistantConversation>(account, ct))?.Messages ?? [];

    public async Task Clear(Guid account, CancellationToken ct)
    {
        Acquire(account);
        try { await state.Data.DeleteAsync<AssistantConversation>(account, ct); }
        finally { _pending.TryRemove(account, out _); }
    }

    public async Task<object> Reply(Guid account, string actor, ILocalAssistantService assistant, LmChatRequest input, bool deviceTools, CancellationToken ct)
    {
        Acquire(account);
        try
        {
            var history = await Read(account, ct);
            var recent = Context(history);
            var sent = new AssistantMessage(Guid.NewGuid(), "user", input.Text?.Trim() ?? "", DateTimeOffset.UtcNow);
            var reply = await assistant.ChatAsync(actor, input, ct, recent, deviceTools);
            var received = new AssistantMessage(Guid.NewGuid(), "assistant", reply.Answer.Length > 0 ? reply.Answer : "Подготовлено предложение для подтверждения.", DateTimeOffset.UtcNow);
            await state.Data.PutAsync(new AssistantConversation { Id = account, Messages = [..history.TakeLast(38), sent, received] }, ct);
            return new { answer = received.Text, reply.Proposals, sent, received };
        }
        finally { _pending.TryRemove(account, out _); }
    }

    private void Acquire(Guid account)
    {
        if (!_pending.TryAdd(account, 0))
            throw new AssistantApiException(StatusCodes.Status409Conflict, "Дождитесь ответа на предыдущее сообщение.");
    }

    private static LocalModelMessage[] Context(AssistantMessage[] history)
    {
        var remaining = 3800;
        var turns = new List<LocalModelMessage>();
        for (var index = history.Length - 2; index >= Math.Max(0, history.Length - 10); index -= 2)
        {
            var question = history[index];
            var answer = history[index + 1];
            var length = question.Text.Length + answer.Text.Length;
            if (length > remaining) break;
            turns.Add(new(answer.Role, answer.Text));
            turns.Add(new(question.Role, question.Text));
            remaining -= length;
        }
        turns.Reverse();
        return [..turns];
    }
}
