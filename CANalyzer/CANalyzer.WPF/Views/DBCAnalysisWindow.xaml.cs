using System;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using CANalyzer.Core.Analyzers;
using CANalyzer.Core.Models;
using CANalyzer.Core.DBC;
using CANalyzer.Core.Parsers;

namespace CANalyzer.WPF.Views
{
    public partial class DBCAnalysisWindow : Window
    {
        private readonly LogParser _logParser;
        private readonly StatisticalAnalyzer _analyzer;
        private readonly DBCGenerator _dbcGenerator;

        public DBCAnalysisWindow()
        {
            InitializeComponent();
            _logParser = new LogParser();
            _analyzer = new StatisticalAnalyzer();
            _dbcGenerator = new DBCGenerator();
        }

        private void LoadButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "CSV files (*.csv)|*.csv|ASC files (*.asc)|*.asc|All files (*.*)|*.*",
                Title = "Open CAN Log File"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var messages = _logParser.Parse(dialog.FileName);
                    if (messages.Count == 0)
                    {
                        MessageBox.Show("No messages found in the file", "Error", 
                                       MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                    
                    _analyzer.CalculateStatistics(messages);
                    
                    MessageBox.Show($"Successfully loaded {messages.Count} messages from {dialog.FileName}",
                                   "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error loading file: {ex.Message}", 
                                   "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }
}