namespace UniMap360.Services.Admin;

public interface ISuperAdminGuardService
{
    Task<int?> GetOwnerAdminAccountIdAsync(CancellationToken cancellationToken = default);
    Task<bool> IsOwnerAdminAsync(int accountId, CancellationToken cancellationToken = default);
}