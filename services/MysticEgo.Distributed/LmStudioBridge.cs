namespace MysticEgo.Distributed;

// Contracts remain compatible with the pre-refactor /api/v1/assistant API.
public sealed record LmChatRequest(string Text, string? Model);
public sealed record LmProposal(Guid Id, string Owner, string Type, string Argument, DateTimeOffset CreatedAt);
public sealed record LmProposalResponse(Guid Id, string Type, string Argument);
public sealed record LmChatResponse(string Answer, IReadOnlyList<LmProposalResponse> Proposals);
public sealed record LmStatusResponse(bool Ready, string Endpoint, string Message);
public sealed record AssistantActionResponse(bool Success, object Result);
