
namespace NEMO
{

    public class DataField
    {
        public required string name;

        public int startBit;
        public int bitLength;

        public int? maxValue;

        public float mutateSensitivity = 1f;

        public bool isSignedFloat = false;
        public bool isFloat = false;
        public bool isBool = false;
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
            new(){name="value", startBit=0, bitLength=8, isSignedFloat=true},
        }},
            {NFunc.Gradient, new()
        {
            new(){name="axis", startBit=0, bitLength=1, isBool=true},
        }},
            {NFunc.MoveDelta, new()
        {
            new(){name="axis", startBit=0, bitLength=1, isBool=true},
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
            new(){name="filterSpecies", startBit=6, bitLength=1, isBool=true},
        }},
            {NFunc.GeneSimilarity, new()
        {
            new(){name="direction", startBit=0, bitLength=2},
            new(){name="distance", startBit=2, bitLength=3, maxValue=4},
            new(){name="exact", startBit=5, bitLength=1, isBool=true},
        }},
            {NFunc.GetRandom, new()
        {

        }},
            {NFunc.Relay, new()
        {
            new(){name="bias", startBit=0, bitLength=8, isSignedFloat=true},
        }},
            {NFunc.Threshold, new()
        {
            new(){name="threshold", startBit=0, bitLength=8, isSignedFloat=true},
            new(){name="invert", startBit=8, bitLength=1, isBool=true},
        }},
            {NFunc.Multiply, new()
        {
            new(){name="grouped", startBit=0, bitLength=1, isBool=true},
        }},
            {NFunc.Memory, new()
        {
            new(){name="decayRate", startBit=0, bitLength=8, isFloat = true, mutateSensitivity = 0.33f},
        }},
            {NFunc.Compare, new()
        {
            new(){name="direction", startBit=0, bitLength=1, isBool=true},
            new(){name="sharpness", startBit=1, bitLength=8, isFloat=true},
        }},
            {NFunc.MoveX, new()
        {
            new(){name="sensitivity", startBit=0, bitLength=8, isFloat=true},
        }},
            {NFunc.MoveY, new()
        {
            new(){name="sensitivity", startBit=0, bitLength=8, isFloat=true},
        }},
            {NFunc.Jitter, new()
        {
            new(){name="sensitivity", startBit=0, bitLength=8, isFloat=true},
        }},
            {NFunc.EmitSignal, new()
        {
            new(){name="channel", startBit=0, bitLength=2},
            new(){name="decayRate", startBit=2, bitLength=6, isFloat=true, mutateSensitivity=0.25f},
            new(){name="species", startBit=8, bitLength=1, isBool=true},
            new(){name="deltaVector", startBit=9, bitLength=1, isBool=true},
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
    }
}