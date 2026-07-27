using System.Text.Json;

namespace NEMO
{
    public class DataField
    {
        public required string name;

        public int startBit;
        public int bitLength;

        public int? maxValue;

        public float mutateSensitivity = 1f;

        public FType fieldType;
    }
    public class DataFieldLite
    {
        public string name { get; set; }
        public string type { get; set; }
    }
    public class DataFieldLive
    {
        public string name { get; set; }
        public string type { get; set; }

        public float floatVal { get; set; }
        public int intVal { get; set; }
        public bool boolVal { get; set; }
    }
    public struct NeuronDataField
    {
        public string name;
        public FType type;

        public float floatVal;
        public int intVal;
        public bool boolVal;

        public NeuronDataField(FType type,
            float floatVal=0f, int intVal=0, bool boolVal=false)
        {
            this.type = type;
            this.name = "";

            switch (type)
            {
                case FType.Int:
                    this.intVal = intVal;
                    break;
                case FType.Float:
                    this.floatVal = floatVal;
                    break;
                case FType.SignedFloat:
                    this.floatVal = floatVal;
                    break;
                case FType.Bool:
                    this.boolVal = boolVal;
                    break;
            }
        }

        public override string ToString()
        {
            string str = "";
            if(type==FType.Float || type == FType.SignedFloat){
                str = $"{name}={floatVal}";
            }
            if(type==FType.Int){
                str = $"{name}={intVal}";
            }
            if(type==FType.Bool){
                str = $"{name}={boolVal}";
            }
            return str;
        }
    }
    public class NeuronDef
    {
        public string func { get; set; }
        public string type { get; set; }
    }

    public enum FType
    {
        Float,
        SignedFloat,
        Int,
        Bool
    }
    public enum NType : byte
    {
        Sensor,
        Math,
        Action,
    }
    public enum NFunc : byte
    {
        //Sensors
        Constant,
        GetRandom,
        Gradient,
        MoveDelta,
        Density,
        GetSignal,
        Age,
        Energy,
        Oscillator,

        //Sensor-Vision
        VisionBlockage,
        VisionProximity,
        VisionTrait,
        VisionGenSim,
        VisionKinematics,
        VisionHealth,
        VisionIsolation,

        //Math
        Relay,
        Threshold,
        Multiply,
        Memory,
        Compare,
        Amplify,
        Pulse,
        Transistor,
        Derivative,
        Divide,

        //Actions
        Move,
        Rotate,
        Jitter,
        EmitSignal,
        Consume,
        Attack
    }

    public enum PType
    {
        //reproduction
        ReproductionThreshold,
        OffspringInvestment,
        MutationVolatility,
        GestationPeriod,

        //metabolism
        CarnivoryBias,
        MetabolicRate,
        RestingEfficiency,
        ScavengerTolerance,

        //morphology
        BodyMass,
        ArmorDensity,
        SpikeCoating,
        Camouflage,
        ToxicCorpse,

        //movement
        FastTwitchMuscle,
        RotationalAgility,
        JitterEfficiency,

        //senses
        VisionAcuity,
        FovSpecialization,
        OlfactorySensitivity,
        BrainSize,

        //interaction
        Lethality,
        SocialCohesion,
        PheromoneVolume,
        ChemicalVolatility,
        Parasitism,
        Symbiosis,
    }

    public static class NeuronDicts
    {
        public static readonly List<NFunc>[] FuncsOfType = new List<NFunc>[3];
        public static readonly List<DataField>[] DataDefinitions = new List<DataField>[32];
        public static readonly NType[] TypesOfFuncs = new NType[32];

        public static readonly HashSet<NFunc> VisionNeurons = new() {
            NFunc.VisionBlockage, NFunc.VisionProximity, NFunc.VisionTrait,
            NFunc.VisionGenSim, NFunc.VisionKinematics, NFunc.VisionHealth, NFunc.VisionIsolation
        };

