// C:\Users\rveremeev\Documents\CANalyzer\CANalyzer.Scripts\Program.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CommandLine;
using CANalyzer.Core.Analyzers;
using CANalyzer.Core.DBC;
using CANalyzer.Core.Models;
using CANalyzer.Core.Parsers;

namespace CANalyzer.Scripts
{
    class Program
    {
        static void Main(string[] args)
        {
            Parser.Default.ParseArguments<Options>(args)
                .WithParsed(options => Run(options))
                .WithNotParsed(errors => HandleParseErrors(errors));
        }

        static void Run(Options options)
        {
            Console.WriteLine("=== CANalyzer Command Line Tool ===");
            Console.WriteLine();

            if (!File.Exists(options.InputFile))
            {
                Console.WriteLine($"Error: Input file '{options.InputFile}' not found");
                return;
            }

            try
            {
                // 1. Загрузка лога
                Console.WriteLine($"Loading log file: {options.InputFile}");
                var parser = new LogParser();
                var messages = parser.Parse(options.InputFile);

                Console.WriteLine($"Loaded {messages.Count} messages");

                // 2. Статистический анализ
                Console.WriteLine("Analyzing messages...");
                var analyzer = new StatisticalAnalyzer();
                analyzer.CalculateStatistics(messages);

                // Правильный вызов метода GetStatistics()
                var statistics = analyzer.GetStatistics(); // Это возвращает Dictionary<uint, CANalyzer.Core.Models.MessageStatistics>

                Console.WriteLine($"Found {statistics.Count} unique message IDs");

                // 3. Детектирование сигналов
                Console.WriteLine("Detecting signals...");
                var detector = new SignalDetector();
                int totalSignals = 0;

                foreach (var kvp in statistics)
                {
                    // kvp.Value - это CANalyzer.Core.Models.MessageStatistics
                    var signals = detector.FindSignals(kvp.Value);
                    totalSignals += signals.Count;
                }

                Console.WriteLine($"Detected {totalSignals} signals");

                // 4. Генерация DBC
                if (!string.IsNullOrEmpty(options.OutputFile))
                {
                    Console.WriteLine($"Generating DBC file: {options.OutputFile}");
                    var dbcGenerator = new DBCGenerator();

                    foreach (var kvp in statistics)
                    {
                        if (kvp.Value.DetectedSignals.Count > 0)
                        {
                            dbcGenerator.AddMessage(kvp.Key, kvp.Value.DetectedSignals);
                        }
                    }

                    dbcGenerator.SaveToFile(options.OutputFile);
                    Console.WriteLine($"Successfully generated DBC file with {statistics.Count} messages");
                }

                Console.WriteLine();
                Console.WriteLine("Analysis completed successfully!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
        }

        static void HandleParseErrors(IEnumerable<Error> errors)
        {
            Console.WriteLine("Error parsing command line arguments");
            foreach (var error in errors)
            {
                Console.WriteLine($"- {error}");
            }
        }
    }

    class Options
    {
        [Option('i', "input", Required = true, HelpText = "Input CAN log file (.csv or .asc)")]
        public required string InputFile { get; set; }

        [Option('o', "output", Required = false, HelpText = "Output DBC file")]
        public string? OutputFile { get; set; }
    }
}