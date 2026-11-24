using Models.Autorization.Contracts.Requests;
using Models.Autorization.Contracts.Responses;

namespace Models.Autorization.Services;

public interface IUserService
{
    Task<List<UserDto>> ListAsync(Guid callerId, CancellationToken ct = default);
    Task<UserDto?> GetAsync(Guid id, CancellationToken ct = default);
    Task<UserDto> CreateAsync(Guid callerId, CreateUserRequest req, CancellationToken ct = default);
    Task<UserDto?> UpdateAsync(Guid callerId, Guid id, UpdateUserRequest req, CancellationToken ct = default);
    Task<bool> SetActiveAsync(Guid callerId, Guid id, bool active, CancellationToken ct = default);
    Task<bool> ChangePasswordAsync(Guid callerId, Guid id, string newPassword, CancellationToken ct = default);
    Task<bool> ChangePasswordSelfAsync(Guid id, string currentPwd, string newPassword, CancellationToken ct = default);
}