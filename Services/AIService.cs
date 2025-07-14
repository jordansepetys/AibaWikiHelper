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
                promptBuilder.AppendLine($"Format your output as a Markdown block starting with a level-3 header for today's date ({DateTime.Now:yyyy-MM-dd}), followed by bolded subsections (e.g., **Project Update:**, **Challenges and Solutions:**, **Development and Testing:**) with paragraphs or bullets under each.");
                promptBuilder.AppendLine("Even if the text is short, do your best to summarize the main point in structured subsections.");
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
                promptBuilder.AppendLine("Format the output with bolded subheadings for key points (e.g., **Role-Based Access:** followed by description), like a structured wiki. Use paragraphs or bullets under each subheading.");
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

        public string BuildBundledPrompt(string projectName, string transcript, List<string> targetSections, string currentWikiContent)
        {
            var promptBuilder = new StringBuilder();
            promptBuilder.AppendLine($"You are an AI assistant that extracts and records notes from transcripts into project wiki sections FACTUALLY. Do not add insights, analysis, or new ideas—stick strictly to the transcript content.");
            promptBuilder.AppendLine($"Project: {projectName}");
            promptBuilder.AppendLine($"\nTranscript to extract from:\n```\n{transcript}\n```");
            promptBuilder.AppendLine("\nFor each listed section, output EXACTLY in this format: 'SectionName:' (exact name) on one line, then the extracted/revised content on the next lines. Separate sections with a single '---' on its own line. Output NOTHING else—no introductions, summaries, or extra text.");
            promptBuilder.AppendLine("If no relevant notes for a section, output empty content after the name (e.g., SectionName:\n<empty>).");
            promptBuilder.AppendLine("Use bolded subheadings for key points (e.g., **Subtopic:** followed by description or bullets) to make it structured like a wiki.");

            foreach (var section in targetSections)
            {
                if (section == "Daily Log")
                {
                    promptBuilder.AppendLine($"\nFor {section}: Extract key notes as bolded subsections (e.g., **Project Update:**, **Challenges and Solutions:**) with paragraphs or bullets under each, starting under '### {DateTime.Now:yyyy-MM-dd}'. Do not include the section header.");
                }
                else
                {
                    string currentContent = ExtractSectionContent(currentWikiContent, section);
                    promptBuilder.AppendLine($"\nFor {section}: Current content:\n```\n{currentContent}\n```");
                    promptBuilder.AppendLine("Extract relevant notes from transcript and integrate them factually into the current content (append or revise minimally with bolded subheadings like **Feature Name:**). Output only the full revised text—no header.");
                }
            }

            return promptBuilder.ToString();
        }

        // Handles Daily Log insertions without duplicating dates
        public string InsertDailyLogEntry(string fullText, string aiOutput)
        {
            if (string.IsNullOrWhiteSpace(aiOutput) || aiOutput.Contains("No new log entries"))
            {
                return fullText; // No changes if nothing to add
            }

            // Extract date and bullets from AI output (assumes format: ### date\nbullets)
            var headerMatch = Regex.Match(aiOutput.Trim(), @"^\s*###\s*(\d{4}-\d{2}-\d{2})\s*$(.*)", RegexOptions.Multiline | RegexOptions.Singleline);
            if (!headerMatch.Success)
            {
                return fullText; // Invalid AI output; skip
            }

            string date = headerMatch.Groups[1].Value;
            string bullets = headerMatch.Groups[2].Value.Trim();

            string sectionTitle = "Daily Log";

            // Pattern to find the Daily Log section
            var sectionPattern = new Regex(@"^(?<header>\s*##\s*" + Regex.Escape(sectionTitle) + @"\s*$\s*)(?<content>.*?)(?=\Z|^\s*##\s)", RegexOptions.Multiline | RegexOptions.Singleline | RegexOptions.IgnoreCase);
            var sectionMatch = sectionPattern.Match(fullText);

            if (sectionMatch.Success)
            {
                string sectionHeader = sectionMatch.Groups["header"].Value;
                string sectionContent = sectionMatch.Groups["content"].Value.Trim();

                // Pattern for the specific date header within the section content
                var datePattern = new Regex(@"^(?<date_header>\s*###\s*" + Regex.Escape(date) + @"\s*$\s*)(?<date_content>.*?)(?=\Z|^\s*###\s)", RegexOptions.Multiline | RegexOptions.Singleline);
                var dateMatch = datePattern.Match(sectionContent);

                string newSectionContent;
                if (dateMatch.Success)
                {
                    // Append bullets to existing date's content
                    string existingDateContent = dateMatch.Groups["date_content"].Value.Trim();
                    string newDateContent = existingDateContent + (string.IsNullOrEmpty(existingDateContent) ? "" : "\n") + bullets;
                    newSectionContent = datePattern.Replace(sectionContent, dateMatch.Groups["date_header"].Value + newDateContent + "\n\n", 1);
                }
                else
                {
                    // Add new date header + bullets at the end of the section
                    newSectionContent = (sectionContent + "\n\n" + $"### {date}\n{bullets}\n").Trim();
                }

                // Rebuild the full text with updated section
                return sectionPattern.Replace(fullText, sectionHeader + newSectionContent + "\n\n", 1);
            }
            else
            {
                // No Daily Log section; add it with the new entry
                return $"{fullText.Trim()}\n\n## {sectionTitle}\n### {date}\n{bullets}\n";
            }
        }
    }
}