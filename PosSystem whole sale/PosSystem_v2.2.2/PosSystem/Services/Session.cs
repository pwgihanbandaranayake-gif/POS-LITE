namespace PosSystem.Services;

/// <summary>
/// Everything about the current login, in one place:
/// who is logged in, which till (terminal), which location, the open shift and the shop settings.
/// </summary>
public static class Session
{
    // ---- user ----
    public static int UserId { get; private set; }
    public static string UserName { get; private set; } = "";
    public static string FullName { get; private set; } = "";
    public static bool IsSuperUser { get; private set; }
    public static decimal MaxDiscountPercent { get; private set; }
    private static HashSet<string> _permissions = new(StringComparer.OrdinalIgnoreCase);

    // ---- till & location ----
    public static int TerminalId { get; private set; }
    public static string TerminalName { get; private set; } = "";
    public static int LocationId { get; private set; } = 1;
    public static string LocationCode { get; private set; } = "MAIN";
    public static string LocationName { get; private set; } = "";

    /// <summary>Open cashier shift (SalesSessions.SalesSessionID), null when no shift is open.</summary>
    public static int? ShiftId { get; set; }

    // ---- configuration loaded at login ----
    public static PosSettings Settings { get; set; } = new();
    public static Terminal Terminal { get; set; } = new();
    public static IReadOnlyList<Location> Locations { get; set; } = Array.Empty<Location>();
    public static IReadOnlyList<PaymentMethod> PaymentMethods { get; set; } = Array.Empty<PaymentMethod>();

    /// <summary>True when the user is an administrator or the role has this permission.</summary>
    public static bool Has(string permission) => IsSuperUser || _permissions.Contains(permission);

    public static void SignIn(UserAccount user, IEnumerable<string> permissions)
    {
        UserId = user.UserProfileId;
        UserName = user.userId ?? "";
        FullName = user.FullName ?? UserName;
        IsSuperUser = user.IsSuperUser;
        MaxDiscountPercent = user.MaxDiscountPercent;
        _permissions = new HashSet<string>(permissions, StringComparer.OrdinalIgnoreCase);
    }

    public static void SetTerminal(Terminal t)
    {
        Terminal = t;
        TerminalId = t.PcRegistrationID;
        TerminalName = t.Description;
        var loc = Locations.FirstOrDefault(l => l.LocationID == t.LocationID) ?? Locations.FirstOrDefault();
        if (loc != null) SetLocation(loc.LocationID, loc.LocationCode, loc.LocationName);
    }

    public static void SetLocation(int id, string code, string name)
    {
        LocationId = id; LocationCode = code; LocationName = name;
    }

    public static void SignOut()
    {
        UserId = 0; UserName = FullName = ""; IsSuperUser = false; ShiftId = null;
        _permissions.Clear();
    }
}
