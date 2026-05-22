
namespace NEMO
{
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
    }

    public class Utils
    {
        public static Genome GenerateGenome(int length)
        {
            Genome genome = new Genome(new List<Gene>());

            for (int i = 0; i < length; i++)
            {
                List<GeneField> template = new List<GeneField> 
                { 
                    new GeneField("srcType", 2),
                    new GeneField("srcFunc", 6),
                    new GeneField("srcID", 8),
                    new GeneField("srcData", 16),

                    new GeneField("tgtType", 2),
                    new GeneField("tgtFunc", 6),
                    new GeneField("tgtID", 8),
                    new GeneField("tgtData", 16),

                    new GeneField("slot", 2),
                    new GeneField("weight", 16),
                };

                Random rand = new Random();
                Gene gene = new Gene();
                foreach (GeneField field in template)
                {
                    ushort value = (ushort) rand.Next(0, field.maxValue+1);

                    switch (field.name)
                    {
                        case "srcType":
                            gene.srcType = (NType)value;
                            break;
                        case "srcFunc":
                            gene.srcFunc = (NFunc)value;
                            break;
                        case "srcID":
                            gene.srcID = (byte)value;
                            break;
                        case "srcData":
                            gene.srcData = value;
                            break;

                        case "tgtType":
                            gene.tgtType = (NType)value;
                            break;
                        case "tgtFunc":
                            gene.tgtFunc = (NFunc)value;
                            break;
                        case "tgtID":
                            gene.tgtID = (byte)value;
                            break;
                        case "tgtData":
                            gene.tgtData = value;
                            break;

                        case "slot":
                            gene.slot = (byte)value;
                            break;
                        case "weight":
                            gene.weight = value;
                            break;
                    }
                }
            }

            return genome;
        }
    }
}