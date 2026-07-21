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
    public class NeuronDataField
    {
        public string name;
        public FType type;

        public float floatVal;
        public int intVal;
        public bool boolVal;

        public NeuronDataField() { }
        public NeuronDataField(FType type,
            float floatVal=0f, int intVal=0, bool boolVal=false)
        {
            this.type = type;

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
        Constant,
        GetRandom,
        Blockage,
        Gradient,
        MoveDelta,
        Density,
        GetSignal,
        GeneSimilarity,
        Age,

        Relay,
        Threshold,
        Multiply,
        Memory,
        Compare,
        Amplify,
        Pulse,

        Move,
        Rotate,
        Jitter,
        EmitSignal,
        Consume,
        Attack,
    }

    public enum PType
    {
        //reproduction
        ReproductionThreshold,
        OffspringInvestment,
        MutationVolatility,

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
        Vampirism,
        Lethality,
        SocialCohesion,
        PheromoneVolume,
        ChemicalVolatility,
        Parasitism
    }

    public static class NeuronDicts
    {
        public static Dictionary<NFunc, List<DataField>> DataDefinitions = new()
        {
            {NFunc.Constant, new() {
                new(){name="value", startBit=0, bitLength=8, fieldType=FType.SignedFloat},
            }},
            {NFunc.GetRandom, new() {
                new(){name="averageCount", startBit=0, bitLength=4, fieldType=FType.Int},
            }},
            {NFunc.Blockage, new() {
                new(){name="angle", startBit=0, bitLength=3, maxValue=7, fieldType=FType.Int},
                new(){name="fov", startBit=3, bitLength=3, maxValue=4, fieldType=FType.Int},
                new(){name="distance", startBit=6, bitLength=4, maxValue=15, fieldType=FType.Int},
                // 0-3 = Closest (All, Food, Creature, Block). 4-7 = Mass (All, Food, Creature, Block)
                new(){name="targetMode", startBit=10, bitLength=3, maxValue=7, fieldType=FType.Int},
                new(){name="steepness", startBit=13, bitLength=3, maxValue=7, fieldType=FType.Int},
            }},
            {NFunc.GetSignal, new() {
                new(){name="channel", startBit=0, bitLength=4, maxValue=15, fieldType=FType.Int},
                new(){name="radius", startBit=4, bitLength=3, maxValue=7, fieldType=FType.Int},
            }},
            {NFunc.GeneSimilarity, new() {
                new(){name="angle", startBit=0, bitLength=3, maxValue=7, fieldType=FType.Int},
                new(){name="fov", startBit=3, bitLength=3, maxValue=4, fieldType=FType.Int}, 
                new(){name="distance", startBit=6, bitLength=4, maxValue=15, fieldType=FType.Int}, 
                new(){name="exactMatch", startBit=10, bitLength=1, fieldType=FType.Bool},
                new(){name="massMode", startBit=11, bitLength=1, fieldType=FType.Bool}, 
                new(){name="steepness", startBit=12, bitLength=3, maxValue=7, fieldType=FType.Int}, 
            }},
            {NFunc.MoveDelta, new() {
                new(){name="checkRotation", startBit=0, bitLength=1, fieldType=FType.Bool},
            }},
            {NFunc.Density, new() {
                //0 all, 1 food, 2, creature, 3 block
                new(){name="targetType", startBit=0, bitLength=3, maxValue=3, fieldType=FType.Int},
                new(){name="radius", startBit=3, bitLength=3, maxValue=7, fieldType=FType.Int},
            }},
            {NFunc.Gradient, new() {
                new(){name="axis", startBit=0, bitLength=1, fieldType=FType.Int},
            }},
            {NFunc.Age, new() {
            }},

            {NFunc.Relay, new() {
                new(){name="bias", startBit=0, bitLength=8, fieldType=FType.SignedFloat, mutateSensitivity=0.33f},
            }},
            {NFunc.Threshold, new() {
                new(){name="threshold", startBit=0, bitLength=7, fieldType=FType.SignedFloat},
                new(){name="invert", startBit=7, bitLength=1, fieldType=FType.Bool},
                new(){name="sharpness", startBit=8, bitLength=7, fieldType=FType.Float},
            }},
            {NFunc.Multiply, new() {
                new(){name="grouped", startBit=0, bitLength=1, fieldType=FType.Bool},
            }},
            {NFunc.Memory, new() {
                new(){name="decayRate", startBit=0, bitLength=8, fieldType=FType.Float, mutateSensitivity = 0.33f},
            }},
            {NFunc.Compare, new() {
                new(){name="direction", startBit=0, bitLength=1, fieldType=FType.Bool},
                new(){name="sharpness", startBit=1, bitLength=8, fieldType=FType.Float},
            }},
            {NFunc.Amplify, new() {
                new(){name="gain", startBit=0, bitLength=8, fieldType=FType.Float},
            }},
            {NFunc.Pulse, new() {
                new(){name="deltaReq", startBit=0, bitLength=8, fieldType=FType.Float},
                new(){name="strength", startBit=8, bitLength=8, fieldType=FType.Float},
            }},

            {NFunc.Move, new() {
                new(){name="sensitivity", startBit=0, bitLength=8, fieldType=FType.Float},
                new(){name="absolute", startBit=8, bitLength=1, fieldType=FType.Bool},
                new(){name="absoluteXAxis", startBit=9, bitLength=1, fieldType=FType.Bool},
            }},
            {NFunc.Rotate, new() {
                new(){name="sensitivity", startBit=0, bitLength=8, fieldType=FType.Float},
            }},
            {NFunc.Jitter, new() {
                new(){name="sensitivity", startBit=0, bitLength=8, fieldType=FType.Float},
                new(){name="absolute", startBit=8, bitLength=1, fieldType=FType.Bool},
            }},
            {NFunc.EmitSignal, new() {
                new(){name="channel", startBit=0, bitLength=4, maxValue=15, fieldType=FType.Int},
                new(){name="decayRate", startBit=4, bitLength=6, fieldType=FType.Float, mutateSensitivity=0.25f},
            }},
            {NFunc.Consume, new() {
            }},
            {NFunc.Attack, new() {
            }},
        };
        
        public static Dictionary<NType, List<NFunc>> FuncsOfType = new()
        {
            {NType.Sensor,
                new(){
                    NFunc.Constant,
                    NFunc.GetRandom,
                    NFunc.Blockage,
                    NFunc.Gradient,
                    NFunc.MoveDelta,
                    NFunc.Density,
                    NFunc.GetSignal,
                    NFunc.GeneSimilarity,
                    NFunc.Age,
            }},
            {NType.Math,
                new(){
                    NFunc.Relay,
                    NFunc.Threshold,
                    NFunc.Multiply,
                    NFunc.Memory,
                    NFunc.Compare,
                    NFunc.Amplify,
                    NFunc.Pulse,
            }},
            {NType.Action,
                new(){
                    NFunc.Move,
                    NFunc.Rotate,
                    NFunc.Jitter,
                    NFunc.EmitSignal,
                    NFunc.Consume,
                    NFunc.Attack,
            }},
        };
        public static Dictionary<NFunc, NType> TypesOfFuncs = new()
        {
            { NFunc.Constant,NType.Sensor },
            { NFunc.Gradient,NType.Sensor },
            { NFunc.MoveDelta,NType.Sensor },
            { NFunc.Blockage,NType.Sensor },
            { NFunc.Density,NType.Sensor },
            { NFunc.GetSignal,NType.Sensor },
            { NFunc.GeneSimilarity,NType.Sensor },
            { NFunc.GetRandom,NType.Sensor },
            { NFunc.Age,NType.Sensor },
        
            { NFunc.Relay,NType.Math },
            { NFunc.Threshold,NType.Math },
            { NFunc.Multiply,NType.Math },
            { NFunc.Memory,NType.Math },
            { NFunc.Compare,NType.Math },
            { NFunc.Amplify,NType.Math },
            { NFunc.Pulse,NType.Math },
        
            { NFunc.Move,NType.Action },
            { NFunc.Rotate,NType.Action },
            { NFunc.Jitter,NType.Action },
            { NFunc.EmitSignal,NType.Action },
            { NFunc.Consume,NType.Action },
            { NFunc.Attack,NType.Action }
        };

        public static void ExportNeuronDefs()
        {
            List<NeuronDef> defs = new();
            foreach (var pair in NeuronDicts.FuncsOfType)
            {
                NType type = pair.Key;
                foreach (NFunc func in pair.Value)
                {
                    defs.Add(new NeuronDef
                    {
                        func = func.ToString(),
                        type = type.ToString(),
                    });
                }
            }

            string json =JsonSerializer.Serialize(defs,
            new JsonSerializerOptions{
                WriteIndented = true
            });

            File.WriteAllText($"{Config.GraphOutputFolder}neuronDefs.json",json
            );
        }
        public static void ExportDataDefs()
        {
            Dictionary<string, List<DataFieldLite>> export = new();
            foreach (var pair in DataDefinitions)
            {
                NFunc func = pair.Key;
                List<DataFieldLite> defs =new();

                foreach (DataField field in pair.Value)
                {
                    DataFieldLite lite = new();
                    lite.name = field.name;
                    lite.type = field.fieldType.ToString();
                    defs.Add(lite);
                }
                export.Add(func.ToString(), defs);
            }

            string json =JsonSerializer.Serialize(export,
            new JsonSerializerOptions{
                WriteIndented = true
            });

            File.WriteAllText($"{Config.GraphOutputFolder}dataDefs.json",json
            );
        }
    }
}