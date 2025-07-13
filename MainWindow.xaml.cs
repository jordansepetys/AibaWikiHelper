using AIWikiHelper.ViewModels;
using Markdig;
using Microsoft.Web.WebView2.Core;  // For CoreWebView2InitializationCompletedEventArgs
using System.ComponentModel;
using System.IO;  // Add this for Path and File
using System.Windows;
using System.Windows.Controls;

namespace AIWikiHelper
{
    public partial class MainWindow : Window
    {
        private MainViewModel _viewModel;

        public MainWindow()
        {
            InitializeComponent();

            _viewModel = new MainViewModel();
            DataContext = _viewModel;

            // Update WebView when WikiContent changes (with safeguards)
            _viewModel.PropertyChanged += ViewModel_PropertyChanged;

            // Hook up init completed event for safer navigation
            WebView.CoreWebView2InitializationCompleted += WebView_CoreWebView2InitializationCompleted;
        }

        private void WebView_CoreWebView2InitializationCompleted(object sender, CoreWebView2InitializationCompletedEventArgs e)
        {
            if (e.IsSuccess)
            {
                // Optional: Set default settings if needed, e.g., WebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                UpdateWebViewContent();  // Navigate once init completes
            }
            else
            {
                MessageBox.Show($"WebView2 init failed: {e.InitializationException?.Message}");
            }
        }

        private void ViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(_viewModel.WikiContent))
            {
                UpdateWebViewContent();
            }
        }

        private async void UpdateWebViewContent()
        {
            if (WebView?.CoreWebView2 == null)
            {
                return;  // Queue for after init
            }

            try
            {
                var htmlFragment = Markdown.ToHtml(_viewModel.WikiContent ?? string.Empty);

                // Wrap in full HTML for reliable rendering
                var fullHtml = $@"
                <!DOCTYPE html>
                <html lang=""en"">
                <head>
                    <meta charset=""UTF-8"">
                    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
                    <title>Wiki Preview</title>
                    <style>
                        body {{ font-family: Arial, sans-serif; margin: 20px; background-color: white; color: black; }}
                        h1, h2, h3 {{ color: #333; }}
                    </style>
                </head>
                <body>
                    {htmlFragment}
                </body>
                </html>";

                // Save to temp file as workaround for NavigateToString issues
                string tempFilePath = Path.GetTempFileName() + ".html";
                await File.WriteAllTextAsync(tempFilePath, fullHtml);

                // Navigate to local file (file:/// path)
                WebView.CoreWebView2.Navigate(new Uri(tempFilePath).AbsoluteUri);

                // Clean up temp file after navigation (delay to ensure load)
                WebView.CoreWebView2.NavigationCompleted += (s, e) => File.Delete(tempFilePath);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error updating WebView: {ex.Message}");
            }
        }

        private async void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0 && (e.AddedItems[0] as TabItem)?.Header.ToString() == "View")
            {
                try
                {
                    if (WebView.CoreWebView2 == null)
                    {
                        await WebView.EnsureCoreWebView2Async(null);
                    }
                    UpdateWebViewContent();  // Call the shared update method
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error initializing WebView: {ex.Message}");
                }
            }
        }
    }
}