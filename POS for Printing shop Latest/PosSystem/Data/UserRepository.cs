using System.DirectoryServices.ActiveDirectory;

namespace PosSystem.Data;

public static class UserRepository
{

    private const string UserSelect = @"
        SELECT u.UserProfileId, u.userId, u.FullName, u.Password, u.UserGroupID, g.UserGroupName,
               CAST(ISNULL(g.IsSuperUser,0) AS BIT) AS IsSuperUser,
               CAST(ISNULL(u.IsSystemAccessible,0) AS BIT) AS IsSystemAccessible,
               CAST(ISNULL(u.IsDeleted,0) AS BIT) AS IsDeleted,
               u.EmployeId, u.DefaultLocationID, ISNULL(g.MaxDiscountPercent,0) AS MaxDiscountPercent
        FROM dbo.UserProfile u LEFT JOIN dbo.UserGroups g ON g.UserGroupID = u.UserGroupID";

    public static async Task<UserAccount?> GetByLoginAsync(string userId)
    {
        await using var cn = await Db.OpenAsync();
        return await cn.QueryFirstOrDefaultAsync<UserAccount>(UserSelect + " WHERE u.userId = @userId AND ISNULL(u.IsDeleted,0) = 0 ORDER BY u.UserProfileId", new { userId });
    }

