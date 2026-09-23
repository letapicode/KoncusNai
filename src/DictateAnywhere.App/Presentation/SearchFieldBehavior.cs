using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DictateAnywhere.App.Presentation;

/// <summary>Clears a search without moving keyboard focus away from the query.</summary>
public static class SearchFieldBehavior
{
  public static readonly RoutedUICommand ClearCommand = new("Clear search", "ClearSearch", typeof(SearchFieldBehavior));

  public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
    "IsEnabled", typeof(bool), typeof(SearchFieldBehavior), new PropertyMetadata(false, OnIsEnabledChanged));

  public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

  public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

  private static void OnIsEnabledChanged(DependencyObject element, DependencyPropertyChangedEventArgs args)
  {
    if (element is not TextBox field)
    {
      return;
    }

    if ((bool)args.NewValue)
    {
      field.CommandBindings.Add(new CommandBinding(ClearCommand, OnClear, CanClear));
    }
    else
    {
      for (int index = field.CommandBindings.Count - 1; index >= 0; index--)
      {
        if (field.CommandBindings[index].Command == ClearCommand)
        {
          field.CommandBindings.RemoveAt(index);
        }
      }
    }
  }

  private static void CanClear(object sender, CanExecuteRoutedEventArgs args)
  {
    args.CanExecute = sender is TextBox { IsEnabled: true, IsReadOnly: false, Text.Length: > 0 };
    args.Handled = true;
  }

  private static void OnClear(object sender, ExecutedRoutedEventArgs args)
  {
    if (sender is TextBox field)
    {
      field.Focus();
      field.Clear();
      args.Handled = true;
    }
  }
}
