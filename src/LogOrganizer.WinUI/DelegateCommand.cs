using System.Windows.Input;

namespace LogOrganizer.WinUI;

internal sealed class DelegateCommand(Action execute) : ICommand
{
    public event EventHandler? CanExecuteChanged
    {
        add { }
        remove { }
    }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => execute();
}
