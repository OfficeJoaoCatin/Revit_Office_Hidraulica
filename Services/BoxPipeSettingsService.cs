using RevitSlopePlugin.Models;
using RevitSlopePlugin.Utils;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace RevitSlopePlugin.Services
{
    public static class BoxPipeSettingsService
    {
        private static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RevitSlopePlugin",
            "BoxPipeSettings.cfg");

        public static BoxPipeSettings Load()
        {
            try
            {
                if (!File.Exists(SettingsPath))
                {
                    return new BoxPipeSettings();
                }

                var settings = new BoxPipeSettings();
                foreach (string line in File.ReadAllLines(SettingsPath))
                {
                    int separatorIndex = line.IndexOf('=');
                    if (separatorIndex <= 0)
                    {
                        continue;
                    }

                    string key = line.Substring(0, separatorIndex).Trim();
                    string value = line.Substring(separatorIndex + 1).Trim();
                    ApplyValue(settings, key, value);
                }

                if (settings.SpreadsheetMappings.Count == 0)
                {
                    settings.SpreadsheetMappings.Add(new SewerSpreadsheetMapping());
                }

                return settings;
            }
            catch (Exception ex)
            {
                Logger.Warn($"Nao foi possivel ler as configuracoes de tubo entre caixas: {ex.Message}");
                return new BoxPipeSettings();
            }
        }

        public static void Save(BoxPipeSettings settings)
        {
            try
            {
                string? directory = Path.GetDirectoryName(SettingsPath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var lines = new List<string>
                {
                    $"PipeTypeName={settings.PipeTypeName}",
                    $"SystemTypeName={settings.SystemTypeName}",
                    $"DiameterMm={Format(settings.DiameterMm)}",
                    $"PipeQuantity={settings.PipeQuantity}",
                    $"BirthClearanceCm={Format(settings.BirthClearanceCm)}",
                    $"ArrivalClearanceCm={Format(settings.ArrivalClearanceCm)}",
                    $"MinBoxHeightCm={Format(settings.MinBoxHeightCm)}",
                    $"MaxBoxHeightCm={Format(settings.MaxBoxHeightCm)}",
                    $"LimitBehavior={settings.LimitBehavior}",
                    $"HeightParameterName={settings.HeightParameterName}",
                    $"FlowDirection={settings.FlowDirection}",
                    $"SpreadsheetExtensionHeader={settings.SpreadsheetExtensionHeader}",
                    $"SpreadsheetMappingLines={EncodeMappingLines(settings.SpreadsheetMappings)}"
                };

                File.WriteAllLines(SettingsPath, lines);
            }
            catch (Exception ex)
            {
                Logger.Warn($"Nao foi possivel salvar as configuracoes de tubo entre caixas: {ex.Message}");
                throw;
            }
        }

        private static void ApplyValue(BoxPipeSettings settings, string key, string value)
        {
            switch (key)
            {
                case nameof(BoxPipeSettings.PipeTypeName):
                    settings.PipeTypeName = value;
                    break;
                case nameof(BoxPipeSettings.SystemTypeName):
                    settings.SystemTypeName = value;
                    break;
                case nameof(BoxPipeSettings.DiameterMm):
                    settings.DiameterMm = ReadDouble(value, settings.DiameterMm);
                    break;
                case "DiameterCm":
                    settings.DiameterMm = ReadDouble(value, settings.DiameterMm / 10.0) * 10.0;
                    break;
                case nameof(BoxPipeSettings.PipeQuantity):
                    settings.PipeQuantity = ReadInt(value, settings.PipeQuantity);
                    break;
                case nameof(BoxPipeSettings.BirthClearanceCm):
                    settings.BirthClearanceCm = ReadDouble(value, settings.BirthClearanceCm);
                    break;
                case nameof(BoxPipeSettings.ArrivalClearanceCm):
                    settings.ArrivalClearanceCm = ReadDouble(value, settings.ArrivalClearanceCm);
                    break;
                case nameof(BoxPipeSettings.MinBoxHeightCm):
                    settings.MinBoxHeightCm = ReadDouble(value, settings.MinBoxHeightCm);
                    break;
                case nameof(BoxPipeSettings.MaxBoxHeightCm):
                    settings.MaxBoxHeightCm = ReadDouble(value, settings.MaxBoxHeightCm);
                    break;
                case nameof(BoxPipeSettings.LimitBehavior):
                    settings.LimitBehavior = value;
                    break;
                case nameof(BoxPipeSettings.HeightParameterName):
                    settings.HeightParameterName = value;
                    break;
                case nameof(BoxPipeSettings.FlowDirection):
                    settings.FlowDirection = value;
                    break;
                case nameof(BoxPipeSettings.SpreadsheetExtensionHeader):
                    settings.SpreadsheetExtensionHeader = value;
                    break;
                case "SpreadsheetMappingLines":
                    settings.SpreadsheetMappings = DecodeMappingLines(value);
                    break;
            }
        }

        private static double ReadDouble(string value, double fallback)
        {
            return double.TryParse(
                value.Replace(',', '.'),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double result)
                ? result
                : fallback;
        }

        private static string Format(double value)
        {
            return value.ToString("0.####", CultureInfo.InvariantCulture);
        }

        private static int ReadInt(string value, int fallback)
        {
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result) &&
                result > 0
                ? result
                : fallback;
        }

        public static string EncodeMappingLines(List<SewerSpreadsheetMapping> mappings)
        {
            return string.Join(
                "\\n",
                (mappings == null || mappings.Count == 0 ? new List<SewerSpreadsheetMapping> { new SewerSpreadsheetMapping() } : mappings)
                    .Select(mapping => string.Join(
                        "|",
                        Escape(mapping.Name),
                        Escape(mapping.CodePrefix),
                        Escape(mapping.FamilyNames),
                        Escape(mapping.NumberParameterName))));
        }

        public static List<SewerSpreadsheetMapping> DecodeMappingLines(string text)
        {
            var mappings = new List<SewerSpreadsheetMapping>();
            string normalized = (text ?? string.Empty).Replace("\\n", "\n");

            foreach (string line in normalized.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] parts = line.Split('|');
                if (parts.Length < 4)
                {
                    continue;
                }

                mappings.Add(new SewerSpreadsheetMapping
                {
                    Name = Unescape(parts[0]).Trim(),
                    CodePrefix = Unescape(parts[1]).Trim(),
                    FamilyNames = Unescape(parts[2]).Trim(),
                    NumberParameterName = Unescape(parts[3]).Trim()
                });
            }

            if (mappings.Count == 0)
            {
                mappings.Add(new SewerSpreadsheetMapping());
            }

            return mappings;
        }

        private static string Escape(string value)
        {
            return (value ?? string.Empty)
                .Replace("%", "%25")
                .Replace("|", "%7C")
                .Replace("\r", string.Empty)
                .Replace("\n", " ");
        }

        private static string Unescape(string value)
        {
            return (value ?? string.Empty)
                .Replace("%7C", "|")
                .Replace("%25", "%");
        }
    }
}
