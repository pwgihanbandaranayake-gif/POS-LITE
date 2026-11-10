namespace PosSystem.ViewModels;

public abstract partial class ViewModelBase : ObservableObject
{
    [ObservableProperty] private bool _isBusy;

    /// <summary>Runs async work with busy flag and friendly error messages (no crash, no raw stack traces for cashiers).</summary>
    protected async Task<bool> RunAsync(Func<Task> work)
    {
        try
        {
            IsBusy = true;
            await work();
            return true;
        }
        catch (AppException ex) { Dialogs.Error(ex.Message); }
        catch (SqlException ex) { Dialogs.Error("Database error: " + ex.Message, "Database"); }
        catch (Exception ex) { Dialogs.Error(ex.Message, "Unexpected error"); }
        finally { IsBusy = false; }
        return false;
    }

    /// <summary>Called by the shell when the page is shown.</summary>
    public virtual Task LoadAsync() => Task.CompletedTask;
}

public abstract partial class DialogViewModel : ViewModelBase
{
    public event Action<bool?>? CloseRequested;
    public string Title { get; protected set; } = "";

    protected void Close(bool? result) => CloseRequested?.Invoke(result);

    [RelayCommand]
    protected virtual void Cancel() => Close(false);
}

public enum MessageKind { Info, Error, Confirm }

public sealed partial class MessageDialogViewModel : DialogViewModel
{
    public MessageDialogViewModel(string title, string message, MessageKind kind)
    {
        Title = title; Message = message; Kind = kind;
    }
    public string Message { get; }
    public MessageKind Kind { get; }
    public bool IsConfirm => Kind == MessageKind.Confirm;
    public bool IsError => Kind == MessageKind.Error;
    public string OkText => IsConfirm ? "Yes" : "OK";

    [RelayCommand] private void Ok() => Close(true);
}

public sealed partial class TextInputViewModel : DialogViewModel
{
    public TextInputViewModel(string title, string label, string? initial, bool numeric)
    {
        Title = title; Label = label; _value = initial ?? ""; Numeric = numeric;
    }
    public string Label { get; }
    public bool Numeric { get; }
    [ObservableProperty] private string _value;

    [RelayCommand]
    private void Ok()
    {
        if (Numeric && !decimal.TryParse(Value, out _)) { Dialogs.Error("Enter a valid number."); return; }
        Close(true);
    }
}
