using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitSlopePlugin.Models;
using RevitSlopePlugin.Services;
using RevitSlopePlugin.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace RevitSlopePlugin.UI
{
    public partial class SewerSpreadsheetResultsWindow : Window
    {
        private readonly RevitSelectionExternalEventHandler _selectionHandler;
        private readonly ExternalEvent _selectionEvent;
        private readonly string _workbookPath;
        private readonly string _sheetName;
        private readonly IReadOnlyDictionary<(int Row, int Column), double> _extensionsToWrite;
        private readonly int _segmentCount;
        private readonly int _pendingWriteCount;

        public SewerSpreadsheetResultsWindow(
            UIApplication uiApp,
            IReadOnlyCollection<SewerSpreadsheetCheckItem> items,
            int segmentCount,
            int pendingWriteCount,
            string workbookPath,
            string sheetName,
            IReadOnlyDictionary<(int Row, int Column), double> extensionsToWrite)
        {
            _selectionHandler = new RevitSelectionExternalEventHandler();
            _selectionEvent = ExternalEvent.Create(_selectionHandler);
            _workbookPath = workbookPath;
            _sheetName = sheetName;
            _extensionsToWrite = extensionsToWrite;
            _segmentCount = segmentCount;
            _pendingWriteCount = pendingWriteCount;
            InitializeComponent();

            txtSummary.Text = $"Trechos lidos: {segmentCount} | Extensoes pendentes: {pendingWriteCount} | Itens: {items.Count}";
            btnWriteSpreadsheet.IsEnabled = pendingWriteCount > 0;
            gridResults.ItemsSource = items.ToList();

            Owner = HwndSource.FromHwnd(uiApp.MainWindowHandle)?.RootVisual as Window;
            new WindowInteropHelper(this).Owner = uiApp.MainWindowHandle;
        }

        public bool ShouldWriteSpreadsheet { get; private set; }

        private void GridResults_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (gridResults.SelectedItem is not SewerSpreadsheetCheckItem item ||
                item.ElementIds.Count == 0)
            {
                return;
            }

            try
            {
                _selectionHandler.RequestSelection(item.ElementIds);
                _selectionEvent.Raise();
            }
            catch (Exception ex)
            {
                Logger.Warn($"Nao foi possivel navegar para o item da planilha: {ex.Message}");
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void BtnWriteSpreadsheet_Click(object sender, RoutedEventArgs e)
        {
            MessageBoxResult result = MessageBox.Show(
                $"Gravar {_pendingWriteCount} extensoes pendentes na planilha?",
                "Atualizar planilha",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                var workbookService = new XlsxWorkbookService();
                workbookService.WriteNumbers(_workbookPath, _sheetName, _extensionsToWrite);

                ShouldWriteSpreadsheet = true;
                btnWriteSpreadsheet.IsEnabled = false;
                txtSummary.Text = $"Trechos lidos: {_segmentCount} | Extensoes pendentes: 0 | Itens: {gridResults.Items.Count}";

                MessageBox.Show(
                    $"{_pendingWriteCount} extensoes foram gravadas na planilha.",
                    "Atualizar planilha",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Logger.Error("Erro ao atualizar planilha de esgoto", ex);
                MessageBox.Show(
                    $"Nao foi possivel atualizar a planilha:\n\n{ex.Message}",
                    "Atualizar planilha",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _selectionEvent.Dispose();
            base.OnClosed(e);
        }
    }
}
