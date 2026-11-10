namespace PosSystem.Services;

/// <summary>Loads and saves shop settings (AppSettings table) and this till's settings (PcRegistration).</summary>
public static class SettingsService
{
    public static async Task LoadAsync()
    {
        Session.Settings = PosSettings.FromDictionary(await SettingsRepository.GetAllAsync());
        Session.Locations = (await LookupRepository.GetLocationsAsync()).Where(l => l.IsActive).ToList();
        Session.PaymentMethods = (await LookupRepository.GetPaymentMethodsAsync()).Where(p => p.IsActive).OrderBy(p => p.SortOrder).ToList();
    }

    public static async Task SaveSettingsAsync(PosSettings settings)
    {
        await SettingsRepository.SaveAsync(settings.ToDictionary());
        Session.Settings = settings;
    }

    public static async Task SaveTerminalAsync(Terminal terminal)
    {
        await TerminalRepository.SaveAsync(terminal, Session.UserId);
        Session.Terminal = terminal;
    }
}
