using AIWikiHelper.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DocumentFormat.OpenXml.Packaging;
using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;

namespace AIWikiHelper.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly ConfigService _configService;
        private readonly AIService _aiService;
        private readonly string _wikiFolderPath;

        // UI-bound properties
        [ObservableProperty]
        private ObservableCollection<string> projects = new();

        [ObservableProperty]
        private string selectedProject;

        [ObservableProperty]
        private string wikiContent;

        [ObservableProperty]
        private string transcriptInput;

        [ObservableProperty]
        private string targetSection = "Overview"; // Default

        [ObservableProperty]
        private bool isGenerating; // For disabling button during AI call

        // For WebView update (triggered on WikiContent change)
        partial void OnWikiContentChanged(string value)
        {
            // This will be called from code-behind to update WebView
        }

        // Commands
        public IRelayCommand NewProjectCommand { get; }
        public IRelayCommand RefreshProjectsCommand { get; }
        public IAsyncRelayCommand SaveCommand { get; }
        public IAsyncRelayCommand GenerateSuggestionCommand { get; }
        public IAsyncRelayCommand AttachFileCommand { get; }
        public IAsyncRelayCommand SummarizeWeekCommand { get; }

        public MainViewModel()
        {
            try
            {
                _configService = new ConfigService();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Configuration Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Application.Current.Shutdown();
            }

            _aiService = new AIService(_configService.ApiKey, _configService.ApiUrl);
            _wikiFolderPath = _configService.WikiFolderPath;

            EnsureWikiFolderExists();
            LoadProjectList();

            NewProjectCommand = new RelayCommand(NewProject);
            RefreshProjectsCommand = new RelayCommand(RefreshProjects);
            SaveCommand = new AsyncRelayCommand(SaveAsync);
            GenerateSuggestionCommand = new AsyncRelayCommand(GenerateSuggestionAsync, () => !IsGenerating);
            AttachFileCommand = new AsyncRelayCommand(AttachFileAsync);
            SummarizeWeekCommand = new AsyncRelayCommand(SummarizeWeekAsync);

            // Handle selected project change
            this.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(SelectedProject))
                {
                    LoadWikiContent();
                }
            };
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
            Projects.Clear();
            var wikiFiles = Directory.GetFiles(_wikiFolderPath, "*.md");
            foreach (var file in wikiFiles)
            {
                Projects.Add(Path.GetFileNameWithoutExtension(file));
            }
        }

        private void LoadWikiContent()
        {
            if (string.IsNullOrEmpty(SelectedProject)) return;

            string filePath = Path.Combine(_wikiFolderPath, $"{SelectedProject}.md");

            if (File.Exists(filePath))
            {
                WikiContent = File.ReadAllText(filePath);
            }
        }

        private void NewProject()
        {
            var dialog = new InputDialog("Enter new project name:");
            if (dialog.ShowDialog() != true) return;

            string projectName = dialog.ResponseText;
            if (string.IsNullOrWhiteSpace(projectName)) return;

            string filePath = Path.Combine(_wikiFolderPath, $"{projectName}.md");
            if (File.Exists(filePath))
            {
                MessageBox.Show("A project with this name already exists.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

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
            SelectedProject = projectName;
        }

        private void RefreshProjects()
        {
            LoadProjectList();
        }

        private async Task SaveAsync()
        {
            if (string.IsNullOrEmpty(SelectedProject))
            {
                MessageBox.Show("Please select a project to save.", "No Project Selected", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string filePath = Path.Combine(_wikiFolderPath, $"{SelectedProject}.md");
            await File.WriteAllTextAsync(filePath, WikiContent);

            MessageBox.Show($"{SelectedProject} has been saved.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async Task GenerateSuggestionAsync()
        {
            if (string.IsNullOrEmpty(SelectedProject))
            {
                MessageBox.Show("Please select a project first.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (string.IsNullOrWhiteSpace(TranscriptInput))
            {
                MessageBox.Show("Please paste a transcript into the AI Assistant input box.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            IsGenerating = true;
            GenerateSuggestionCommand.NotifyCanExecuteChanged();

            try
            {
                string prompt = _aiService.BuildPrompt(SelectedProject, TranscriptInput, TargetSection, WikiContent);
                string suggestion = await _aiService.GetAiSuggestionAsync(prompt);

                if (string.IsNullOrWhiteSpace(suggestion))
                {
                    MessageBox.Show("AI returned an empty suggestion.", "No Suggestion", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var dialog = new SuggestionDialog(suggestion);
                if (dialog.ShowDialog() == true)
                {
                    string finalSuggestion = dialog.EditedSuggestion; // Assuming this property exists
                    ApplySuggestion(TargetSection, finalSuggestion);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsGenerating = false;
                GenerateSuggestionCommand.NotifyCanExecuteChanged();
            }
        }

        private void ApplySuggestion(string targetSection, string suggestion)
        {
            if (targetSection == "Daily Log")
            {
                if (suggestion.Trim() == "No new log entries from this meeting.")
                {
                    MessageBox.Show("No new log entries to add.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                WikiContent = _aiService.InsertIntoSection(WikiContent, "Daily Log", suggestion);
            }
            else
            {
                WikiContent = _aiService.ReplaceSection(WikiContent, targetSection, suggestion);
            }

            // Auto-save
            _ = SaveAsync();
        }

        private async Task AttachFileAsync()
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "Transcript Files (*.txt;*.vtt;*.docx)|*.txt;*.vtt;*.docx|All files (*.*)|*.*",
                Title = "Select a Transcript File"
            };

            if (openFileDialog.ShowDialog() != true) return;

            string filePath = openFileDialog.FileName;
            try
            {
                string fileContent = "";
                string extension = Path.GetExtension(filePath).ToLower();

                if (extension == ".docx")
                {
                    fileContent = await ReadWordDocAsync(filePath);
                }
                else
                {
                    fileContent = await File.ReadAllTextAsync(filePath);
                    if (extension == ".vtt")
                    {
                        fileContent = CleanVtt(fileContent);
                    }
                }
                TranscriptInput = fileContent;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error reading file: {ex.Message}", "File Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private string CleanVtt(string vttContent)
        {
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
            return string.Join(" ", contentLines.Distinct());
        }

        private async Task<string> ReadWordDocAsync(string filePath)
        {
            var textBuilder = new StringBuilder();
            await Task.Run(() =>
            {
                using (WordprocessingDocument wordDoc = WordprocessingDocument.Open(filePath, false))
                {
                    foreach (var para in wordDoc.MainDocumentPart.Document.Body.Elements<DocumentFormat.OpenXml.Wordprocessing.Paragraph>())
                    {
                        textBuilder.AppendLine(para.InnerText);
                    }
                }
            });
            return textBuilder.ToString();
        }

        private async Task SummarizeWeekAsync()
        {
            if (string.IsNullOrEmpty(SelectedProject))
            {
                MessageBox.Show("Please select a project first.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                string dailyLogContent = _aiService.ExtractSectionContent(WikiContent, "Daily Log");

                if (string.IsNullOrWhiteSpace(dailyLogContent))
                {
                    MessageBox.Show("The 'Daily Log' section is empty. Nothing to summarize.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                string summaryPrompt = _aiService.BuildWeeklySummaryPrompt(dailyLogContent);
                string summary = await _aiService.GetAiSuggestionAsync(summaryPrompt);

                MessageBox.Show(summary, "Weekly Summary", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred during summarization: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}