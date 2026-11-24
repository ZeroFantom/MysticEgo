using Models.Autorization.Contracts.Responses;

namespace Models.Autorization.Services;

public interface IToolService
{
    Task<List<ToolDto>> ListAsync(CancellationToken ct = default);
    Task<bool> SetEnabledAsync(int id, bool enabled, CancellationToken ct = default);
}