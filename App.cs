using Autodesk.Revit.UI;
using RevitSlopePlugin.Utils;
using System;
using System.Linq;
using System.Reflection;

namespace RevitSlopePlugin
{
    public class App : IExternalApplication
    {
        private const string TabName = "Office - Hidraulica";
        private const string ToolsPanelName = "Esgoto Sanitario";
        private const string SettingsPanelName = "Configurações";
        private const string ApplySlopeButtonName = "ApplySlopeCommand";
        private const string CreateBoxPipeButtonName = "CreatePipeBetweenBoxesCommand";
        private const string CreateMainBranchSequenceButtonName = "CreateMainBranchPipeThroughBoxesCommand";
        private const string CreateSecondaryBranchSequenceButtonName = "CreateSecondaryBranchPipeThroughBoxesCommand";
        private const string CreateConfiguredPipeSegmentsButtonName = "CreateConfiguredPipeSegmentsCommand";
        private const string AlignBoxesTopographyButtonName = "AlignBoxesToLinkedTopographyCommand";
        private const string VerifySewerSpreadsheetButtonName = "VerifySewerSpreadsheetCommand";
        private const string BoxPipeSettingsButtonName = "BoxPipeSettingsCommand";
        private static readonly bool ShowVerifySewerSpreadsheetButton = false;

