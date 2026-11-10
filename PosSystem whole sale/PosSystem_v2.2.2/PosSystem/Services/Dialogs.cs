using System.Windows;
using PosSystem.Views;

namespace PosSystem.Services;

/// <summary>
/// All pop-up windows. A dialog is a ViewModel shown inside DialogWindow;
/// App.xaml maps each dialog ViewModel to its view.
/// </summary>
public static class Dialogs
{
    public static bool? Show(DialogViewModel vm)
    {
        var w = new DialogWindow { DataContext = vm, Title = Lang.T(vm.Title), Owner = ActiveWindow() };
        void Close(bool? r)
        {
            try { w.DialogResult = r; } catch (InvalidOperationException) { }
            w.Close();
        }
        vm.CloseRequested += Close;
        try { return w.ShowDialog(); }
        finally { vm.CloseRequested -= Close; }
    }

    public static void Info(string message, string title = "Information") =>
        Show(new MessageDialogViewModel(Lang.T(title), Lang.T(message), MessageKind.Info));

    public static void Error(string message, string title = "Something needs attention") =>
        Show(new MessageDialogViewModel(Lang.T(title), Lang.T(message), MessageKind.Error));

    public static bool Confirm(string message, string title = "Please confirm") =>
        Show(new MessageDialogViewModel(Lang.T(title), Lang.T(message), MessageKind.Confirm)) == true;

    /// <summary>Asks for one value. Returns null when cancelled.</summary>
    public static string? Prompt(string title, string label, string? initial = null, bool numeric = false)
    {
        var vm = new TextInputViewModel(Lang.T(title), Lang.T(label), initial, numeric);
        return Show(vm) == true ? vm.Value : null;
    }

    private static Window? ActiveWindow() =>
        System.Windows.Application.Current?.Windows.OfType<Window>().FirstOrDefault(x => x.IsActive)
        ?? System.Windows.Application.Current?.MainWindow;
}

/// <summary>
/// Permission check with supervisor approval.
/// Returns the approving user id (the cashier when they have the permission), or null when refused.
/// </summary>
public static class OverrideService
{
    public static async Task<int?> AuthorizeAsync(string permission, string action)
    {
        if (Session.Has(permission)) return Session.UserId;
        var vm = new ManagerOverrideViewModel(permission, action);
        if (Dialogs.Show(vm) != true) return null;
        await AuthService.AuditAsync("OVERRIDE", null, action + " [" + permission + "]", vm.ApprovedBy);
        return vm.ApprovedBy;
    }
}
