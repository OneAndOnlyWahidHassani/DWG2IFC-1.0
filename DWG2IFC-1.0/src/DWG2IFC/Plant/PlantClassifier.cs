using System;

namespace Plant3D.IfcExporter.Plant
{
    public enum IfcEntityType
    {
        IfcPipeSegment,
        IfcPipeFitting,
        IfcValve,
        IfcPump,
        IfcFlowMovingDevice,
        IfcEnergyConversionDevice,
        IfcDiscreteAccessory,
        IfcFastener,
        IfcBeam,
        IfcColumn,
        IfcMember,
        IfcBuildingElementProxy
    }

    public class ClassificationResult
    {
        public IfcEntityType EntityType { get; set; } = IfcEntityType.IfcBuildingElementProxy;
        public string PredefinedType { get; set; } = "$";
        public string UserDefinedType { get; set; } = string.Empty;
    }

    public static class PlantClassifier
    {
        public static ClassificationResult Classify(string className, string partType, string partSubType, string description, string layer = "")
        {
            var result = new ClassificationResult();
            var combined = $"{className} {partType} {partSubType} {description} {layer}".ToUpperInvariant();

            // 1. Pipe Segment (Трубы)
            if ((combined.Contains("PIPE") || combined.Contains("ТРУБ")) && 
                !combined.Contains("FITTING") && !combined.Contains("ФИТИНГ") && 
                !combined.Contains("SUPPORT") && !combined.Contains("ОПОР"))
            {
                result.EntityType = IfcEntityType.IfcPipeSegment;
                result.PredefinedType = ".RIGIDSEGMENT.";
                return result;
            }

            // 2. Pipe Fittings (Отводы, переходы, тройники, фланцы)
            if (combined.Contains("ELBOW") || combined.Contains("BEND") || combined.Contains("MITRE") || combined.Contains("ОТВОД"))
            {
                result.EntityType = IfcEntityType.IfcPipeFitting;
                result.PredefinedType = ".BEND.";
                return result;
            }
            if (combined.Contains("TEE") || combined.Contains("CROSS") || combined.Contains("WYE") || combined.Contains("ТРОЙНИК") || combined.Contains("КРЕСТОВИН"))
            {
                result.EntityType = IfcEntityType.IfcPipeFitting;
                result.PredefinedType = ".JUNCTION.";
                return result;
            }
            if (combined.Contains("REDUCER") || combined.Contains("CONCENTRIC") || combined.Contains("ECCENTRIC") || combined.Contains("ПЕРЕХОД"))
            {
                result.EntityType = IfcEntityType.IfcPipeFitting;
                result.PredefinedType = ".TRANSITION.";
                return result;
            }
            if (combined.Contains("FLANGE") || combined.Contains("COUPLING") || combined.Contains("UNION") ||
                combined.Contains("OLET") || combined.Contains("WELDOLET") || combined.Contains("SOCKOLET") ||
                combined.Contains("THREADOLET") || combined.Contains("NIPPLE") || combined.Contains("CAP") ||
                combined.Contains("ФЛАНЕЦ") || combined.Contains("МУФТА") || combined.Contains("ЗАГЛУШК") || combined.Contains("НИППЕЛЬ"))
            {
                result.EntityType = IfcEntityType.IfcPipeFitting;
                result.PredefinedType = ".CONNECTOR.";
                return result;
            }

            // 3. Valves & Flow Controllers (Арматура, краны, задвижки, клапаны)
            if (combined.Contains("VALVE") || combined.Contains("GATE") || combined.Contains("GLOBE") ||
                combined.Contains("BALL") || combined.Contains("BUTTERFLY") || combined.Contains("CHECK") ||
                combined.Contains("АРМАТУР") || combined.Contains("КЛАПАН") || combined.Contains("КРАН") || combined.Contains("ЗАДВИЖК") || combined.Contains("ВЕНТИЛ"))
            {
                result.EntityType = IfcEntityType.IfcValve;
                result.PredefinedType = ".NOTDEFINED.";
                return result;
            }

            // 4. Pumps & Flow Moving (Насосы, компрессоры, вентиляторы)
            if (combined.Contains("PUMP") || combined.Contains("НАСОС"))
            {
                result.EntityType = IfcEntityType.IfcPump;
                result.PredefinedType = ".NOTDEFINED.";
                return result;
            }
            if (combined.Contains("COMPRESSOR") || combined.Contains("FAN") || combined.Contains("BLOWER") ||
                combined.Contains("КОМПРЕССОР") || combined.Contains("ВЕНТИЛЯТОР"))
            {
                result.EntityType = IfcEntityType.IfcFlowMovingDevice;
                result.PredefinedType = ".NOTDEFINED.";
                return result;
            }

            // 5. Equipment (Емкости, аппараты, теплообменники)
            if (combined.Contains("TANK") || combined.Contains("VESSEL") || combined.Contains("EXCHANGER") ||
                combined.Contains("HEATER") || (combined.Contains("COLUMN") && combined.Contains("VESSEL")) ||
                combined.Contains("РЕЗЕРВУАР") || combined.Contains("ЕМКОСТ") || combined.Contains("ТЕПЛООБМЕН") || combined.Contains("АППАРАТ") || combined.Contains("ОБОРУД"))
            {
                result.EntityType = IfcEntityType.IfcEnergyConversionDevice;
                result.PredefinedType = ".NOTDEFINED.";
                return result;
            }

            // 6. Pipe Supports & Fasteners (Опоры, подвески, метизы)
            if (combined.Contains("SUPPORT") || combined.Contains("HANGER") || combined.Contains("SHOE") || combined.Contains("GUIDE") ||
                combined.Contains("ОПОР") || combined.Contains("ПОДВЕСК"))
            {
                result.EntityType = IfcEntityType.IfcDiscreteAccessory;
                result.PredefinedType = ".NOTDEFINED.";
                return result;
            }
            if (combined.Contains("BOLT") || combined.Contains("GASKET") || combined.Contains("NUT") ||
                combined.Contains("БОЛТ") || combined.Contains("ГАЙК") || combined.Contains("ПРОКЛАДК") || combined.Contains("КРЕПЕЖ"))
            {
                result.EntityType = IfcEntityType.IfcFastener;
                result.PredefinedType = ".NOTDEFINED.";
                return result;
            }

            // 7. Structural steel (Металлоконструкции, балки, колонны)
            if (combined.Contains("BEAM") || combined.Contains("БАЛК") || combined.Contains("ПРОГОН"))
            {
                result.EntityType = IfcEntityType.IfcBeam;
                result.PredefinedType = ".BEAM.";
                return result;
            }
            if (combined.Contains("COLUMN") || combined.Contains("POST") || combined.Contains("КОЛОНН") || combined.Contains("СТОЙК"))
            {
                result.EntityType = IfcEntityType.IfcColumn;
                result.PredefinedType = ".COLUMN.";
                return result;
            }
            if (combined.Contains("BRACE") || combined.Contains("STRUCTURAL") || combined.Contains("ФЕРМ") || combined.Contains("СВЯЗ"))
            {
                result.EntityType = IfcEntityType.IfcMember;
                result.PredefinedType = ".NOTDEFINED.";
                return result;
            }

            // Default
            result.EntityType = IfcEntityType.IfcBuildingElementProxy;
            result.PredefinedType = ".NOTDEFINED.";
            return result;
        }
    }
}