    public static async Task<IReadOnlyList<string>> GetPermissionKeysAsync(int? roleId)
    {
        if (roleId == null) return Array.Empty<string>();
        await using var cn = await Db.OpenAsync();
        return (await cn.QueryAsync<string>(@"
            SELECT f.FormRuleName FROM dbo.UserGroupFormRules r JOIN dbo.FormRules f ON f.FormRuleID = r.FormRuleID
            WHERE r.UserGroupID = @roleId AND ISNULL(r.IsDeleted,0) = 0 AND ISNULL(f.IsDeleted,0) = 0", new { roleId })).ToList();
    }

    public static async Task UpdatePasswordAsync(int userProfileId, string hash)
    {
        await using var cn = await Db.OpenAsync();
        await cn.ExecuteAsync("UPDATE dbo.UserProfile SET Password = @hash, DateModified = GETDATE() WHERE UserProfileId = @userProfileId", new { hash, userProfileId });
    }

    public static async Task<IReadOnlyList<UserAccount>> ListAsync()
    {
        await using var cn = await Db.OpenAsync();
        return (await cn.QueryAsync<UserAccount>(UserSelect + " WHERE ISNULL(u.IsDeleted,0) = 0 ORDER BY u.FullName")).ToList();
    }

    public static async Task<int> SaveAsync(UserAccount u, string? newPasswordHash, int byUser)
    {
        await using var cn = await Db.OpenAsync();
        var dup = await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.UserProfile WHERE userId = @userId AND UserProfileId <> @id AND ISNULL(IsDeleted,0) = 0",
            new { u.userId, id = u.UserProfileId });
        if (dup > 0) throw new Domain.Common.AppException($"User name '{u.userId}' already exists.");

        if (u.UserProfileId == 0)
        {
            return await cn.ExecuteScalarAsync<int>(@"
                INSERT dbo.UserProfile(userId, Password, FullName, UserGroupID, IsSystemAccessible, IsDeleted, IsManger, DefaultLocationID, EmployeId, DateCreated, CreatedBy)
                VALUES(@userId, @pwd, @FullName, @UserGroupID, @IsSystemAccessible, 0, 0, @DefaultLocationID, @EmployeId, GETDATE(), @byUser);
                SELECT CAST(SCOPE_IDENTITY() AS INT);",
                new { u.userId, pwd = newPasswordHash, u.FullName, u.UserGroupID, u.IsSystemAccessible, u.DefaultLocationID, u.EmployeId, byUser });
        }
        await cn.ExecuteAsync(@"
            UPDATE dbo.UserProfile SET userId = @userId, FullName = @FullName, UserGroupID = @UserGroupID,
                   IsSystemAccessible = @IsSystemAccessible, DefaultLocationID = @DefaultLocationID, EmployeId = @EmployeId,
                   IsDeleted = @IsDeleted, DateModified = GETDATE(), ModifiedBy = @byUser,
                   Password = ISNULL(@pwd, Password)
            WHERE UserProfileId = @UserProfileId",
            new { u.userId, u.FullName, u.UserGroupID, u.IsSystemAccessible, u.DefaultLocationID, u.EmployeId, u.IsDeleted, byUser, pwd = newPasswordHash, u.UserProfileId });
        return u.UserProfileId;
    }

    public static async Task<IReadOnlyList<Role>> ListRolesAsync()
    {
        await using var cn = await Db.OpenAsync();
        return (await cn.QueryAsync<Role>(@"
            SELECT UserGroupID, UserGroupName, CAST(ISNULL(IsSuperUser,0) AS BIT) AS IsSuperUser, ISNULL(MaxDiscountPercent,0) AS MaxDiscountPercent
            FROM dbo.UserGroups WHERE ISNULL(IsDeleted,0) = 0 ORDER BY UserGroupName")).ToList();
    }

    public static async Task<int> SaveRoleAsync(Role r, int byUser)
    {
        await using var cn = await Db.OpenAsync();
        if (r.UserGroupID == 0)
            return await cn.ExecuteScalarAsync<int>(@"
                INSERT dbo.UserGroups(UserGroupName, IsSuperUser, IsSuperAdmin, IsAutoLogout, MaxDiscountPercent, DateCreated, CreatedBy, IsDeleted)
                VALUES(@UserGroupName, @IsSuperUser, 0, 0, @MaxDiscountPercent, GETDATE(), @byUser, 0);
                SELECT CAST(SCOPE_IDENTITY() AS INT);", new { r.UserGroupName, r.IsSuperUser, r.MaxDiscountPercent, byUser });
        await cn.ExecuteAsync(@"UPDATE dbo.UserGroups SET UserGroupName = @UserGroupName, IsSuperUser = @IsSuperUser,
                MaxDiscountPercent = @MaxDiscountPercent, DateModified = GETDATE(), ModifiedBy = @byUser WHERE UserGroupID = @UserGroupID",
            new { r.UserGroupName, r.IsSuperUser, r.MaxDiscountPercent, byUser, r.UserGroupID });
        return r.UserGroupID;
    }

    public static async Task<IReadOnlyList<Permission>> ListPermissionsAsync()
    {
        await using var cn = await Db.OpenAsync();
        return (await cn.QueryAsync<Permission>(@"
            SELECT FormRuleID, FormRuleName, FormName, Description FROM dbo.FormRules
            WHERE ISNULL(IsDeleted,0) = 0 AND FormRuleName LIKE '%.%' ORDER BY FormName, FormRuleID")).ToList();
    }

    public static async Task<IReadOnlyList<int>> GetRolePermissionIdsAsync(int roleId)
    {
        await using var cn = await Db.OpenAsync();
        return (await cn.QueryAsync<int>("SELECT FormRuleID FROM dbo.UserGroupFormRules WHERE UserGroupID = @roleId AND ISNULL(IsDeleted,0) = 0", new { roleId })).ToList();
    }

    public static async Task SetRolePermissionsAsync(int roleId, IEnumerable<int> permissionIds, int byUser)
    {
        var ids = permissionIds.Distinct().ToList();
        await Db.InTransactionAsync(async (cn, tx) =>
        {
            await cn.ExecuteAsync("DELETE FROM dbo.UserGroupFormRules WHERE UserGroupID = @roleId", new { roleId }, tx);
            foreach (var id in ids)
                await cn.ExecuteAsync(@"INSERT dbo.UserGroupFormRules(UserGroupID, FormRuleID, DateCreated, CreatedBy, IsDeleted)
                                        VALUES(@roleId, @id, GETDATE(), @byUser, 0)", new { roleId, id, byUser }, tx);
            return 0;
        });
    }

    public static async Task WriteAuditAsync(int? userId, int? approvedBy, int? terminalId, string action, string? refNo, string? details)
    {
        try
        {
            await using var cn = await Db.OpenAsync();
            await cn.ExecuteAsync(@"INSERT dbo.AuditLogs(UserID, ApprovedBy, PCCreated, Action, RefNo, Details)
                                    VALUES(@userId, @approvedBy, @terminalId, @action, @refNo, @details)",
                new { userId, approvedBy, terminalId, action, refNo, details });
        }
        catch { /* audit must never block billing */ }
    }
}
