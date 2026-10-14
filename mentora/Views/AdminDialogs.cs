using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace mentora.Views;

public static class AdminDialogs
{
    private static Window CreateDialog(string title)
    {
        return new Window
        {
            Title = title,
            Width = 460,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brushes.White
        };
    }

    private static Button CreateButton(string text, bool primary, bool danger)
    {
        var button = new Button
        {
            Content = text,
            Height = 44,
            CornerRadius = new CornerRadius(6),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };

        if (primary)
        {
            button.Background = Brush.Parse(danger ? "#B52C2C" : "#5B4BFF");
            button.Foreground = Brushes.White;
            button.BorderThickness = new Thickness(0);
            button.FontWeight = FontWeight.SemiBold;
        }
        else
        {
            button.Background = Brushes.White;
            button.Foreground = Brush.Parse("#39446B");
            button.BorderBrush = Brush.Parse("#DDE0E8");
            button.BorderThickness = new Thickness(1);
        }

        return button;
    }

    private static TextBlock CreateTitle(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = 21,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brush.Parse("#19234D"),
            TextWrapping = TextWrapping.Wrap
        };
    }

    private static TextBlock CreateText(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = 14,
            Foreground = Brush.Parse("#747C90"),
            TextWrapping = TextWrapping.Wrap
        };
    }

    public static async Task<bool> ConfirmAsync(
        Window owner,
        string title,
        string text,
        string confirmText,
        bool danger)
    {
        var dialog = CreateDialog(title);

        var layout = new StackPanel
        {
            Margin = new Thickness(26),
            Spacing = 16
        };

        layout.Children.Add(CreateTitle(title));
        layout.Children.Add(CreateText(text));

        var buttons = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,12,*")
        };

        var cancelButton = CreateButton("Отмена", false, false);
        var confirmButton = CreateButton(confirmText, true, danger);

        cancelButton.Click += (_, _) => dialog.Close(false);
        confirmButton.Click += (_, _) => dialog.Close(true);

        Grid.SetColumn(cancelButton, 0);
        Grid.SetColumn(confirmButton, 2);

        buttons.Children.Add(cancelButton);
        buttons.Children.Add(confirmButton);

        layout.Children.Add(buttons);

        dialog.Content = layout;

        return await dialog.ShowDialog<bool>(owner);
    }

    public static async Task<string?> AskReasonAsync(
        Window owner,
        string title,
        string text,
        string confirmText,
        bool danger)
    {
        var dialog = CreateDialog(title);

        var layout = new StackPanel
        {
            Margin = new Thickness(26),
            Spacing = 14
        };

        layout.Children.Add(CreateTitle(title));
        layout.Children.Add(CreateText(text));

        var reasonBox = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 100,
            MaxLength = 1000,
            Watermark = "Причина (от 5 до 1000 символов)"
        };

        var errorText = new TextBlock
        {
            Foreground = Brush.Parse("#B52C2C"),
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false
        };

        layout.Children.Add(reasonBox);
        layout.Children.Add(errorText);

        var buttons = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,12,*"),
            Margin = new Thickness(0, 4, 0, 0)
        };

        var cancelButton = CreateButton("Отмена", false, false);
        var confirmButton = CreateButton(confirmText, true, danger);

        cancelButton.Click += (_, _) => dialog.Close(null);

        confirmButton.Click += (_, _) =>
        {
            string reason = reasonBox.Text?.Trim() ?? "";

            if (reason.Length < 5 || reason.Length > 1000)
            {
                errorText.Text = "Причина должна содержать от 5 до 1000 символов.";
                errorText.IsVisible = true;
                return;
            }

            dialog.Close(reason);
        };

        Grid.SetColumn(cancelButton, 0);
        Grid.SetColumn(confirmButton, 2);

        buttons.Children.Add(cancelButton);
        buttons.Children.Add(confirmButton);

        layout.Children.Add(buttons);

        dialog.Content = layout;

        return await dialog.ShowDialog<string?>(owner);
    }
}