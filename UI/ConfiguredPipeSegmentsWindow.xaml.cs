using Autodesk.Revit.DB.Plumbing;
using RevitSlopePlugin.Models;
using RevitSlopePlugin.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace RevitSlopePlugin.UI
{
    public partial class ConfiguredPipeSegmentsWindow : Window
    {
        public ConfiguredPipeSegmentsWindow(
            IEnumerable<PipingSystemType> systemTypes,
            string selectedSystemTypeName,
            IEnumerable<PipeType> pipeTypes,
            string selectedPipeTypeName,
            List<ConfiguredPipeSegmentInput> segments)
        {
            InitializeComponent();
            Segments = segments;
            PipeTypes = pipeTypes
                .OrderBy(type => type.Name)
                .ToList();

            List<PipingSystemType> orderedSystemTypes = systemTypes
                .OrderBy(type => type.Name)
                .ToList();

            cmbInfrastructure.ItemsSource = orderedSystemTypes;
            cmbInfrastructure.SelectedItem = orderedSystemTypes
                .FirstOrDefault(type => type.Name.Equals(selectedSystemTypeName, StringComparison.OrdinalIgnoreCase)) ??
                orderedSystemTypes.FirstOrDefault();

            string defaultPipeTypeName = PipeTypes
                .FirstOrDefault(type => type.Name.Equals(selectedPipeTypeName, StringComparison.OrdinalIgnoreCase))?.Name ??
                PipeTypes.FirstOrDefault()?.Name ??
                string.Empty;

            foreach (ConfiguredPipeSegmentInput segment in Segments.Where(segment => string.IsNullOrWhiteSpace(segment.PipeTypeName)))
            {
                segment.PipeTypeName = defaultPipeTypeName;
            }

            gridSegments.ItemsSource = Segments;
        }

        public List<ConfiguredPipeSegmentInput> Segments { get; }
        public List<PipeType> PipeTypes { get; }
        public PipingSystemType? SelectedSystemType { get; private set; }
        public bool IsConfirmed { get; private set; }

        private void BtnCreate_Click(object sender, RoutedEventArgs e)
        {
            gridSegments.CommitEdit(DataGridEditingUnit.Cell, true);
            gridSegments.CommitEdit(DataGridEditingUnit.Row, true);

            if (cmbInfrastructure.SelectedItem is not PipingSystemType systemType)
            {
                ShowValidationError("Selecione o tipo de infraestrutura.");
                return;
            }

            foreach (ConfiguredPipeSegmentInput segment in Segments)
            {
                if (!PipeTypes.Any(type => type.Name.Equals(segment.PipeTypeName, StringComparison.OrdinalIgnoreCase)))
                {
                    ShowValidationError($"Revise o tipo de tubo do trecho {segment.Index}.");
                    return;
                }

                if (!TryReadPositive(segment.SlopePercentText, out double slopePercent))
                {
                    ShowValidationError($"Revise a inclinacao do trecho {segment.Index}.");
                    return;
                }

                if (!TryReadPositiveInt(segment.PipeQuantityText, out int pipeQuantity))
                {
                    ShowValidationError($"Revise a quantidade de tubos do trecho {segment.Index}.");
                    return;
                }

                if (!TryReadPositive(segment.DiameterMmText, out double diameterMm))
                {
                    ShowValidationError($"Revise o diametro do trecho {segment.Index}.");
                    return;
                }

                var (isValidSlope, slopeErrorMessage) = ValidationService.ValidateSlope(slopePercent);
                if (!isValidSlope)
                {
                    ShowValidationError($"Trecho {segment.Index}: {slopeErrorMessage}");
                    return;
                }

                segment.SlopePercent = slopePercent;
                segment.PipeQuantity = pipeQuantity;
                segment.DiameterMm = diameterMm;
            }

            SelectedSystemType = systemType;
            IsConfirmed = true;
            DialogResult = true;
            Close();
        }

        private void BtnCopyDown_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element ||
                element.DataContext is not ConfiguredPipeSegmentInput source)
            {
                return;
            }

            int sourceIndex = Segments.IndexOf(source);
            if (sourceIndex < 0 || sourceIndex >= Segments.Count - 1)
            {
                return;
            }

            for (int index = sourceIndex + 1; index < Segments.Count; index++)
            {
                Segments[index].PipeTypeName = source.PipeTypeName;
                Segments[index].SlopePercentText = source.SlopePercentText;
                Segments[index].PipeQuantityText = source.PipeQuantityText;
                Segments[index].DiameterMmText = source.DiameterMmText;
            }

            gridSegments.Items.Refresh();
        }

        private void EditableTextBox_GotKeyboardFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                textBox.SelectAll();
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            IsConfirmed = false;
            DialogResult = false;
            Close();
        }

        private static bool TryReadPositive(string text, out double value)
        {
            string normalized = (text ?? string.Empty).Trim().Replace(',', '.');
            return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
                value > 0.0;
        }

        private static bool TryReadPositiveInt(string text, out int value)
        {
            return int.TryParse((text ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) &&
                value > 0;
        }

        private static void ShowValidationError(string message)
        {
            MessageBox.Show(message, "Configurar trechos", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
