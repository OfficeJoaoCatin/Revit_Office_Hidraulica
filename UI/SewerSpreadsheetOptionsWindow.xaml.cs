using RevitSlopePlugin.Models;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace RevitSlopePlugin.UI
{
    public partial class SewerSpreadsheetOptionsWindow : Window
    {
        public SewerSpreadsheetOptionsWindow(
            IEnumerable<string> sheetNames,
            IEnumerable<SewerSpreadsheetMapping> mappings)
        {
            InitializeComponent();

            cmbSheets.ItemsSource = sheetNames.ToList();
            cmbMappings.ItemsSource = mappings.ToList();

            if (cmbSheets.Items.Count > 0)
            {
                cmbSheets.SelectedIndex = 0;
            }

            if (cmbMappings.Items.Count > 0)
            {
                cmbMappings.SelectedIndex = 0;
            }
        }

        public string SelectedSheetName { get; private set; } = string.Empty;
        public SewerSpreadsheetMapping? SelectedMapping { get; private set; }
        public bool IsConfirmed { get; private set; }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            if (cmbSheets.SelectedItem is not string sheetName ||
                cmbMappings.SelectedItem is not SewerSpreadsheetMapping mapping)
            {
                MessageBox.Show(
                    "Selecione a aba e o mapeamento.",
                    "Verificar Planilha",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            SelectedSheetName = sheetName;
            SelectedMapping = mapping;
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
    }
}
