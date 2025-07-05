using System.Windows;

namespace AIWikiHelper
{
    public partial class SuggestionDialog : Window
    {
        // This public property will let the MainWindow get the final text
        public string EditedSuggestion { get; private set; }

        public SuggestionDialog(string initialSuggestion)
        {
            InitializeComponent();
            // Load the AI's original suggestion into the textbox
            SuggestionTextBox.Text = initialSuggestion;
        }

        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            // Store the (potentially edited) text from the textbox
            EditedSuggestion = SuggestionTextBox.Text;
            // Set the dialog result to true so the MainWindow knows we clicked "Apply"
            this.DialogResult = true;
        }
    }
}