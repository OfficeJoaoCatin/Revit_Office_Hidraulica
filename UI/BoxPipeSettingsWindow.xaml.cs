using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using RevitSlopePlugin.Models;
using RevitSlopePlugin.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;

namespace RevitSlopePlugin.UI
{
    public partial class BoxPipeSettingsWindow : Window
    {
        private readonly Document _doc;

        public BoxPipeSettings Settings { get; private set; }
        public bool IsConfirmed { get; private set; }

        public BoxPipeSettingsWindow(Document doc, BoxPipeSettings settings)
        {
            _doc = doc;
            Settings = settings;

            InitializeComponent();
            LoadOptions();
            LoadSettings();
        }

        private void LoadOptions()
        {
            List<OptionItem> pipeTypes = new FilteredElementCollector(_doc)
                .OfClass(typeof(PipeType))
                .Cast<PipeType>()
                .Select(type => new OptionItem(type.Name))
                .OrderBy(item => item.Name)
                .ToList();

            List<OptionItem> systemTypes = new FilteredElementCollector(_doc)
                .OfClass(typeof(PipingSystemType))
                .Cast<PipingSystemType>()
                .Select(type => new OptionItem(type.Name))
                .OrderBy(item => item.Name)
                .ToList();

            cmbPipeType.ItemsSource = pipeTypes;
            cmbSystemType.ItemsSource = systemTypes;

            cmbLimitBehavior.ItemsSource = new[]
            {
                BoxPipeSettings.LimitBehaviorBlock,
                BoxPipeSettings.LimitBehaviorWarn
            };

            cmbFlowDirection.ItemsSource = new[]
            {
                BoxPipeSettings.FlowOriginToDestination,
                BoxPipeSettings.FlowDestinationToOrigin
            };
        }

        private void LoadSettings()
        {
            cmbPipeType.SelectedValue = Settings.PipeTypeName;
            if (cmbPipeType.SelectedItem == null && cmbPipeType.Items.Count > 0)
            {
                cmbPipeType.SelectedIndex = 0;
            }

            cmbSystemType.SelectedValue = Settings.SystemTypeName;
            if (cmbSystemType.SelectedItem == null && cmbSystemType.Items.Count > 0)
            {
                cmbSystemType.SelectedIndex = 0;
            }

            txtDiameter.Text = FormatNumber(Settings.DiameterMm);
            txtPipeQuantity.Text = Settings.PipeQuantity.ToString(CultureInfo.InvariantCulture);
            txtBirthClearance.Text = FormatNumber(Settings.BirthClearanceCm);
            txtArrivalClearance.Text = FormatNumber(Settings.ArrivalClearanceCm);
            txtMinHeight.Text = FormatNumber(Settings.MinBoxHeightCm);
            txtMaxHeight.Text = FormatNumber(Settings.MaxBoxHeightCm);
            txtHeightParameter.Text = Settings.HeightParameterName;
            cmbLimitBehavior.SelectedItem = Settings.LimitBehavior;
            cmbFlowDirection.SelectedItem = Settings.FlowDirection;
            txtSpreadsheetExtensionHeader.Text = Settings.SpreadsheetExtensionHeader;
            txtSpreadsheetMappings.Text = BoxPipeSettingsService
                .EncodeMappingLines(Settings.SpreadsheetMappings)
                .Replace("\\n", Environment.NewLine);
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (cmbPipeType.SelectedValue == null)
            {
                ShowValidationError("Selecione um tipo de tubo.");
                return;
            }

            if (cmbSystemType.SelectedValue == null)
            {
                ShowValidationError("Selecione um sistema de tubulacao.");
                return;
            }

            if (!TryReadPositive(txtDiameter.Text, out double diameterMm) ||
                !TryReadPositiveInt(txtPipeQuantity.Text, out int pipeQuantity) ||
                !TryReadNonNegative(txtBirthClearance.Text, out double birthClearanceCm) ||
                !TryReadNonNegative(txtArrivalClearance.Text, out double arrivalClearanceCm) ||
                !TryReadPositive(txtMinHeight.Text, out double minHeightCm) ||
                !TryReadPositive(txtMaxHeight.Text, out double maxHeightCm))
            {
                ShowValidationError("Revise os campos numericos. O diametro usa milimetros, a quantidade precisa ser inteira e positiva, e os demais limites e folgas usam centimetros.");
                return;
            }

            if (minHeightCm > maxHeightCm)
            {
                ShowValidationError("A altura minima nao pode ser maior que a altura maxima.");
                return;
            }

            string heightParameterName = txtHeightParameter.Text.Trim();
            if (string.IsNullOrWhiteSpace(heightParameterName))
            {
                ShowValidationError("Informe o nome do parametro de altura da caixa.");
                return;
            }

            Settings = new BoxPipeSettings
            {
                PipeTypeName = cmbPipeType.SelectedValue.ToString() ?? string.Empty,
                SystemTypeName = cmbSystemType.SelectedValue.ToString() ?? string.Empty,
                DiameterMm = diameterMm,
                PipeQuantity = pipeQuantity,
                BirthClearanceCm = birthClearanceCm,
                ArrivalClearanceCm = arrivalClearanceCm,
                MinBoxHeightCm = minHeightCm,
                MaxBoxHeightCm = maxHeightCm,
                LimitBehavior = cmbLimitBehavior.SelectedItem?.ToString() ?? BoxPipeSettings.LimitBehaviorBlock,
                HeightParameterName = heightParameterName,
                FlowDirection = cmbFlowDirection.SelectedItem?.ToString() ?? BoxPipeSettings.FlowOriginToDestination,
                SpreadsheetExtensionHeader = Settings.SpreadsheetExtensionHeader,
                SpreadsheetMappings = Settings.SpreadsheetMappings
            };

            try
            {
                BoxPipeSettingsService.Save(Settings);
            }
            catch (Exception ex)
            {
                ShowValidationError($"Nao foi possivel salvar as configuracoes:\n{ex.Message}");
                return;
            }

            IsConfirmed = true;
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            IsConfirmed = false;
            DialogResult = false;
            Close();
        }

        private static bool TryReadPositive(string text, out double value)
        {
            return TryReadDouble(text, out value) && value > 0.0;
        }

        private static bool TryReadNonNegative(string text, out double value)
        {
            return TryReadDouble(text, out value) && value >= 0.0;
        }

        private static bool TryReadPositiveInt(string text, out int value)
        {
            return int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) &&
                value > 0;
        }

        private static bool TryReadDouble(string text, out double value)
        {
            string normalized = text.Trim().Replace(',', '.');
            return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private static string FormatNumber(double value)
        {
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        }

        private static void ShowValidationError(string message)
        {
            MessageBox.Show(message, "Configuracao", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private sealed class OptionItem
        {
            public OptionItem(string name)
            {
                Name = name;
            }

            public string Name { get; }
        }
    }
}
