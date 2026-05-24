
namespace NEMO
{
    public static class Config
    {
        //Genome Parameters
        public static int minGenes = 12;
        public static int baseGenes = 16;
        public static int maxGenes = 24;
        public static float neuronReuse = 1f;
        //Genome ----------

        //Mutation Parameters
        public static float weightSharpness = 10f;
        public static float weightFlux = 1.5f;
        public static float wSignFlipChance = 0.05f;

        public static float floatDataSharpness = 10f;
        public static float floatDataFlux = 1.5f;
        public static float boolFlipChance = 0.1f;
        public static float intRandChance = 0.05f;

        public static float slotFlipChance = 0.15f;
        public static float rewireOneChance = 0.1f;
        public static float regenOneChance = 0.05f;

        public static float neuronReplaceChance = 0.03f;
        public static float sameTypeChance = 0.7f;

        public static float geneToggleChance = 0.02f;
        public static float geneSplitChance = 0.015f;

        public static float geneDuplicationChance = 0.03f;
        public static float geneInsertionChance = 0.03f;
        public static float geneRemovalChance = 0.05f;
        //Mutation ----------

        public static int height = 1024;

    }
}
