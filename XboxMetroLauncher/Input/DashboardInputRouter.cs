using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace XboxMetroLauncher.Input;

public static class DashboardInputRouter
{
	public static bool TryMapKey(KeyEventArgs args, out DashboardInputAction action)
	{
		action = DashboardInputAction.MoveLeft;

		if (IsTextEditingKey(args))
		{
			return false;
		}

		if (!TryMapKey(args.Key, out action))
		{
			return false;
		}

		return true;
	}

	public static bool MoveFocus(DashboardInputAction action)
	{
		FocusNavigationDirection? direction = action switch
		{
			DashboardInputAction.MoveLeft => FocusNavigationDirection.Left,
			DashboardInputAction.MoveRight => FocusNavigationDirection.Right,
			DashboardInputAction.MoveUp => FocusNavigationDirection.Up,
			DashboardInputAction.MoveDown => FocusNavigationDirection.Down,
			_ => null,
		};

		if (!direction.HasValue || Keyboard.FocusedElement is not UIElement focusedElement)
		{
			return false;
		}

		try
		{
			return focusedElement.MoveFocus(new TraversalRequest(direction.Value));
		}
		catch (InvalidOperationException)
		{
			return false;
		}
		catch (ArgumentException)
		{
			return false;
		}
		catch
		{
			return false;
		}
	}

	public static bool ActivateFocusedElement()
	{
		if (Keyboard.FocusedElement is Button button)
		{
			ICommand command = button.Command;
			if (command != null && command.CanExecute(button.CommandParameter))
			{
				try
				{
					button.Command.Execute(button.CommandParameter);
					return true;
				}
				catch
				{
					return false;
				}
			}
		}

		if (Keyboard.FocusedElement is CheckBox checkBox)
		{
			checkBox.IsChecked = checkBox.IsChecked != true;
			return true;
		}

		if (Keyboard.FocusedElement is ComboBox comboBox)
		{
			comboBox.IsDropDownOpen = !comboBox.IsDropDownOpen;
			return true;
		}

		if (Keyboard.FocusedElement is Slider slider)
		{
			slider.Value = Math.Clamp(slider.Value + slider.SmallChange, slider.Minimum, slider.Maximum);
			return true;
		}

		if (Keyboard.FocusedElement is TextBox textBox)
		{
			textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
			return true;
		}

		return false;
	}

	public static bool TryAdjustFocusedSetting(DashboardInputAction action)
	{
		if (action is not (DashboardInputAction.MoveLeft or DashboardInputAction.MoveRight))
		{
			return false;
		}

		if (Keyboard.FocusedElement is Slider slider)
		{
			double delta = action == DashboardInputAction.MoveRight ? slider.SmallChange : -slider.SmallChange;
			slider.Value = Math.Clamp(slider.Value + delta, slider.Minimum, slider.Maximum);
			return true;
		}

		if (Keyboard.FocusedElement is ComboBox comboBox)
		{
			if (comboBox.Items.Count == 0)
			{
				return false;
			}

			int delta = action == DashboardInputAction.MoveRight ? 1 : -1;
			int selectedIndex = Math.Clamp(comboBox.SelectedIndex + delta, 0, comboBox.Items.Count - 1);
			if (selectedIndex == comboBox.SelectedIndex)
			{
				return true;
			}

			comboBox.SelectedIndex = selectedIndex;
			return true;
		}

		return false;
	}

	private static bool TryMapKey(Key key, out DashboardInputAction action)
	{
		action = key switch
		{
			Key.Left or Key.A => DashboardInputAction.MoveLeft,
			Key.Right or Key.D => DashboardInputAction.MoveRight,
			Key.Up or Key.W => DashboardInputAction.MoveUp,
			Key.Down or Key.S => DashboardInputAction.MoveDown,
			Key.Return or Key.Space => DashboardInputAction.Activate,
			Key.Escape or Key.Back => DashboardInputAction.Back,
			Key.X => DashboardInputAction.Details,
			Key.Y or Key.F => DashboardInputAction.Search,
			Key.Q or Key.PageUp => DashboardInputAction.PreviousTab,
			Key.E or Key.PageDown => DashboardInputAction.NextTab,
			Key.F10 => DashboardInputAction.Options,
			_ => DashboardInputAction.MoveLeft,
		};

		return key is Key.Left or Key.A
			or Key.Right or Key.D
			or Key.Up or Key.W
			or Key.Down or Key.S
			or Key.Return or Key.Space
			or Key.Escape or Key.Back
			or Key.X or Key.Y or Key.F
			or Key.Q or Key.PageUp
			or Key.E or Key.PageDown
			or Key.F10;
	}

	private static bool IsTextEditingKey(KeyEventArgs args)
	{
		if (Keyboard.FocusedElement is not TextBox)
		{
			return false;
		}

		return args.Key is not (Key.Return or Key.Escape or Key.PageUp or Key.PageDown);
	}
}
