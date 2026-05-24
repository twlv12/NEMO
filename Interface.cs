
namespace NEMO
{
    public static class NEMO
    {
        public static void Main()
        {
            Genome genomeAlpha = GeneTools.GenerateGenome(16);

            while (true)
            {
                genomeAlpha = GeneTools.MutateGenome(genomeAlpha);
                GeneTools.RenderGraph(genomeAlpha);
                Thread.Sleep(1000);
            }
        }
    }
}
