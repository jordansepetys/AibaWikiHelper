using Microsoft.Extensions.Configuration;
using Microsoft.Win32;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using Newtonsoft.Json;
using DocumentFormat.OpenXml.Packaging;
using System.Globalization;

namespace AIWikiHelper
{
    public partial class MainWindow : Window
    {
        private readonly string _wikiFolderPath;
        private readonly string _apiKey;
        private readonly string _apiUrl;
        private static readonly HttpClient client = new HttpClient();

        public MainWindow()
        {
            InitializeComponent();

            // Load configuration from appsettings.json
            var config = new ConfigurationBuilder()
                .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                .AddJsonFile("appsettings.json").Build();

            _apiKey = config.GetValue<string>("OpenAI:ApiKey");
            _apiUrl = config.GetValue<string>("OpenAI:ApiUrl");
            _wikiFolderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, config.GetValue<string>("AppSettings:WikiFolderPath"));

            // Initial setup
            EnsureWikiFolderExists();
            LoadProjectList();
            InitializeWebView();

            if (string.IsNullOrEmpty(_apiKey) || _apiKey == "YOUR_API_KEY_GOES_HERE")
            {
                MessageBox.Show("OpenAI API Key is not configured. Please set it in appsettings.json.", "Configuration Error", MessageBoxButton.OK, MessageBoxImage.Error);
                GenerateButton.IsEnabled = false;
            }
        }

        private void EnsureWikiFolderExists()
        {
            if (!Directory.Exists(_wikiFolderPath))
            {
                Directory.CreateDirectory(_wikiFolderPath);
            }
        }

        private void LoadProjectList()
        {
            ProjectList.Items.Clear();
            var wikiFiles = Directory.GetFiles(_wikiFolderPath, "*.md");
            foreach (var file in wikiFiles)
            {
                ProjectList.Items.Add(Path.GetFileNameWithoutExtension(file));
            }
        }

        private async void InitializeWebView()
        {
            await WebView.EnsureCoreWebView2Async(null);
        }

        private void UpdateWebView(string markdown)
        {
            var html = Markdig.Markdown.ToHtml(markdown);
            WebView.NavigateToString(html);
        }