        static NeuronDicts()
        {
            DataDefinitions[(int)NFunc.Constant] = new() {
                new(){name="value", startBit=0, bitLength=8, fieldType=FType.SignedFloat},
            };
            DataDefinitions[(int)NFunc.GetRandom] = new() {
                new(){name="averageCount", startBit=0, bitLength=4, fieldType=FType.Int},
            };
            DataDefinitions[(int)NFunc.GetSignal] = new() {
                new(){name="channel", startBit=0, bitLength=4, maxValue=15, fieldType=FType.Int},
                new(){name="radius", startBit=4, bitLength=3, maxValue=7, fieldType=FType.Int},
            };
            DataDefinitions[(int)NFunc.MoveDelta] = new() {
                new(){name="checkRotation", startBit=0, bitLength=1, fieldType=FType.Bool},
            };
            DataDefinitions[(int)NFunc.Density] = new() {
                new(){name="targetType", startBit=0, bitLength=3, maxValue=3, fieldType=FType.Int},
                new(){name="radius", startBit=3, bitLength=3, maxValue=7, fieldType=FType.Int},
                new(){name="amplifier", startBit=6, bitLength=8, fieldType=FType.Float},
            };
            DataDefinitions[(int)NFunc.Gradient] = new() {
                new(){name="axis", startBit=0, bitLength=1, fieldType=FType.Int},
            };
            DataDefinitions[(int)NFunc.Age] = new();
            DataDefinitions[(int)NFunc.Energy] = new() {
                
            };
            DataDefinitions[(int)NFunc.Oscillator] = new() {
                new(){name="useWorldTime", startBit=0, bitLength=1, fieldType=FType.Bool},
                new(){name="speciesSync", startBit=1, bitLength=1, fieldType=FType.Bool},
                new(){name="periodScale", startBit=2, bitLength=6, maxValue=63, fieldType=FType.Int},
            };

            DataDefinitions[(int)NFunc.VisionBlockage] = new() {
                new(){name="angle", startBit=0, bitLength=3, maxValue=7, fieldType=FType.Int},
                new(){name="fov", startBit=3, bitLength=3, maxValue=4, fieldType=FType.Int},
                new(){name="distance", startBit=6, bitLength=4, maxValue=15, fieldType=FType.Int},
                new(){name="targetMode", startBit=10, bitLength=3, maxValue=7, fieldType=FType.Int},
                new(){name="steepness", startBit=13, bitLength=3, maxValue=7, fieldType=FType.Int},
            };
            DataDefinitions[(int)NFunc.VisionGenSim] = new() {
                new(){name="angle", startBit=0, bitLength=3, maxValue=7, fieldType=FType.Int},
                new(){name="fov", startBit=3, bitLength=3, maxValue=4, fieldType=FType.Int},
                new(){name="distance", startBit=6, bitLength=4, maxValue=15, fieldType=FType.Int},
                new(){name="exactMatch", startBit=10, bitLength=1, fieldType=FType.Bool},
                new(){name="massMode", startBit=11, bitLength=1, fieldType=FType.Bool},
                new(){name="steepness", startBit=12, bitLength=3, maxValue=7, fieldType=FType.Int},
            };
            DataDefinitions[(int)NFunc.VisionProximity] = new() {
                new(){name="angle", startBit=0, bitLength=3, maxValue=7, fieldType=FType.Int},
                new(){name="fov", startBit=3, bitLength=3, maxValue=4, fieldType=FType.Int},
                new(){name="distance", startBit=6, bitLength=4, maxValue=15, fieldType=FType.Int},
                new(){name="targetType", startBit=10, bitLength=2, maxValue=3, fieldType=FType.Int},
                new(){name="steepness", startBit=12, bitLength=2, maxValue=3, fieldType=FType.Int},
            };
            DataDefinitions[(int)NFunc.VisionKinematics] = new() {
                new(){name="angle", startBit=0, bitLength=3, maxValue=7, fieldType=FType.Int},
                new(){name="fov", startBit=3, bitLength=3, maxValue=4, fieldType=FType.Int},
                new(){name="distance", startBit=6, bitLength=4, maxValue=15, fieldType=FType.Int},
                new(){name="targetAction", startBit=10, bitLength=2, maxValue=3, fieldType=FType.Int},
                new(){name="steepness", startBit=12, bitLength=2, maxValue=3, fieldType=FType.Int},
            };
            DataDefinitions[(int)NFunc.VisionTrait] = new() {
                new(){name="angle", startBit=0, bitLength=3, maxValue=7, fieldType=FType.Int},
                new(){name="fov", startBit=3, bitLength=3, maxValue=4, fieldType=FType.Int},
                new(){name="distance", startBit=6, bitLength=4, maxValue=15, fieldType=FType.Int},
                new(){name="phenotype", startBit=10, bitLength=5, maxValue=31, fieldType=FType.Int},
                new(){name="steepness", startBit=15, bitLength=1, maxValue=1, fieldType=FType.Int},
            };
            DataDefinitions[(int)NFunc.VisionHealth] = new() {
                new(){name="angle", startBit=0, bitLength=3, maxValue=7, fieldType=FType.Int},
                new(){name="fov", startBit=3, bitLength=3, maxValue=4, fieldType=FType.Int},
                new(){name="distance", startBit=6, bitLength=4, maxValue=15, fieldType=FType.Int},
                new(){name="massMode", startBit=10, bitLength=1, fieldType=FType.Bool},
                new(){name="steepness", startBit=11, bitLength=2, maxValue=3, fieldType=FType.Int},
            };
            DataDefinitions[(int)NFunc.VisionIsolation] = new() {
                new(){name="angle", startBit=0, bitLength=3, maxValue=7, fieldType=FType.Int},
                new(){name="fov", startBit=3, bitLength=3, maxValue=4, fieldType=FType.Int},
                new(){name="distance", startBit=6, bitLength=4, maxValue=15, fieldType=FType.Int},
                new(){name="searchRadius", startBit=10, bitLength=3, maxValue=5, fieldType=FType.Int},
                new(){name="steepness", startBit=13, bitLength=2, maxValue=3, fieldType=FType.Int},
            };

            DataDefinitions[(int)NFunc.Relay] = new() {
                new(){name="bias", startBit=0, bitLength=8, fieldType=FType.SignedFloat, mutateSensitivity=0.33f},
            };
            DataDefinitions[(int)NFunc.Threshold] = new() {
                new(){name="threshold", startBit=0, bitLength=7, fieldType=FType.SignedFloat},
                new(){name="invert", startBit=7, bitLength=1, fieldType=FType.Bool},
                new(){name="sharpness", startBit=8, bitLength=7, fieldType=FType.Float},
            };
            DataDefinitions[(int)NFunc.Multiply] = new() {
                new(){name="grouped", startBit=0, bitLength=1, fieldType=FType.Bool},
            };
            DataDefinitions[(int)NFunc.Memory] = new() {
                new(){name="decayRate", startBit=0, bitLength=8, fieldType=FType.Float, mutateSensitivity = 0.33f},
            };
            DataDefinitions[(int)NFunc.Compare] = new() {
                new(){name="direction", startBit=0, bitLength=1, fieldType=FType.Bool},
                new(){name="sharpness", startBit=1, bitLength=8, fieldType=FType.Float},
            };
            DataDefinitions[(int)NFunc.Amplify] = new() {
                new(){name="gain", startBit=0, bitLength=8, fieldType=FType.Float},
            };
            DataDefinitions[(int)NFunc.Pulse] = new() {
                new(){name="deltaReq", startBit=0, bitLength=8, fieldType=FType.Float},
                new(){name="strength", startBit=8, bitLength=8, fieldType=FType.Float},
            };
            DataDefinitions[(int)NFunc.Transistor] = new() {
                new(){name="slotAIsGate", startBit=0, bitLength=1, fieldType=FType.Bool},
                new(){name="invertGate", startBit=1, bitLength=1, fieldType=FType.Bool}
            };
            DataDefinitions[(int)NFunc.Derivative] = new();
            DataDefinitions[(int)NFunc.Divide] = new();

            DataDefinitions[(int)NFunc.Move] = new() {
                new(){name="sensitivity", startBit=0, bitLength=8, fieldType=FType.Float},
                new(){name="absolute", startBit=8, bitLength=1, fieldType=FType.Bool},
                new(){name="absoluteXAxis", startBit=9, bitLength=1, fieldType=FType.Bool},
            };
            DataDefinitions[(int)NFunc.Rotate] = new() {
                new(){name="sensitivity", startBit=0, bitLength=8, fieldType=FType.Float},
            };
            DataDefinitions[(int)NFunc.Jitter] = new() {
                new(){name="sensitivity", startBit=0, bitLength=8, fieldType=FType.Float},
                new(){name="absolute", startBit=8, bitLength=1, fieldType=FType.Bool},
            };
            DataDefinitions[(int)NFunc.EmitSignal] = new() {
                new(){name="channel", startBit=0, bitLength=4, maxValue=15, fieldType=FType.Int},
                new(){name="decayRate", startBit=4, bitLength=6, fieldType=FType.Float, mutateSensitivity=0.25f},
            };
            DataDefinitions[(int)NFunc.Consume] = new();
            DataDefinitions[(int)NFunc.Attack] = new();



            FuncsOfType[(int)NType.Sensor] = new() {
                NFunc.Constant,
                NFunc.GetRandom,
                NFunc.VisionBlockage,
                NFunc.Gradient,
                NFunc.MoveDelta,
                NFunc.Density,
                NFunc.GetSignal,
                NFunc.VisionGenSim,
                NFunc.Age,
                NFunc.VisionProximity,
                NFunc.VisionTrait,
                NFunc.Energy,
                NFunc.Oscillator,
                NFunc.VisionKinematics,
                NFunc.VisionHealth,
                NFunc.VisionIsolation
            };
            FuncsOfType[(int)NType.Math] = new() {
                NFunc.Relay,
                NFunc.Threshold,
                NFunc.Multiply,
                NFunc.Memory,
                NFunc.Compare,
                NFunc.Amplify,
                NFunc.Pulse,
                NFunc.Transistor,
                NFunc.Derivative,
                NFunc.Divide
            };
            FuncsOfType[(int)NType.Action] = new() {
                NFunc.Move,
                NFunc.Rotate,
                NFunc.Jitter,
                NFunc.EmitSignal,
                NFunc.Consume,
                NFunc.Attack
            };



            TypesOfFuncs[(int)NFunc.Constant] = NType.Sensor;
            TypesOfFuncs[(int)NFunc.Gradient] = NType.Sensor;
            TypesOfFuncs[(int)NFunc.MoveDelta] = NType.Sensor;
            TypesOfFuncs[(int)NFunc.VisionBlockage] = NType.Sensor;
            TypesOfFuncs[(int)NFunc.Density] = NType.Sensor;
            TypesOfFuncs[(int)NFunc.GetSignal] = NType.Sensor;
            TypesOfFuncs[(int)NFunc.VisionGenSim] = NType.Sensor;
            TypesOfFuncs[(int)NFunc.GetRandom] = NType.Sensor;
            TypesOfFuncs[(int)NFunc.Age] = NType.Sensor;
            TypesOfFuncs[(int)NFunc.VisionProximity] = NType.Sensor;
            TypesOfFuncs[(int)NFunc.VisionTrait] = NType.Sensor;
            TypesOfFuncs[(int)NFunc.Energy] = NType.Sensor;
            TypesOfFuncs[(int)NFunc.Oscillator] = NType.Sensor;
            TypesOfFuncs[(int)NFunc.VisionKinematics] = NType.Sensor;
            TypesOfFuncs[(int)NFunc.VisionHealth] = NType.Sensor;
            TypesOfFuncs[(int)NFunc.VisionIsolation] = NType.Sensor;

            TypesOfFuncs[(int)NFunc.Relay] = NType.Math;
            TypesOfFuncs[(int)NFunc.Threshold] = NType.Math;
            TypesOfFuncs[(int)NFunc.Multiply] = NType.Math;
            TypesOfFuncs[(int)NFunc.Memory] = NType.Math;
            TypesOfFuncs[(int)NFunc.Compare] = NType.Math;
            TypesOfFuncs[(int)NFunc.Amplify] = NType.Math;
            TypesOfFuncs[(int)NFunc.Pulse] = NType.Math;
            TypesOfFuncs[(int)NFunc.Transistor] = NType.Math;
            TypesOfFuncs[(int)NFunc.Derivative] = NType.Math;
            TypesOfFuncs[(int)NFunc.Divide] = NType.Math;

            TypesOfFuncs[(int)NFunc.Move] = NType.Action;
            TypesOfFuncs[(int)NFunc.Rotate] = NType.Action;
            TypesOfFuncs[(int)NFunc.Jitter] = NType.Action;
            TypesOfFuncs[(int)NFunc.EmitSignal] = NType.Action;
            TypesOfFuncs[(int)NFunc.Consume] = NType.Action;
            TypesOfFuncs[(int)NFunc.Attack] = NType.Action;
        }

