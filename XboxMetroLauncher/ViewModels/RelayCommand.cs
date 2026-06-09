using System;
using System.Windows.Input;

namespace XboxMetroLauncher.ViewModels;

public sealed class RelayCommand : ICommand
{
	private readonly Action<object?> _execute;

	private readonly Predicate<object?>? _canExecute;

	public event EventHandler? CanExecuteChanged;

	public RelayCommand(Action execute)
		: this(delegate
		{
			execute();
		}, null)
	{
	}

	public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
	{
		_execute = execute;
		_canExecute = canExecute;
	}

	public bool CanExecute(object? parameter)
	{
		return _canExecute?.Invoke(parameter) ?? true;
	}

	public void Execute(object? parameter)
	{
		_execute(parameter);
	}

	public void RaiseCanExecuteChanged()
	{
		this.CanExecuteChanged?.Invoke(this, EventArgs.Empty);
	}
}
