using Microsoft.Extensions.Configuration;
using System;
using System.IO;

namespace AIWikiHelper.Services
{
    public class ConfigService
    {
        public string ApiKey { get; }
        public string ApiUrl { get; }
        public string WikiFolderPath { get; }

        public ConfigService()
        {
            var config = new ConfigurationBuilder()
                .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                .AddJsonFile("appsettings.json")
                .Build();

            ApiKey = config.GetValue<string>("OpenAI:ApiKey");
            ApiUrl = config.GetValue<string>("OpenAI:ApiUrl");
            WikiFolderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, config.GetValue<string>("AppSettings:WikiFolderPath"));

            if (string.IsNullOrEmpty(ApiKey) || ApiKey == "YOUR_API_KEY_GOES_HERE")
            {
                throw new Exception("OpenAI API Key is not configured. Please set it in appsettings.json.");
            }
        }
    }
}