
namespace NEMO
{
    public static class NEMO
    {
        public static void Main()
        {
            Genome genomeAlpha = GeneTools.GenerateGenome(16);

            genomeAlpha.PrintGenes();
            GeneTools.RenderGraphViz(genomeAlpha);
        }
    }
}
