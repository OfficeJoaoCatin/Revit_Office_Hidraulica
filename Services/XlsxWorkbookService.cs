using RevitSlopePlugin.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace RevitSlopePlugin.Services
{
    internal sealed class XlsxWorkbookService
    {
        private static readonly XNamespace SpreadsheetNs =
            "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        private static readonly XNamespace RelationshipNs =
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

        private static readonly XNamespace PackageRelationshipNs =
            "http://schemas.openxmlformats.org/package/2006/relationships";

        public List<string> GetSheetNames(string workbookPath)
        {
            using (ZipArchive archive = ZipFile.OpenRead(workbookPath))
            {
                XDocument workbook = LoadXml(archive, "xl/workbook.xml");
                return workbook
                    .Descendants(SpreadsheetNs + "sheet")
                    .Select(sheet => sheet.Attribute("name")?.Value)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Cast<string>()
                    .ToList();
            }
        }

        public XlsxWorksheet ReadWorksheet(string workbookPath, string sheetName)
        {
            using (ZipArchive archive = ZipFile.OpenRead(workbookPath))
            {
                List<string> sharedStrings = ReadSharedStrings(archive);
                string worksheetPath = ResolveWorksheetPath(archive, sheetName);
                XDocument worksheet = LoadXml(archive, worksheetPath);
                var cells = new Dictionary<(int Row, int Column), string>();

                foreach (XElement cell in worksheet.Descendants(SpreadsheetNs + "c"))
                {
                    string reference = cell.Attribute("r")?.Value ?? string.Empty;
                    if (!TryParseCellReference(reference, out int row, out int column))
                    {
                        continue;
                    }

                    cells[(row, column)] = ReadCellText(cell, sharedStrings);
                }

                return new XlsxWorksheet(sheetName, cells);
            }
        }

        public void WriteNumbers(
            string workbookPath,
            string sheetName,
            IReadOnlyDictionary<(int Row, int Column), double> values)
        {
            if (values.Count == 0)
            {
                return;
            }

            using (ZipArchive archive = ZipFile.Open(workbookPath, ZipArchiveMode.Update))
            {
                string worksheetPath = ResolveWorksheetPath(archive, sheetName);
                ZipArchiveEntry entry = archive.GetEntry(worksheetPath)
                    ?? throw new InvalidOperationException($"A aba '{sheetName}' nao foi encontrada.");

                XDocument worksheet;
                using (Stream stream = entry.Open())
                {
                    worksheet = XDocument.Load(stream);
                }

                XElement sheetData = worksheet.Root?.Element(SpreadsheetNs + "sheetData")
                    ?? throw new InvalidOperationException("A planilha nao possui sheetData.");

                foreach (var pair in values)
                {
                    SetNumberCell(sheetData, pair.Key.Row, pair.Key.Column, pair.Value);
                }

                entry.Delete();
                ZipArchiveEntry newEntry = archive.CreateEntry(worksheetPath);
                using (Stream stream = newEntry.Open())
                {
                    worksheet.Save(stream);
                }
            }
        }

        public List<SewerSpreadsheetSegment> FindSegments(
            XlsxWorksheet worksheet,
            string codePrefix,
            string extensionHeader)
        {
            int extensionColumn = worksheet.FindHeaderColumn(extensionHeader);
            if (extensionColumn <= 0)
            {
                extensionColumn = worksheet.FindHeaderColumn("extens");
            }

            int minimumSlopeColumn = worksheet.FindHeaderColumn("declividade minima");
            if (minimumSlopeColumn <= 0)
            {
                minimumSlopeColumn = worksheet.FindHeaderColumn("declividade min");
            }

            int diameterColumn = worksheet.FindHeaderColumn("diametro calcul");
            if (diameterColumn <= 0)
            {
                diameterColumn = worksheet.FindHeaderColumn("diam. calcul");
            }

            Regex primaryCodeRegex = BuildCodeRegex(codePrefix);
            Regex anyCodeRegex = BuildAnyCodeRegex();
            var segments = new List<SewerSpreadsheetSegment>();

            foreach (int row in worksheet.Rows)
            {
                var matches = new List<(string Code, string Number)>();
                List<string> rowValues = worksheet.GetRowValues(row).ToList();
                bool belongsToMapping = rowValues.Any(text => primaryCodeRegex.IsMatch(RemoveDiacritics(text)));
                if (!belongsToMapping)
                {
                    continue;
                }

                foreach (string text in rowValues)
                {
                    foreach (Match match in anyCodeRegex.Matches(RemoveDiacritics(text).ToUpperInvariant()))
                    {
                        string code = match.Groups["code"].Value.Trim();
                        string number = NormalizeNumber(match.Groups["number"].Value);
                        if (!matches.Any(existing => existing.Code.Equals(code, StringComparison.OrdinalIgnoreCase)))
                        {
                            matches.Add((code, number));
                        }
                    }
                }

                if (matches.Count < 2 || extensionColumn <= 0)
                {
                    continue;
                }

                segments.Add(new SewerSpreadsheetSegment
                {
                    Row = row,
                    ExtensionColumn = extensionColumn,
                    FirstCode = matches[0].Code,
                    SecondCode = matches[1].Code,
                    FirstNumber = matches[0].Number,
                    SecondNumber = matches[1].Number,
                    ExistingExtensionMeters = ReadNullableDouble(worksheet.GetText(row, extensionColumn)),
                    MinimumSlope = ReadNullableDouble(worksheet.GetText(row, minimumSlopeColumn)),
                    CalculatedDiameterMm = ReadNullableDouble(worksheet.GetText(row, diameterColumn))
                });
            }

            return segments;
        }

        public static string NormalizeNumber(string value)
        {
            string digits = new string((value ?? string.Empty).Where(char.IsDigit).ToArray());
            if (string.IsNullOrWhiteSpace(digits))
            {
                return string.Empty;
            }

            string trimmed = digits.TrimStart('0');
            return string.IsNullOrWhiteSpace(trimmed) ? "0" : trimmed;
        }

        public static double? ReadNullableDouble(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            string normalized = text
                .Trim()
                .Replace("%", string.Empty);

            normalized = Regex.Replace(normalized, "mm", string.Empty, RegexOptions.IgnoreCase);
            normalized = Regex.Replace(normalized, "m", string.Empty, RegexOptions.IgnoreCase)
                .Replace(',', '.');

            Match match = Regex.Match(normalized, @"-?\d+(\.\d+)?");
            if (!match.Success)
            {
                return null;
            }

            return double.TryParse(
                match.Value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double value)
                ? value
                : null;
        }

        private static Regex BuildCodeRegex(string codePrefix)
        {
            string normalizedPrefix = RemoveDiacritics(codePrefix).Trim();
            return new Regex(
                Regex.Escape(normalizedPrefix) + @"[-\s]*(?<number>\d+)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        private static Regex BuildAnyCodeRegex()
        {
            return new Regex(
                @"(?<![A-Z0-9])(?<code>[A-Z]{1,}(?:-[A-Z0-9]+)*-(?<number>\d+))(?![A-Z0-9])",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        private static List<string> ReadSharedStrings(ZipArchive archive)
        {
            ZipArchiveEntry? entry = archive.GetEntry("xl/sharedStrings.xml");
            if (entry == null)
            {
                return new List<string>();
            }

            using (Stream stream = entry.Open())
            {
                XDocument doc = XDocument.Load(stream);
                return doc
                    .Descendants(SpreadsheetNs + "si")
                    .Select(si => string.Concat(si.Descendants(SpreadsheetNs + "t").Select(t => t.Value)))
                    .ToList();
            }
        }

        private static string ResolveWorksheetPath(ZipArchive archive, string sheetName)
        {
            XDocument workbook = LoadXml(archive, "xl/workbook.xml");
            XDocument relationships = LoadXml(archive, "xl/_rels/workbook.xml.rels");

            XElement sheet = workbook
                .Descendants(SpreadsheetNs + "sheet")
                .FirstOrDefault(item => string.Equals(
                    item.Attribute("name")?.Value,
                    sheetName,
                    StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"A aba '{sheetName}' nao foi encontrada.");

            string relationId = sheet.Attribute(RelationshipNs + "id")?.Value
                ?? throw new InvalidOperationException($"A aba '{sheetName}' nao possui relacao interna.");

            XElement relationship = relationships
                .Descendants(PackageRelationshipNs + "Relationship")
                .FirstOrDefault(item => item.Attribute("Id")?.Value == relationId)
                ?? throw new InvalidOperationException($"A aba '{sheetName}' nao possui arquivo interno.");

            string target = relationship.Attribute("Target")?.Value ?? string.Empty;
            if (string.IsNullOrWhiteSpace(target))
            {
                throw new InvalidOperationException($"A aba '{sheetName}' possui destino vazio.");
            }

            return target.StartsWith("xl/", StringComparison.OrdinalIgnoreCase)
                ? target
                : "xl/" + target.TrimStart('/');
        }

        private static XDocument LoadXml(ZipArchive archive, string path)
        {
            ZipArchiveEntry entry = archive.GetEntry(path)
                ?? throw new InvalidOperationException($"Arquivo interno nao encontrado no xlsx: {path}");

            using (Stream stream = entry.Open())
            {
                return XDocument.Load(stream);
            }
        }

        private static string ReadCellText(XElement cell, List<string> sharedStrings)
        {
            string type = cell.Attribute("t")?.Value ?? string.Empty;
            if (type == "s")
            {
                string indexText = cell.Element(SpreadsheetNs + "v")?.Value ?? string.Empty;
                return int.TryParse(indexText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index) &&
                    index >= 0 &&
                    index < sharedStrings.Count
                    ? sharedStrings[index]
                    : string.Empty;
            }

            if (type == "inlineStr")
            {
                return string.Concat(cell.Descendants(SpreadsheetNs + "t").Select(t => t.Value));
            }

            return cell.Element(SpreadsheetNs + "v")?.Value ??
                cell.Element(SpreadsheetNs + "is")?.Value ??
                string.Empty;
        }

        private static void SetNumberCell(XElement sheetData, int rowNumber, int columnNumber, double value)
        {
            XElement row = sheetData
                .Elements(SpreadsheetNs + "row")
                .FirstOrDefault(item => (int?)item.Attribute("r") == rowNumber)
                ?? CreateRow(sheetData, rowNumber);

            string reference = ToColumnName(columnNumber) + rowNumber.ToString(CultureInfo.InvariantCulture);
            XElement cell = row
                .Elements(SpreadsheetNs + "c")
                .FirstOrDefault(item => string.Equals(
                    item.Attribute("r")?.Value,
                    reference,
                    StringComparison.OrdinalIgnoreCase))
                ?? CreateCell(row, columnNumber, reference);

            cell.SetAttributeValue("t", null);
            cell.Elements(SpreadsheetNs + "is").Remove();
            cell.Elements(SpreadsheetNs + "f").Remove();

            XElement valueElement = cell.Element(SpreadsheetNs + "v") ?? new XElement(SpreadsheetNs + "v");
            valueElement.Value = value.ToString("0.###", CultureInfo.InvariantCulture);
            if (valueElement.Parent == null)
            {
                cell.Add(valueElement);
            }
        }

        private static XElement CreateRow(XElement sheetData, int rowNumber)
        {
            var row = new XElement(SpreadsheetNs + "row", new XAttribute("r", rowNumber));
            XElement? insertBefore = sheetData
                .Elements(SpreadsheetNs + "row")
                .FirstOrDefault(item => ((int?)item.Attribute("r") ?? int.MaxValue) > rowNumber);

            if (insertBefore == null)
            {
                sheetData.Add(row);
            }
            else
            {
                insertBefore.AddBeforeSelf(row);
            }

            return row;
        }

        private static XElement CreateCell(XElement row, int columnNumber, string reference)
        {
            var cell = new XElement(SpreadsheetNs + "c", new XAttribute("r", reference));
            XElement? insertBefore = row
                .Elements(SpreadsheetNs + "c")
                .FirstOrDefault(item =>
                    TryParseCellReference(item.Attribute("r")?.Value ?? string.Empty, out _, out int existingColumn) &&
                    existingColumn > columnNumber);

            if (insertBefore == null)
            {
                row.Add(cell);
            }
            else
            {
                insertBefore.AddBeforeSelf(cell);
            }

            return cell;
        }

        private static bool TryParseCellReference(string reference, out int row, out int column)
        {
            row = 0;
            column = 0;
            Match match = Regex.Match(reference ?? string.Empty, @"^(?<col>[A-Z]+)(?<row>\d+)$", RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                return false;
            }

            row = int.Parse(match.Groups["row"].Value, CultureInfo.InvariantCulture);
            column = 0;
            foreach (char c in match.Groups["col"].Value.ToUpperInvariant())
            {
                column = (column * 26) + (c - 'A' + 1);
            }

            return true;
        }

        private static string ToColumnName(int columnNumber)
        {
            string name = string.Empty;
            int dividend = columnNumber;
            while (dividend > 0)
            {
                int modulo = (dividend - 1) % 26;
                name = Convert.ToChar('A' + modulo) + name;
                dividend = (dividend - modulo) / 26;
            }

            return name;
        }

        internal static string RemoveDiacritics(string text)
        {
            string normalized = (text ?? string.Empty).Normalize(System.Text.NormalizationForm.FormD);
            return new string(normalized
                .Where(ch => CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                .ToArray())
                .Normalize(System.Text.NormalizationForm.FormC);
        }
    }

    internal sealed class XlsxWorksheet
    {
        private readonly Dictionary<(int Row, int Column), string> _cells;

        public XlsxWorksheet(string name, Dictionary<(int Row, int Column), string> cells)
        {
            Name = name;
            _cells = cells;
            Rows = _cells.Keys.Select(key => key.Row).Distinct().OrderBy(row => row).ToList();
        }

        public string Name { get; }
        public List<int> Rows { get; }

        public string GetText(int row, int column)
        {
            return column <= 0 ? string.Empty : _cells.TryGetValue((row, column), out string? value) ? value : string.Empty;
        }

        public IEnumerable<string> GetRowValues(int row)
        {
            return _cells
                .Where(pair => pair.Key.Row == row)
                .OrderBy(pair => pair.Key.Column)
                .Select(pair => pair.Value);
        }

        public int FindHeaderColumn(string headerText)
        {
            if (string.IsNullOrWhiteSpace(headerText))
            {
                return 0;
            }

            string normalizedHeader = XlsxWorkbookService.RemoveDiacritics(headerText).ToLowerInvariant();
            return _cells
                .Where(pair => pair.Key.Row <= 30)
                .Where(pair => XlsxWorkbookService
                    .RemoveDiacritics(pair.Value)
                    .ToLowerInvariant()
                    .Contains(normalizedHeader))
                .OrderBy(pair => pair.Key.Row)
                .ThenBy(pair => pair.Key.Column)
                .Select(pair => pair.Key.Column)
                .FirstOrDefault();
        }
    }
}
