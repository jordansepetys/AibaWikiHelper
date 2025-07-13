using Newtonsoft.Json;
using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace AIWikiHelper.Services
{
    public class AIService
    {
        private readonly HttpClient _client;
        private readonly string _apiKey;
        private readonly string _apiUrl;

        public AIService(string apiKey, string apiUrl)
        {
            _apiKey = apiKey;
            _apiUrl = apiUrl;
            _client = new HttpClient();
        }

        public async Task<string> GetAiSuggestionAsync(string prompt)
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
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

            var response = await _client.PostAsync(_apiUrl, content);

            if (!response.IsSuccessStatusCode)
            {
                string errorBody = await response.Content.ReadAsStringAsync();
                throw new Exception($"API call failed with status code {response.StatusCode}: {errorBody}");
            }

            var responseBody = await response.Content.ReadAsStringAsync();
            dynamic responseObject = JsonConvert.DeserializeObject(responseBody);

            return responseObject.choices[0].message.content;
        }

        public string BuildPrompt(string projectName, string transcript, string targetSection, string currentWikiContent)
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

        public string BuildWeeklySummaryPrompt(string dailyLogContent)
        {
            // Extract recent logs (last 7 days)
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
                throw new Exception("No log entries found from the last 7 days.");
            }

            return $"Please provide a concise, bullet-point summary of the key activities, decisions, and blockers from the following daily log entries from the past week:\n\n---\n{recentLogs.ToString()}\n---";
        }

        public string ExtractSectionContent(string fullText, string sectionTitle)
        {
            var pattern = new Regex($@"^\s*#+\s*{Regex.Escape(sectionTitle)}\s*$(.*?)(?=^\s*##\s|\Z)", RegexOptions.Multiline | RegexOptions.Singleline | RegexOptions.IgnoreCase);
            var match = pattern.Match(fullText);

            return match.Success ? match.Groups[1].Value.Trim() : "";
        }

        public string ReplaceSection(string fullText, string sectionTitle, string newContent)
        {
            var pattern = new Regex($@"^(\s*#+\s*{Regex.Escape(sectionTitle)}\s*\r?\n)(.*?)(?=^\s*##\s|\Z)", RegexOptions.Multiline | RegexOptions.Singleline | RegexOptions.IgnoreCase);
            var match = pattern.Match(fullText);

            if (match.Success)
            {
                string header = match.Groups[1].Value;
                string newSection = $"{header}{newContent.Trim()}\n\n";
                return pattern.Replace(fullText, newSection, 1);
            }
            else
            {
                return $"{fullText.Trim()}\n\n## {sectionTitle}\n{newContent.Trim()}\n";
            }
        }

        public string InsertIntoSection(string fullText, string sectionTitle, string contentToInsert)
        {
            var pattern = new Regex($@"^(\s*#+\s*{Regex.Escape(sectionTitle)}\s*\r?\n)", RegexOptions.Multiline | RegexOptions.IgnoreCase);
            var match = pattern.Match(fullText);

            if (match.Success)
            {
                return fullText.Insert(match.Index + match.Length, $"{contentToInsert.Trim()}\n\n");
            }
            else
            {
                return $"{fullText.Trim()}\n\n## {sectionTitle}\n{contentToInsert.Trim()}\n";
            }
        }
    }
}