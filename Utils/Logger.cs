using Autodesk.Revit.UI;
using System;
using System.IO;

namespace RevitSlopePlugin.Utils
{
    /// <summary>
    /// Logger básico para o plugin. Grava mensagens em arquivo e/ou exibe via TaskDialog.
    /// O arquivo de log é criado na pasta do usuário.
    /// </summary>
    public static class Logger
    {
        private static readonly string LogFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RevitSlopePlugin",
            "RevitSlopePlugin.log");

        private static bool _initialized = false;

        /// <summary>
        /// Inicializa o logger criando o diretório se necessário.
        /// </summary>
        private static void EnsureInitialized()
        {
            if (_initialized) return;

            try
            {
                string? dir = Path.GetDirectoryName(LogFilePath);
                if (dir != null && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                _initialized = true;
            }
            catch
            {
                // Silenciosamente ignora erro de criação de diretório
            }
        }

        /// <summary>
        /// Registra uma mensagem informativa no log.
        /// </summary>
        public static void Info(string message)
        {
            WriteLog("INFO", message);
        }

        /// <summary>
        /// Registra um aviso no log.
        /// </summary>
        public static void Warn(string message)
        {
            WriteLog("WARN", message);
        }

        /// <summary>
        /// Registra um erro no log, opcionalmente com exceção.
        /// </summary>
        public static void Error(string message, Exception? ex = null)
        {
            string fullMessage = ex != null
                ? $"{message} | Exception: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}"
                : message;
            WriteLog("ERROR", fullMessage);
        }

        /// <summary>
        /// Escreve uma entrada no arquivo de log com timestamp.
        /// </summary>
        private static void WriteLog(string level, string message)
        {
            EnsureInitialized();

            try
            {
                string entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}";
                File.AppendAllText(LogFilePath, entry + Environment.NewLine);
                System.Diagnostics.Debug.WriteLine(entry);
            }
            catch
            {
                // Nunca deixar o logger causar crash no plugin
            }
        }

        /// <summary>
        /// Exibe uma mensagem de erro ao usuário via TaskDialog do Revit.
        /// </summary>
        public static void ShowError(string title, string message)
        {
            TaskDialog dialog = new TaskDialog(title)
            {
                MainInstruction = title,
                MainContent = message,
                MainIcon = TaskDialogIcon.TaskDialogIconError
            };
            dialog.Show();
        }

        /// <summary>
        /// Exibe uma mensagem informativa ao usuário via TaskDialog do Revit.
        /// </summary>
        public static void ShowInfo(string title, string message)
        {
            TaskDialog dialog = new TaskDialog(title)
            {
                MainInstruction = title,
                MainContent = message,
                MainIcon = TaskDialogIcon.TaskDialogIconInformation
            };
            dialog.Show();
        }

        /// <summary>
        /// Exibe um aviso ao usuário via TaskDialog do Revit.
        /// </summary>
        public static void ShowWarning(string title, string message)
        {
            TaskDialog dialog = new TaskDialog(title)
            {
                MainInstruction = title,
                MainContent = message,
                MainIcon = TaskDialogIcon.TaskDialogIconWarning
            };
            dialog.Show();
        }
    }
}