        private void ProjectList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ProjectList.SelectedItem == null) return;

            string projectName = ProjectList.SelectedItem.ToString();
            string filePath = Path.Combine(_wikiFolderPath, $"{projectName}.md");

            if (File.Exists(filePath))
            {
                string content = File.ReadAllText(filePath);
                WikiEditor.Text = content;
                UpdateWebView(content);
            }
        }

        private void NewProjectButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new InputDialog("Enter new project name:");
            if (dialog.ShowDialog() == true)
            {
                string projectName = dialog.ResponseText;
                if (string.IsNullOrWhiteSpace(projectName)) return;

                string filePath = Path.Combine(_wikiFolderPath, $"{projectName}.md");
                if (File.Exists(filePath))
                {
                    MessageBox.Show("A project with this name already exists.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Create a basic template
                var template = new StringBuilder();
                template.AppendLine($"# Project: {projectName}");
                template.AppendLine();
                template.AppendLine("## Overview");
                template.AppendLine();
                template.AppendLine("## Goals");
                template.AppendLine();
                template.AppendLine("## Key Features");
                template.AppendLine();
                template.AppendLine("## Risks/Mitigations");
                template.AppendLine();
                template.AppendLine("## Daily Log");
                template.AppendLine();

                File.WriteAllText(filePath, template.ToString());
                LoadProjectList();
                ProjectList.SelectedItem = projectName;
            }
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            LoadProjectList();
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (ProjectList.SelectedItem == null)
            {
                MessageBox.Show("Please select a project to save.", "No Project Selected", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string projectName = ProjectList.SelectedItem.ToString();
            string filePath = Path.Combine(_wikiFolderPath, $"{projectName}.md");
            File.WriteAllText(filePath, WikiEditor.Text);
            UpdateWebView(WikiEditor.Text);

            MessageBox.Show($"{projectName} has been saved.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async void GenerateButton_Click(object sender, RoutedEventArgs e)
        {
            if (ProjectList.SelectedItem == null)
            {
                MessageBox.Show("Please select a project first.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (string.IsNullOrWhiteSpace(TranscriptInput.Text))
            {
                MessageBox.Show("Please paste a transcript into the AI Assistant input box.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            GenerateButton.IsEnabled = false;
            GenerateButton.Content = "Generating...";

            try
            {
                string projectName = ProjectList.SelectedItem.ToString();
                string transcript = TranscriptInput.Text;
                string targetSection = ((ComboBoxItem)TargetSectionCombo.SelectedItem).Content.ToString();
                string currentWikiContent = WikiEditor.Text;

                string prompt = BuildPrompt(projectName, transcript, targetSection, currentWikiContent);
                string suggestion = await GetAiSuggestion(prompt);

                if (string.IsNullOrWhiteSpace(suggestion))
                {
                    MessageBox.Show("AI returned an empty suggestion.", "No Suggestion", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var dialog = new SuggestionDialog(suggestion);

                // Show the dialog and wait for the user to close it.
                // ShowDialog() returns true if the user clicked "Apply".
                if (dialog.ShowDialog() == true)
                {
                    // Get the final, edited text from the dialog's public property
                    string finalSuggestion = dialog.EditedSuggestion;

                    // Apply the user's final version
                    ApplySuggestion(targetSection, finalSuggestion, currentWikiContent);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                GenerateButton.IsEnabled = true;
                GenerateButton.Content = "Generate Suggestion";
            }
        }

        private string BuildPrompt(string projectName, string transcript, string targetSection, string currentWikiContent)
        {
            var promptBuilder = new StringBuilder();
            promptBuilder.AppendLine($"You are an AI assistant helping to maintain a project wiki.");
            promptBuilder.AppendLine($"Project: {projectName}");

            if (targetSection == "Daily Log")
            {
                promptBuilder.AppendLine("\nYou are an AI assistant that helps users create clean daily log entries from raw, informal notes.");
                promptBuilder.AppendLine("Your task is to process the user's text below and extract the key takeaways or activities.");
                promptBuilder.AppendLine($"Format your output as a Markdown block starting with a level-3 header for today's date ({DateTime.Now:yyyy-MM-dd}), followed by a bulleted list of the key points.");
                promptBuilder.AppendLine("Even if the text is short, do your best to summarize the main point.");
                promptBuilder.AppendLine("If the text is truly empty or nonsensical (like 'asdfg'), then respond with 'No new log entries from this meeting.'");
                promptBuilder.AppendLine($"\nUser's Raw Notes:\n```\n{transcript}\n```");
            }
            else
            {
                string currentSectionContent = ExtractSectionContent(currentWikiContent, targetSection);
                promptBuilder.AppendLine($"Target Section to Update: {targetSection}");
                promptBuilder.AppendLine($"\nHere is the CURRENT content of the '{targetSection}' section:\n```\n{currentSectionContent}\n```");
                promptBuilder.AppendLine($"\nHere is the information from the recent meeting (Transcript):\n```\n{transcript}\n```");
                promptBuilder.AppendLine("\nInstructions:");
                promptBuilder.AppendLine($"Review the new information from the meeting. Suggest a complete, revised version of the ENTIRE '{targetSection}' section that integrates the new information.");
                promptBuilder.AppendLine("Output ONLY the complete, revised text for the content that should go *under* the header. Do NOT include the markdown header itself or any conversational preamble.");
            }

            return promptBuilder.ToString();
        }

        private async Task<string> GetAiSuggestion(string prompt)
        {
            var requestBody = new
            {
                model = "gpt-4o",
                messages = new[]
                {
                    new { role = "system", content = "You are a helpful assistant for project managers." },
                    new { role = "user", content = prompt }
                },
                temperature = 0.3,
            };

            var content = new StringContent(JsonConvert.SerializeObject(requestBody), Encoding.UTF8, "application/json");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

            var response = await client.PostAsync(_apiUrl, content);

            if (!response.IsSuccessStatusCode)
            {
                string errorBody = await response.Content.ReadAsStringAsync();
                throw new Exception($"API call failed with status code {response.StatusCode}: {errorBody}");
            }

            var responseBody = await response.Content.ReadAsStringAsync();
            dynamic responseObject = JsonConvert.DeserializeObject(responseBody);

            return responseObject.choices[0].message.content;
        }

        private void ApplySuggestion(string targetSection, string suggestion, string currentWikiContent)
        {
            string updatedWiki;
            if (targetSection == "Daily Log")
            {
                if (suggestion.Trim() == "No new log entries from this meeting.")
                {
                    MessageBox.Show("No new log entries to add.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                updatedWiki = InsertIntoSection(currentWikiContent, "Daily Log", suggestion);
            }
            else
            {
                updatedWiki = ReplaceSection(currentWikiContent, targetSection, suggestion);
            }

            WikiEditor.Text = updatedWiki;
            UpdateWebView(updatedWiki);
            // Auto-save on apply
            SaveButton_Click(null, null);
        }

        private string ExtractSectionContent(string fullText, string sectionTitle)
        {
            // Regex to find a markdown header (##, ###, etc.) and capture its content
            var pattern = new Regex($@"^\s*#+\s*{Regex.Escape(sectionTitle)}\s*$(.*?)(?=^\s*##\s|\Z)", RegexOptions.Multiline | RegexOptions.Singleline | RegexOptions.IgnoreCase);
            var match = pattern.Match(fullText);

            return match.Success ? match.Groups[1].Value.Trim() : "";
        }

        private string ReplaceSection(string fullText, string sectionTitle, string newContent)
        {
            // Use a more robust pattern that handles optional newlines
            var pattern = new Regex($@"^(\s*#+\s*{Regex.Escape(sectionTitle)}\s*\r?\n)(.*?)(?=^\s*##\s|\Z)", RegexOptions.Multiline | RegexOptions.Singleline | RegexOptions.IgnoreCase);
            var match = pattern.Match(fullText);

            if (match.Success)
            {
                string header = match.Groups[1].Value;
                // Build the new section text, ensuring proper spacing
                string newSection = $"{header}{newContent.Trim()}\n\n";
                // To avoid duplicating content if it was already there, we need a clean replace
                return pattern.Replace(fullText, newSection, 1);
            }
            else
            {
                // If section not found, append it to the end
                return $"{fullText.Trim()}\n\n## {sectionTitle}\n{newContent.Trim()}\n";
            }
        }

        private string InsertIntoSection(string fullText, string sectionTitle, string contentToInsert)
        {
            var pattern = new Regex($@"^(\s*#+\s*{Regex.Escape(sectionTitle)}\s*\r?\n)", RegexOptions.Multiline | RegexOptions.IgnoreCase);
            var match = pattern.Match(fullText);

            if (match.Success)
            {
                // Insert the new content right after the header line
                return fullText.Insert(match.Index + match.Length, $"{contentToInsert.Trim()}\n\n");
            }
            else
            {
                // If section not found, append it
                return $"{fullText.Trim()}\n\n## {sectionTitle}\n{contentToInsert.Trim()}\n";
            }
        }

        private void AttachFileButton_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "Transcript Files (*.txt;*.vtt;*.docx)|*.txt;*.vtr;*.docx|All files (*.*)|*.*",
                Title = "Select a Transcript File"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                string filePath = openFileDialog.FileName;
                try
                {
                    string fileContent = "";
                    string extension = Path.GetExtension(filePath).ToLower();

                    if (extension == ".docx")
                    {
                        fileContent = ReadWordDoc(filePath);
                    }
                    else // .txt, .vtt, or other text-based
                    {
                        fileContent = File.ReadAllText(filePath);
                        if (extension == ".vtt")
                        {
                            fileContent = CleanVtt(fileContent);
                        }
                    }
                    TranscriptInput.Text = fileContent;
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error reading file: {ex.Message}", "File Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private string CleanVtt(string vttContent)
        {
            // Remove WEBVTT header and timestamps
            var lines = vttContent.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            var contentLines = new List<string>();
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line) || line.Contains("-->") || line.Trim().Equals("WEBVTT", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                contentLines.Add(line);
            }
            // Join unique lines to form the transcript
            return string.Join(" ", contentLines.Distinct());
        }

        private string ReadWordDoc(string filePath)
        {
            var textBuilder = new StringBuilder();
            using (WordprocessingDocument wordDoc = WordprocessingDocument.Open(filePath, false))
            {
                // This is the corrected line: We specify the EXACT Paragraph class to use.
                foreach (var para in wordDoc.MainDocumentPart.Document.Body.Elements<DocumentFormat.OpenXml.Wordprocessing.Paragraph>())
                {
                    textBuilder.AppendLine(para.InnerText);
                }
            }
            return textBuilder.ToString();
        }

        // ---------- NEW METHOD FOR WEEKLY SUMMARY ----------
        private async void SummarizeWeekButton_Click(object sender, RoutedEventArgs e)
        {
            if (ProjectList.SelectedItem == null)
            {
                MessageBox.Show("Please select a project first.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var button = sender as Button;
            button.IsEnabled = false;
            button.Content = "Summarizing...";

            try
            {
                string currentWikiContent = WikiEditor.Text;
                string dailyLogContent = ExtractSectionContent(currentWikiContent, "Daily Log");

                if (string.IsNullOrWhiteSpace(dailyLogContent))
                {
                    MessageBox.Show("The 'Daily Log' section is empty. Nothing to summarize.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // Find log entries from the last 7 days
                var recentLogs = new StringBuilder();
                var logEntries = Regex.Split(dailyLogContent, @"(?=^###\s)", RegexOptions.Multiline);
                var cutoffDate = DateTime.Now.AddDays(-7);

                foreach (var entry in logEntries)
                {
                    if (string.IsNullOrWhiteSpace(entry)) continue;

                    var dateMatch = Regex.Match(entry, @"^###\s*(\d{4}-\d{2}-\d{2})");
                    if (dateMatch.Success && DateTime.TryParse(dateMatch.Groups[1].Value, out DateTime logDate))
                    {
                        if (logDate >= cutoffDate)
                        {
                            recentLogs.AppendLine(entry);
                        }
                    }
                }

                if (recentLogs.Length == 0)
                {
                    MessageBox.Show("No log entries found from the last 7 days.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // Build the prompt for the AI
                string summaryPrompt = $"Please provide a concise, bullet-point summary of the key activities, decisions, and blockers from the following daily log entries from the past week:\n\n---\n{recentLogs.ToString()}\n---";

                string summary = await GetAiSuggestion(summaryPrompt);

                MessageBox.Show(summary, "Weekly Summary", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred during summarization: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                button.IsEnabled = true;
                button.Content = "Summarize Week";
            }
        }
    }

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

            // This is the corrected code for setting the DockPanel property in C#
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