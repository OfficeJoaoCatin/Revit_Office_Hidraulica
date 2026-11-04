using Autodesk.Revit.DB;
using RevitSlopePlugin.Utils;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace RevitSlopePlugin.Services
{
    public sealed class SelectionNumberOverlayService : IDisposable
    {
        private const string TextTypeName = "Office Hidraulica - Numeracao Vermelha";

        private readonly Document _doc;
        private readonly View _view;
        private readonly List<ElementId> _noteIds = new List<ElementId>();
        private bool _enabled = true;

        public SelectionNumberOverlayService(Document doc, View view)
        {
            _doc = doc;
            _view = view;
        }

        public void AddNumber(FamilyInstance box, int number)
        {
            if (!_enabled)
            {
                return;
            }

            Transaction? tx = null;
            try
            {
                ElementId textTypeId = GetOrCreateRedTextTypeId();
                XYZ position = GetLabelPosition(box);

                tx = new Transaction(_doc, "Mostrar ordem de selecao");
                tx.Start();

                var options = new TextNoteOptions(textTypeId)
                {
                    HorizontalAlignment = HorizontalTextAlignment.Center,
                    VerticalAlignment = VerticalTextAlignment.Middle
                };

                TextNote note = TextNote.Create(
                    _doc,
                    _view.Id,
                    position,
                    number.ToString(CultureInfo.InvariantCulture),
                    options);

                OverrideGraphicSettings overrides = new OverrideGraphicSettings()
                    .SetProjectionLineColor(new Color(255, 0, 0));
                _view.SetElementOverrides(note.Id, overrides);

                _noteIds.Add(note.Id);
                tx.Commit();
            }
            catch (Exception ex)
            {
                if (tx != null && tx.HasStarted())
                {
                    tx.RollBack();
                }

                _enabled = false;
                Logger.Warn($"Nao foi possivel criar numeracao temporaria na vista ativa: {ex.Message}");
            }
        }

        public void Clear()
        {
            if (_noteIds.Count == 0)
            {
                return;
            }

            Transaction? tx = null;
            try
            {
                List<ElementId> validIds = _noteIds
                    .Where(id => _doc.GetElement(id) != null)
                    .ToList();

                if (validIds.Count == 0)
                {
                    _noteIds.Clear();
                    return;
                }

                tx = new Transaction(_doc, "Remover ordem de selecao");
                tx.Start();
                _doc.Delete(validIds);
                tx.Commit();
            }
            catch (Exception ex)
            {
                if (tx != null && tx.HasStarted())
                {
                    tx.RollBack();
                }

                Logger.Warn($"Nao foi possivel remover numeracao temporaria: {ex.Message}");
            }
            finally
            {
                _noteIds.Clear();
            }
        }

        public void Dispose()
        {
            Clear();
        }

        private ElementId GetOrCreateRedTextTypeId()
        {
            TextNoteType? existingType = new FilteredElementCollector(_doc)
                .OfClass(typeof(TextNoteType))
                .Cast<TextNoteType>()
                .FirstOrDefault(type => type.Name == TextTypeName);

            if (existingType != null)
            {
                ConfigureRedTextType(existingType);
                return existingType.Id;
            }

            TextNoteType? baseType = new FilteredElementCollector(_doc)
                .OfClass(typeof(TextNoteType))
                .Cast<TextNoteType>()
                .FirstOrDefault();

            if (baseType == null)
            {
                return ElementId.InvalidElementId;
            }

            TextNoteType redType = baseType;
            Transaction? tx = null;

            try
            {
                tx = new Transaction(_doc, "Criar tipo de texto vermelho");
                tx.Start();

                redType = baseType.Duplicate(TextTypeName) as TextNoteType ?? baseType;
                ConfigureTextTypeParameters(redType);

                tx.Commit();
            }
            catch (Exception ex)
            {
                if (tx != null && tx.HasStarted())
                {
                    tx.RollBack();
                }

                Logger.Warn($"Nao foi possivel criar tipo de texto vermelho: {ex.Message}");
            }

            return redType.Id;
        }

        private void ConfigureRedTextType(TextNoteType textType)
        {
            Transaction? tx = null;

            try
            {
                tx = new Transaction(_doc, "Atualizar tipo de texto vermelho");
                tx.Start();
                ConfigureTextTypeParameters(textType);
                tx.Commit();
            }
            catch (Exception ex)
            {
                if (tx != null && tx.HasStarted())
                {
                    tx.RollBack();
                }

                Logger.Warn($"Nao foi possivel atualizar tipo de texto vermelho: {ex.Message}");
            }
        }

        private void ConfigureTextTypeParameters(TextNoteType textType)
        {
            SetTextTypeParameter(textType, BuiltInParameter.TEXT_COLOR, 255);
            SetTextTypeParameter(textType, BuiltInParameter.TEXT_SIZE, GetScaledTextSizeFeet());
            SetTextTypeParameter(textType, BuiltInParameter.TEXT_STYLE_BOLD, 1);
        }

        private double GetScaledTextSizeFeet()
        {
            int viewScale = Math.Max(1, _view.Scale);
            double textSizeMm = 10.0 * viewScale / 100.0;
            return UnitConversion.MmToFt(textSizeMm);
        }

        private static void SetTextTypeParameter(Element element, BuiltInParameter builtInParameter, int value)
        {
            Parameter? parameter = element.get_Parameter(builtInParameter);
            if (parameter != null && !parameter.IsReadOnly)
            {
                parameter.Set(value);
            }
        }

        private static void SetTextTypeParameter(Element element, BuiltInParameter builtInParameter, double value)
        {
            Parameter? parameter = element.get_Parameter(builtInParameter);
            if (parameter != null && !parameter.IsReadOnly)
            {
                parameter.Set(value);
            }
        }

        private XYZ GetLabelPosition(FamilyInstance box)
        {
            XYZ center = box.Location is LocationPoint locationPoint
                ? locationPoint.Point
                : XYZ.Zero;

            BoundingBoxXYZ? bb = box.get_BoundingBox(_view) ?? box.get_BoundingBox(null);
            double offset = UnitConversion.CmToFt(30.0);

            if (bb != null)
            {
                double width = Math.Abs(bb.Max.X - bb.Min.X);
                double depth = Math.Abs(bb.Max.Y - bb.Min.Y);
                offset = Math.Max(offset, Math.Max(width, depth) * 0.6);
            }

            XYZ right = SafeNormalize(_view.RightDirection, XYZ.BasisX);
            XYZ up = SafeNormalize(_view.UpDirection, XYZ.BasisY);
            XYZ direction = SafeNormalize(right + up, XYZ.BasisX);

            return center + direction * offset;
        }

        private static XYZ SafeNormalize(XYZ vector, XYZ fallback)
        {
            try
            {
                if (vector.GetLength() > 1e-9)
                {
                    return vector.Normalize();
                }
            }
            catch
            {
            }

            return fallback;
        }
    }
}
