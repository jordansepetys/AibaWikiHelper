using System.Windows;
using System.Windows.Controls;

namespace AIWikiHelper
{
    // A simple dialog window for getting user input
    public class InputDialog : Window
    {
        public string ResponseText { get; private set; }
        private TextBox _textBox;

        public InputDialog(string question)
        {
            Title = question;
            Width = 300;
            Height = 150;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var panel = new DockPanel { Margin = new Thickness(10) };

            var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };

            DockPanel.SetDock(buttonPanel, Dock.Bottom);

            var okButton = new Button { Content = "OK", IsDefault = true, Width = 75, Margin = new Thickness(5) };
            okButton.Click += (s, e) => { ResponseText = _textBox.Text; DialogResult = true; };
            var cancelButton = new Button { Content = "Cancel", IsCancel = true, Width = 75, Margin = new Thickness(5) };

            buttonPanel.Children.Add(okButton);
            buttonPanel.Children.Add(cancelButton);

            _textBox = new TextBox();
            panel.Children.Add(buttonPanel);
            panel.Children.Add(_textBox);

            Content = panel;
            _textBox.Focus();
        }
    }
}