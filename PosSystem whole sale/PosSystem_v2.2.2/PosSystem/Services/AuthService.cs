namespace PosSystem.Services;

public sealed record LoginResult(bool Success, string? Error, bool NeedsTerminalRegistration);

/// <summary>Login, till registration, supervisor approval and password change.</summary>
public static class AuthService
{
    /// <summary>This computer's name. Tills are registered by computer name.</summary>
    public static string MachineCode { get; } = Environment.MachineName.ToUpperInvariant();

    public static async Task<LoginResult> LoginAsync(string userName, string password)
    {
        if (string.IsNullOrWhiteSpace(userName)) return new(false, "Enter user name.", false);

        var user = await UserRepository.GetByLoginAsync(userName.Trim());
        if (user == null || user.IsDeleted || !user.IsSystemAccessible)
            return new(false, "Invalid user name or password.", false);

        if (!PasswordHasher.Verify(password, user.Password, out bool upgrade))
            return new(false, "Invalid user name or password.", false);

        if (upgrade) // old MD5 password -> PBKDF2 automatically
            await UserRepository.UpdatePasswordAsync(user.UserProfileId, PasswordHasher.Hash(password));

        var permissions = await UserRepository.GetPermissionKeysAsync(user.UserGroupID);
        Session.SignIn(user, permissions);
        await SettingsService.LoadAsync();

        var terminal = await TerminalRepository.GetByMachineAsync(MachineCode);
        if (terminal == null) return new(true, null, true);

        Session.SetTerminal(terminal);
        await AuditAsync("LOGIN", null, null);
        return new(true, null, false);
    }

    public static async Task RegisterTerminalAsync(string description, int locationId)
    {
        var t = new Terminal { MachineCode = MachineCode, Description = description, LocationID = locationId };
        t.PcRegistrationID = await TerminalRepository.SaveAsync(t, Session.UserId);
        Session.SetTerminal(t);
    }

    /// <summary>Supervisor approval: returns the approver when the login is valid and has the permission.</summary>
    public static async Task<UserAccount?> VerifyOverrideAsync(string userName, string password, string permission)
    {
        var user = await UserRepository.GetByLoginAsync(userName.Trim());
        if (user == null || user.IsDeleted || !user.IsSystemAccessible) return null;
        if (!PasswordHasher.Verify(password, user.Password, out _)) return null;
        if (user.IsSuperUser) return user;
        var permissions = await UserRepository.GetPermissionKeysAsync(user.UserGroupID);
        return permissions.Contains(permission, StringComparer.OrdinalIgnoreCase) ? user : null;
    }

    public static async Task<string?> ChangePasswordAsync(string oldPassword, string newPassword)
    {
        var user = await UserRepository.GetByLoginAsync(Session.UserName);
        if (user == null || !PasswordHasher.Verify(oldPassword, user.Password, out _)) return "Current password is incorrect.";
        if (newPassword.Length < 4) return "New password must have at least 4 characters.";
        await UserRepository.UpdatePasswordAsync(user.UserProfileId, PasswordHasher.Hash(newPassword));
        return null;
    }

    public static Task AuditAsync(string action, string? refNo, string? details, int? approvedBy = null) =>
        UserRepository.WriteAuditAsync(Session.UserId, approvedBy, Session.TerminalId, action, refNo, details);

    public static void Logout() => Session.SignOut();
}
