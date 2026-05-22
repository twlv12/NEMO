
namespace NEMO
{
    public class GeneField
    {
        public string name;
        public int bitLength;
        public ushort maxValue;

        public GeneField(string name, int bitLength)
        {
            this.name = name;
            this.bitLength = bitLength;

            if (name == "srcType" || name == "tgtType")
            {
                this.maxValue = (ushort)(Enum.GetNames(typeof(NType)).Length - 1);
            }
            if (name == "srcFunc" || name == "tgtFunc")
            {
                this.maxValue = (ushort)(Enum.GetNames(typeof(NFunc)).Length - 1);
            }
            else { this.maxValue = (ushort)Math.Pow(2, bitLength); }
        }
    }

    public class DataField
    {
        public required string name;

        public int startBit;
        public int bitLength;

        public int? maxValue;
    }

    public class NeuronDict
    {
        public Dictionary<NFunc, List<DataField>> NeuronDefinitions;

        public NeuronDict()
        {
            NeuronDefinitions = new Dictionary<NFunc, List<DataField>>();

            //SENSOR
            NeuronDefinitions.Add(
                NFunc.Constant, new List<DataField>
                {
                    new DataField
                    {
                        name = "value",
                        startBit = 0,
                        bitLength = 8,
                    }
                });
            NeuronDefinitions.Add(
                NFunc.Blockage, new List<DataField>
                {
                    new DataField
                    {
                        name = "direction",
                        startBit = 0,
                        bitLength = 3,
                    },
                    new DataField
                    {
                        name = "distance",
                        startBit = 5,
                        bitLength = 4,
                    },
                });
            NeuronDefinitions.Add(
                NFunc.Gradient, new List<DataField>
                {
                    new DataField
                    {
                        name = "axis",
                        startBit = 0,
                        bitLength = 1,
                    },
                });
            NeuronDefinitions.Add(
                NFunc.MoveDelta, new List<DataField>
                {
                    new DataField
                    {
                        name = "axis",
                        startBit = 0,
                        bitLength = 1,
                    },
                });
            NeuronDefinitions.Add(
                NFunc.Density, new List<DataField>
                {
                    new DataField
                    {
                        name = "radius",
                        startBit = 0,
                        bitLength = 2,
                        maxValue = 3,
                    },
                });
            NeuronDefinitions.Add(
                NFunc.GetSignal, new List<DataField>
                {
                    new DataField
                    {
                        name = "channel",
                        startBit = 0,
                        bitLength = 3,
                    },
                    new DataField
                    {
                        name = "detectMode",
                        startBit = 3,
                        bitLength = 3,
                        maxValue = 1,
                    },
                    new DataField
                    {
                        name = "filterSpecies",
                        startBit = 6,
                        bitLength = 1,
                    },
                });
            NeuronDefinitions.Add(
                NFunc.GeneSimilarity, new List<DataField>
                {
                    new DataField
                    {
                        name = "direction",
                        startBit = 0,
                        bitLength = 2,
                    },
                    new DataField
                    {
                        name = "distance",
                        startBit = 2,
                        bitLength = 3,
                        maxValue = 4,
                    },
                    new DataField
                    {
                        name = "exact",
                        startBit = 5,
                        bitLength = 1,
                    },
                });

            //MATH
            NeuronDefinitions.Add(
                NFunc.Relay, new List<DataField>
                {
                    new DataField
                    {
                        name = "bias",
                        startBit = 0,
                        bitLength = 8,
                    },
                });
            NeuronDefinitions.Add(
                NFunc.Threshold, new List<DataField>
                {
                    new DataField
                    {
                        name = "threshold",
                        startBit = 0,
                        bitLength = 8,
                    },
                    new DataField
                    {
                        name = "invert",
                        startBit = 8,
                        bitLength = 1,
                    },
                });
            NeuronDefinitions.Add(
                NFunc.Multiply, new List<DataField>
                {
                    new DataField
                    {
                        name = "mode",
                        startBit = 0,
                        bitLength = 1,
                    },
                });
            NeuronDefinitions.Add(
                NFunc.Memory, new List<DataField>
                {
                    new DataField
                    {
                        name = "decayRate",
                        startBit = 0,
                        bitLength = 8,
                    },
                    new DataField
                    {
                        name = "mode",
                        startBit = 8,
                        bitLength = 1,
                    },
                });
            NeuronDefinitions.Add(
                NFunc.Compare, new List<DataField>
                {
                    new DataField
                    {
                        name = "direction",
                        startBit = 0,
                        bitLength = 1,
                    },
                    new DataField
                    {
                        name = "sharpness",
                        startBit = 1,
                        bitLength = 8,
                    },
                });

            //ACTION
            NeuronDefinitions.Add(
                NFunc.MoveX, new List<DataField>
                {
                    new DataField
                    {
                        name = "sensitivity",
                        startBit = 0,
                        bitLength = 8,
                    },
                });
            NeuronDefinitions.Add(
                NFunc.MoveY, new List<DataField>
                {
                    new DataField
                    {
                        name = "sensitivity",
                        startBit = 0,
                        bitLength = 8,
                    },
                });
            NeuronDefinitions.Add(
                NFunc.Jitter, new List<DataField>
                {
                    new DataField
                    {
                        name = "sensitivity",
                        startBit = 0,
                        bitLength = 8,
                    },
                });
            NeuronDefinitions.Add(
                NFunc.EmitSignal, new List<DataField>
                {
                    new DataField
                    {
                        name = "channel",
                        startBit = 0,
                        bitLength = 2,
                    },
                    new DataField
                    {
                        name = "decayRate",
                        startBit = 2,
                        bitLength = 6,
                    },
                    new DataField
                    {
                        name = "depositSpecies",
                        startBit = 8,
                        bitLength = 1,
                    },
                    new DataField
                    {
                        name = "depositVector",
                        startBit = 9,
                        bitLength = 1,
                    },
                });
        }
    }
}