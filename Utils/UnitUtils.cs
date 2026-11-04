using System;

namespace RevitSlopePlugin.Utils
{
    /// <summary>
    /// Utilitários para conversão de unidades.
    /// A API do Revit trabalha internamente em pés (feet).
    /// </summary>
    public static class UnitConversion
    {
        private const double FeetToMeters = 0.3048;
        private const double MetersToFeet = 1.0 / FeetToMeters;

        /// <summary>Converte pés para metros.</summary>
        public static double FtToM(double feet) => feet * FeetToMeters;

        /// <summary>Converte metros para pés.</summary>
        public static double MToFt(double meters) => meters * MetersToFeet;

        /// <summary>Converte pés para milímetros.</summary>
        public static double FtToMm(double feet) => feet * FeetToMeters * 1000.0;

        /// <summary>Converte milímetros para pés.</summary>
        public static double MmToFt(double mm) => mm / 1000.0 * MetersToFeet;

        /// <summary>Converte centimetros para pes.</summary>
        public static double CmToFt(double cm) => cm / 100.0 * MetersToFeet;

        /// <summary>Converte pes para centimetros.</summary>
        public static double FtToCm(double feet) => feet * FeetToMeters * 100.0;

        /// <summary>
        /// Formata um valor em pés para exibição com metros entre parênteses.
        /// Exemplo: "3.28 ft (1.00 m)"
        /// </summary>
        public static string FormatFeetWithMeters(double feet, int decimals = 2)
        {
            double meters = FtToM(feet);
            return $"{feet.ToString($"F{decimals}")} ft ({meters.ToString($"F{decimals}")} m)";
        }
    }
}
