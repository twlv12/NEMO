
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

    public class GeneField
    {
        public string name;
        public int bitLength;
        public ushort maxValue;

        public GeneField(string name, int bitLength)
        {
            this.name = name;
            this.bitLength = bitLength;

            if (name == "srcType" || name == "tgtType"){
                this.maxValue = (ushort) (Enum.GetNames(typeof(NType)).Length -1);
            }
            if (name == "srcFunc" || name == "tgtFunc"){
                this.maxValue = (ushort) (Enum.GetNames(typeof(NFunc)).Length -1);
            }
            else { this.maxValue = (ushort) Math.Pow(2, bitLength); }
        }
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

                    //How to handle differing bitLengths?
                    //How to set gene variable of name "field.name" to cutValue?
                }
            }

            return genome;
        }
    }
}