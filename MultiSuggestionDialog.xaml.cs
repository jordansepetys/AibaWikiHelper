using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;

namespace AIWikiHelper
{
    public partial class MultiSuggestionDialog : Window
    {
        public Dictionary<string, string> ApprovedSuggestions { get; private set; }

        public ObservableCollection<SuggestionItem> Suggestions { get; } = new ObservableCollection<SuggestionItem>();

        public MultiSuggestionDialog(Dictionary<string, string> initialSuggestions)
        {
            InitializeComponent();
            DataContext = this;

            foreach (var kvp in initialSuggestions)
            {
                Suggestions.Add(new SuggestionItem { Key = kvp.Key, Value = kvp.Value, IsSelected = true });
            }
        }

        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            ApprovedSuggestions = new Dictionary<string, string>();
            foreach (var item in Suggestions)
            {
                if (item.IsSelected)
                {
                    ApprovedSuggestions[item.Key] = item.Value;
                }
            }
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }

    public class SuggestionItem : INotifyPropertyChanged
    {
        private string _key;
        public string Key
        {
            get => _key;
            set { _key = value; OnPropertyChanged(nameof(Key)); }
        }

        private string _value;
        public string Value
        {
            get => _value;
            set { _value = value; OnPropertyChanged(nameof(Value)); }
        }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(nameof(IsSelected)); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}