        public Result OnStartup(UIControlledApplication application)
        {
            CreateRibbonTab(application);
            RibbonPanel settingsPanel = GetOrCreateRibbonPanel(application, SettingsPanelName);
            RibbonPanel toolsPanel = GetOrCreateRibbonPanel(application, ToolsPanelName);

            string assemblyPath = Assembly.GetExecutingAssembly().Location;

            if (!toolsPanel.GetItems().Any(item => item.Name == ApplySlopeButtonName))
            {
                var buttonData = new PushButtonData(
                    ApplySlopeButtonName,
                    "Aplicar\nInclinacao",
                    assemblyPath,
                    "RevitSlopePlugin.Commands.ApplySlopeCommand")
                {
                    ToolTip = "Aplica inclinacao automatica em redes hidraulicas de esgoto sanitario.",
                    LongDescription = "Selecione o elemento base da rede, informe a inclinacao desejada e o plugin ajusta os elementos conectados mantendo o ponto base fixo.",
                    Image = RibbonIconFactory.Create(RibbonIconKind.ApplySlope, 16),
                    LargeImage = RibbonIconFactory.Create(RibbonIconKind.ApplySlope, 32)
                };

                toolsPanel.AddItem(buttonData);
            }

            if (!toolsPanel.GetItems().Any(item => item.Name == CreateBoxPipeButtonName))
            {
                var buttonData = new PushButtonData(
                    CreateBoxPipeButtonName,
                    "Tubo entre\nCaixas",
                    assemblyPath,
                    "RevitSlopePlugin.Commands.CreatePipeBetweenBoxesCommand")
                {
                    ToolTip = "Cria uma tubulacao entre duas caixas sem conectores.",
                    LongDescription = "Seleciona caixa de origem e destino, calcula as cotas pelo topo e pelo parametro Altura, ajusta a caixa destino e cria o tubo com inclinacao.",
                    Image = RibbonIconFactory.Create(RibbonIconKind.BoxPipe, 16),
                    LargeImage = RibbonIconFactory.Create(RibbonIconKind.BoxPipe, 32)
                };

                toolsPanel.AddItem(buttonData);
            }

            if (!toolsPanel.GetItems().Any(item => item.Name == CreateMainBranchSequenceButtonName))
            {
                var buttonData = new PushButtonData(
                    CreateMainBranchSequenceButtonName,
                    "Ramal\nPrincipal",
                    assemblyPath,
                    "RevitSlopePlugin.Commands.CreatePipeThroughBoxesCommand")
                {
                    ToolTip = "Cria tubulacoes do ramal principal entre varias caixas em sequencia.",
                    LongDescription = "Seleciona caixas uma a uma, calcula cada trecho pela mesma regra do Tubo entre Caixas, ajusta as alturas intermediarias e cria os tubos consecutivos.",
                    Image = RibbonIconFactory.Create(RibbonIconKind.MainBranchSequence, 16),
                    LargeImage = RibbonIconFactory.Create(RibbonIconKind.MainBranchSequence, 32)
                };

                toolsPanel.AddItem(buttonData);
            }

            if (!toolsPanel.GetItems().Any(item => item.Name == CreateSecondaryBranchSequenceButtonName))
            {
                var buttonData = new PushButtonData(
                    CreateSecondaryBranchSequenceButtonName,
                    "Ramal\nSecundario",
                    assemblyPath,
                    "RevitSlopePlugin.Commands.CreateSecondaryBranchPipeThroughBoxesCommand")
                {
                    ToolTip = "Cria tubulacoes do ramal secundario preservando a ultima caixa selecionada.",
                    LongDescription = "Seleciona caixas em sequencia, ajusta as caixas intermediarias e cria o ultimo trecho sem alterar a altura da ultima caixa, ignorando a folga de chegada nesse ponto.",
                    Image = RibbonIconFactory.Create(RibbonIconKind.SecondaryBranchSequence, 16),
                    LargeImage = RibbonIconFactory.Create(RibbonIconKind.SecondaryBranchSequence, 32)
                };

                toolsPanel.AddItem(buttonData);
            }

            if (!toolsPanel.GetItems().Any(item => item.Name == CreateConfiguredPipeSegmentsButtonName))
            {
                var buttonData = new PushButtonData(
                    CreateConfiguredPipeSegmentsButtonName,
                    "Tubos por\nTrecho",
                    assemblyPath,
                    "RevitSlopePlugin.Commands.CreateConfiguredPipeSegmentsCommand")
                {
                    ToolTip = "Cria tubulacoes em sequencia com inclinacao, quantidade e diametro definidos por trecho.",
                    LongDescription = "Seleciona caixas em sequencia, mostra a ordem no modelo e abre uma janela para configurar cada trecho individualmente antes de criar os tubos.",
                    Image = RibbonIconFactory.Create(RibbonIconKind.ConfiguredSegments, 16),
                    LargeImage = RibbonIconFactory.Create(RibbonIconKind.ConfiguredSegments, 32)
                };

                toolsPanel.AddItem(buttonData);
            }

            if (!toolsPanel.GetItems().Any(item => item.Name == AlignBoxesTopographyButtonName))
            {
                var buttonData = new PushButtonData(
                    AlignBoxesTopographyButtonName,
                    "Alinhar\nTopografia",
                    assemblyPath,
                    "RevitSlopePlugin.Commands.AlignBoxesToLinkedTopographyCommand")
                {
                    ToolTip = "Alinha o topo de caixas de inspecao a topografia de um Revit Link.",
                    LongDescription = "Seleciona caixas no modelo atual, seleciona o Revit Link de referencia, consulta a topografia vinculada considerando a transformacao do link e move cada caixa verticalmente para alinhar seu topo ao terreno.",
                    Image = RibbonIconFactory.Create(RibbonIconKind.AlignTopography, 16),
                    LargeImage = RibbonIconFactory.Create(RibbonIconKind.AlignTopography, 32)
                };

                toolsPanel.AddItem(buttonData);
            }

            if (ShowVerifySewerSpreadsheetButton &&
                !toolsPanel.GetItems().Any(item => item.Name == VerifySewerSpreadsheetButtonName))
            {
                var buttonData = new PushButtonData(
                    VerifySewerSpreadsheetButtonName,
                    "Verificar\nPlanilha",
                    assemblyPath,
                    "RevitSlopePlugin.Commands.VerifySewerSpreadsheetCommand")
                {
                    ToolTip = "Verifica a planilha de calculo de esgoto contra o modelo Revit.",
                    LongDescription = "Seleciona uma planilha xlsx, escolhe aba e mapeamento, localiza caixas por familia e numero, grava a extensao dos trechos e exibe pendencias/divergencias para conferencia.",
                    Image = RibbonIconFactory.Create(RibbonIconKind.VerifySpreadsheet, 16),
                    LargeImage = RibbonIconFactory.Create(RibbonIconKind.VerifySpreadsheet, 32)
                };

                toolsPanel.AddItem(buttonData);
            }

            if (!settingsPanel.GetItems().Any(item => item.Name == BoxPipeSettingsButtonName))
            {
                var buttonData = new PushButtonData(
                    BoxPipeSettingsButtonName,
                    "Configurações",
                    assemblyPath,
                    "RevitSlopePlugin.Commands.BoxPipeSettingsCommand")
                {
                    ToolTip = "Configura tipo, sistema, diametro, folgas e limites para tubo entre caixas.",
                    LongDescription = "Define as regras usadas pelo comando Tubo entre Caixas, separadas da inclinacao de redes existentes.",
                    Image = RibbonIconFactory.Create(RibbonIconKind.Settings, 16),
                    LargeImage = RibbonIconFactory.Create(RibbonIconKind.Settings, 32)
                };

                settingsPanel.AddItem(buttonData);
            }

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }

        private static void CreateRibbonTab(UIControlledApplication application)
        {
            try
            {
                application.CreateRibbonTab(TabName);
            }
            catch (ArgumentException)
            {
                // A aba ja pode existir se outro add-in do mesmo conjunto a criou.
            }
        }

        private static RibbonPanel GetOrCreateRibbonPanel(
            UIControlledApplication application,
            string panelName)
        {
            RibbonPanel? existingPanel = application
                .GetRibbonPanels(TabName)
                .FirstOrDefault(panel => panel.Name == panelName);

            return existingPanel ?? application.CreateRibbonPanel(TabName, panelName);
        }
    }
}
