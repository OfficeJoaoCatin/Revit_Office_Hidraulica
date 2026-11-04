using System;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using Autodesk.Revit.DB;
using RevitSlopePlugin.Services;

namespace RevitSlopePlugin.UI
{
    /// <summary>
    /// Code-behind da janela WPF para input da inclinação.
    /// Exibe informações do elemento base e coleta a inclinação desejada.
    /// </summary>
    public partial class SlopeInputWindow : Window
    {
        /// <summary>
        /// Inclinação em porcentagem informada pelo usuário.
        /// Válido apenas quando DialogResult = true.
        /// </summary>
        public double SlopePercent { get; private set; }

        /// <summary>
        /// Indica se o usuário confirmou a aplicação.
        /// </summary>
        public bool IsConfirmed { get; private set; }

        /// <summary>
        /// Construtor. Recebe informações do elemento base para exibição.
        /// </summary>
        /// <param name="baseElementInfo">Texto descritivo do elemento base selecionado.</param>
        public SlopeInputWindow(string baseElementInfo)
        {
            InitializeComponent();
            txtBaseElementInfo.Text = baseElementInfo;

            // Focar no campo de inclinação ao abrir
            Loaded += (s, e) => txtSlope.Focus();
        }

        /// <summary>
        /// Seleciona todo o texto ao receber foco (facilita edição).
        /// </summary>
        private void TxtSlope_GotFocus(object sender, RoutedEventArgs e)
        {
            txtSlope.SelectAll();
        }

        /// <summary>
        /// Botão Aplicar: valida input e fecha a janela com confirmação.
        /// </summary>
        private void BtnApply_Click(object sender, RoutedEventArgs e)
        {
            // Tentar parsear o valor (aceitar vírgula ou ponto como separador decimal)
            string input = txtSlope.Text.Trim().Replace(',', '.');
            
            if (!double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out double slope))
            {
                ShowValidationError("Valor inválido. Digite um número válido para a inclinação.\nExemplo: 2.0");
                txtSlope.Focus();
                txtSlope.SelectAll();
                return;
            }

            // Validar usando o serviço
            var (isValid, errorMessage) = ValidationService.ValidateSlope(slope);
            if (!isValid)
            {
                ShowValidationError(errorMessage);
                txtSlope.Focus();
                txtSlope.SelectAll();
                return;
            }

            SlopePercent = slope;
            IsConfirmed = true;
            DialogResult = true;
            Close();
        }

        /// <summary>
        /// Botão Cancelar: fecha a janela sem confirmação.
        /// </summary>
        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            IsConfirmed = false;
            DialogResult = false;
            Close();
        }

        /// <summary>
        /// Exibe mensagem de erro de validação.
        /// </summary>
        private void ShowValidationError(string message)
        {
            MessageBox.Show(
                message,
                "Validação",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }
}
