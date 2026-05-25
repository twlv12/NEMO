
namespace NEMO //Neural Emergence thru Mutating Organisms
{
    public static class NEMO
    {
        public static void Main()
        {
            DateTime lastReload = DateTime.Now;
            Config.Load();

            List<string> genomeNames = ["alpha", "beta", "gamma", "delta"];
            List<(Genome genome, string name)> genomes = new();
            foreach (string name in genomeNames){
                Genome newGenome = GeneTools.GenerateGenome();
                newGenome.PrintGenes();
                genomes.Add((newGenome, name));
            }

            for (int i = 0; true; i++)
            {
                if ((DateTime.Now - lastReload)
                    .TotalMilliseconds > 250){
                    Config.ReloadRuntime();
                    lastReload = DateTime.Now;
                }

                foreach (var (genome, name) in genomes){
                    GeneTools.MutateGenome(genome);
                    GeneTools.RenderGraph(genome, name);
                }
                Thread.Sleep(250);
            }
        }
    }
}
