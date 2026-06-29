namespace Shared.AuthService;

public interface IAuthService
{
    Task<int> ValidateToken(string token);
}