        public static void ExportNeuronDefs()
        {
            List<NeuronDef> defs = new();
            for (int t = 0; t < FuncsOfType.Length; t++)
            {
                NType type = (NType)t;
                foreach (NFunc func in FuncsOfType[t])
                {
                    defs.Add(new NeuronDef
                    {
                        func = func.ToString(),
                        type = type.ToString(),
                    });
                }
            }

            string json = JsonSerializer.Serialize(defs,
            new JsonSerializerOptions
            {
                WriteIndented = true
            });

            File.WriteAllText($"{Config.WebFolder}/neuronDefs.json", json);
        }

        public static void ExportDataDefs()
        {
            Dictionary<string, List<DataFieldLite>> export = new();
            for (int f = 0; f < DataDefinitions.Length; f++)
            {
                NFunc func = (NFunc)f;
                List<DataFieldLite> defs = new();

                foreach (DataField field in DataDefinitions[f])
                {
                    DataFieldLite lite = new();
                    lite.name = field.name;
                    lite.type = field.fieldType.ToString();
                    defs.Add(lite);
                }
                export.Add(func.ToString(), defs);
            }

            string json = JsonSerializer.Serialize(export,
            new JsonSerializerOptions
            {
                WriteIndented = true
            });

            File.WriteAllText($"{Config.WebFolder}/dataDefs.json", json);
        }
    }
}