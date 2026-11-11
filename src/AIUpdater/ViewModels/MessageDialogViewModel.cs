using CommunityToolkit.Mvvm.Input;
using KeepShell.Services.Modal;

namespace AIUpdater.ViewModels;

public sealed class MessageDialogViewModel : IDialogViewModel, ICancelableDialog, IAcceptableDialog
{
    public MessageDialogViewModel(string title, string message, bool confirmation, string acceptText = "Подтвердить")
    {
        Title = title;
        Message = message;
        IsConfirmation = confirmation;
        AcceptText = confirmation ? acceptText : "ОК";
        AcceptCommand = new RelayCommand(() => RequestClose?.Invoke(this, true));
        CancelCommand = new RelayCommand(() => RequestClose?.Invoke(this, false));
    }

    public string Title { get; }
    public string Message { get; }
    public bool IsConfirmation { get; }
    public bool CanCancel => true;
    public string AcceptText { get; }
    public IRelayCommand AcceptCommand { get; }
    public IRelayCommand CancelCommand { get; }
    public event EventHandler<bool>? RequestClose;
    public bool TryAccept()
    {
        if (IsConfirmation) CancelCommand.Execute(null);
        else AcceptCommand.Execute(null);
        return true;
    }
}
