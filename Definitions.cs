
using Microsoft.VisualBasic;
using Microsoft.VisualBasic.FileIO;
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

        Relay, //tanh sum+bias
        Threshold,
        Multiply,
        Memory,
        Compare, //slots A & B
        //Sum up all of A and same with B
        //Compare A & B using

        MoveX,
        MoveY,
        Jitter,
        EmitSignal,
    } //1. ADD NEURON TYPE ENTRY

    public static class NeuronDicts
    {
        public static Dictionary<NFunc, List<DataField>> DataDefinitions = new()
        {
            {NFunc.Constant, new()
        {
            new(){name="value", startBit=0, bitLength=8, fieldType=FType.SignedFloat},
        }},
            {NFunc.Gradient, new()
        {
            new(){name="axis", startBit=0, bitLength=1, fieldType=FType.Int},
        }},
            {NFunc.MoveDelta, new()
        {
            new(){name="axis", startBit=0, bitLength=1, fieldType=FType.Bool},
        }},
            {NFunc.Blockage, new()
        {
            new(){name="direction", startBit=0, bitLength=3},
            new(){name="distance", startBit=3, bitLength=4},
        }},
            {NFunc.Density, new()
        {
            new(){name="radius", startBit=0, bitLength=2, maxValue=3},
        }},
            {NFunc.GetSignal, new()
        {
            new(){name="channel", startBit=0, bitLength=3},
            new(){name="detectMode", startBit=3, bitLength=3, maxValue=1}, //maxValue here is temp for more modes later
            new(){name="filterSpecies", startBit=6, bitLength=1, fieldType=FType.Bool},
        }},
            {NFunc.GeneSimilarity, new()
        {
            new(){name="direction", startBit=0, bitLength=2},
            new(){name="distance", startBit=2, bitLength=3, maxValue=4},
            new(){name="exact", startBit=5, bitLength=1, fieldType=FType.Bool},
        }},
            {NFunc.GetRandom, new()
        {
            new(){name="averageCount", startBit=0, bitLength=4, fieldType=FType.Int},
        }},
            {NFunc.Relay, new()
        {
            new(){name="bias", startBit=0, bitLength=8, fieldType=FType.SignedFloat},
        }},
            {NFunc.Threshold, new()
        {
            new(){name="threshold", startBit=0, bitLength=7, fieldType=FType.SignedFloat},
            new(){name="invert", startBit=8, bitLength=1, fieldType=FType.Bool},
            new(){name="sharpness", startBit=9, bitLength=7, fieldType=FType.Float},
        }},
            {NFunc.Multiply, new()
        {
            new(){name="grouped", startBit=0, bitLength=1, fieldType=FType.Bool},
        }},
            {NFunc.Memory, new()
        {
            new(){name="decayRate", startBit=0, bitLength=8, fieldType=FType.Float, mutateSensitivity = 0.33f},
        }},
            {NFunc.Compare, new()
        {
            new(){name="direction", startBit=0, bitLength=1, fieldType=FType.Bool},
            new(){name="sharpness", startBit=1, bitLength=8, fieldType=FType.Float},
        }},
            {NFunc.MoveX, new()
        {
            new(){name="sensitivity", startBit=0, bitLength=8, fieldType=FType.Float },
        }},
            {NFunc.MoveY, new()
        {
            new(){name="sensitivity", startBit=0, bitLength=8, fieldType=FType.Float},
        }},
            {NFunc.Jitter, new()
        {
            new(){name="sensitivity", startBit=0, bitLength=8, fieldType=FType.Float},
        }},
            {NFunc.EmitSignal, new()
        {
            new(){name="channel", startBit=0, bitLength=2, fieldType=FType.Int},
            new(){name="decayRate", startBit=2, bitLength=6, fieldType=FType.Float, mutateSensitivity=0.25f},
            new(){name="species", startBit=8, bitLength=1, fieldType=FType.Bool},
            new(){name="deltaVector", startBit=9, bitLength=1, fieldType=FType.Bool},
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
            }},
            {NType.Math,
                new(){
                    NFunc.Relay,
                    NFunc.Threshold,
                    NFunc.Multiply,
                    NFunc.Memory,
                    NFunc.Compare,
            }},
            {NType.Action,
                new(){
                    NFunc.MoveX,
                    NFunc.MoveY,
                    NFunc.Jitter,
                    NFunc.EmitSignal,
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
        
            { NFunc.Relay,NType.Math },
            { NFunc.Threshold,NType.Math },
            { NFunc.Multiply,NType.Math },
            { NFunc.Memory,NType.Math },
            { NFunc.Compare,NType.Math },
        
            { NFunc.MoveX,NType.Action },
            { NFunc.MoveY,NType.Action },
            { NFunc.Jitter,NType.Action },
            { NFunc.EmitSignal,NType.Action }
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