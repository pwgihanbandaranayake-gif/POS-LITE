namespace PosSystem.ViewModels;

/// <summary>Supervisor approval when the cashier lacks a permission (void, discount, price change, cancel...).</summary>
public sealed partial class ManagerOverrideViewModel : DialogViewModel
{
    private readonly string _permission;

    public ManagerOverrideViewModel(string permission, string action)
    {
        _permission = permission; Action = action; Title = "Supervisor approval";
    }

    public string Action { get; }
    [ObservableProperty] private string _userName = "";
    public string Password { get; set; } = "";
    public int ApprovedBy { get; private set; }
    [ObservableProperty] private string? _error;

    [RelayCommand]
    private async Task Approve()
    {
        await RunAsync(async () =>
        {
            var acc = await AuthService.VerifyOverrideAsync(UserName, Password, _permission);
            if (acc == null) { Error = "Not authorised. Check the user name, password and permission."; return; }
            ApprovedBy = acc.UserProfileId;
            Close(true);
        });
    }
}

public sealed partial class TerminalSetupViewModel : DialogViewModel
{
    public TerminalSetupViewModel()
    {
        Title = "Register this computer";
        MachineCode = AuthService.MachineCode;
        Locations = Session.Locations;
        SelectedLocation = Locations.FirstOrDefault();
        _description = "Counter " + MachineCode;
    }
    public string MachineCode { get; }
    public IReadOnlyList<Location> Locations { get; }
    [ObservableProperty] private Location? _selectedLocation;
    [ObservableProperty] private string _description;

    [RelayCommand]
    private async Task Save()
    {
        if (SelectedLocation == null || string.IsNullOrWhiteSpace(Description)) { Dialogs.Error("Enter a name and select a location."); return; }
        if (await RunAsync(() => AuthService.RegisterTerminalAsync(Description.Trim(), SelectedLocation.LocationID))) Close(true);
    }
}

public sealed partial class OpenSessionViewModel : DialogViewModel
{
    public OpenSessionViewModel() { Title = "Start cashier session"; }
    [ObservableProperty] private string _openingFloat = "0";
    public decimal Amount => decimal.TryParse(OpeningFloat, out var d) ? d : 0;

    [RelayCommand]
    private void Start()
    {
        if (!decimal.TryParse(OpeningFloat, out var d) || d < 0) { Dialogs.Error("Enter the opening cash float (0 if none)."); return; }
        Close(true);
    }
}

public sealed partial class ChangePasswordViewModel : DialogViewModel
{
    public ChangePasswordViewModel() { Title = "Change my password"; }
    public string Current { get; set; } = "";
    public string New { get; set; } = "";
    public string Confirm { get; set; } = "";

    [RelayCommand]
    private async Task Save()
    {
        if (New != Confirm) { Dialogs.Error("New passwords do not match."); return; }
        await RunAsync(async () =>
        {
            var err = await AuthService.ChangePasswordAsync(Current, New);
            if (err != null) { Dialogs.Error(err); return; }
            Dialogs.Info("Password changed.");
            Close(true);
        });
    }
}
