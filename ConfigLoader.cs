using System.Reflection;
using System.Text.Json;
namespace NEMO
{
    public class ConfigData
    {
        //WHEN ADDING NEW, ADD BOTH UPPER AND LOWER ENTRY!!!
        #region Set1
        public int minGenes { get; set; }
        public int baseGenes { get; set; }
        public int maxGenes { get; set; }
        public float neuronReuse { get; set; }
        public float globalMutationRate { get; set; }
        public float topologyMutationRate { get; set; }
        public float mathSuppressionExponent { get; set; }
        public float mathWeightMultiplier { get; set; }
        public float baseActionWeight { get; set; }
        public float baseSensorWeight { get; set; }
        public float weightSharpness { get; set; }
        public float weightFlux { get; set; }
        public float wSignFlipChance { get; set; }
        public float floatDataSharpness { get; set; }
        public float floatDataFlux { get; set; }
        public float boolFlipChance { get; set; }
        public float intRandChance { get; set; }
        public float slotFlipChance { get; set; }
        public float rewireOneChance { get; set; }
        public float regenOneChance { get; set; }
        public float neuronReplaceChance { get; set; }
        public float sameTypeChance { get; set; }
        public float globalNewGeneRate { get; set; }
        public float geneToggleChance { get; set; }
        public float geneSplitChance { get; set; }
        public float geneDuplicationChance { get; set; }
        public float geneInsertionChance { get; set; }
        public float geneRemovalChance { get; set; }
        public bool printMutations { get; set; }
        public int currentView { get; set; }
        public int worldWidth { get; set; }
        public int worldHeight { get; set; }
        public int creatureCount { get; set; }
        public float baseNutrition { get; set; }
        public float meatNutritionMultiplier { get; set; }
        public float movementCost { get; set; }
        public float costOfLiving { get; set; }
        public float baseStartingEnergy { get; set; }
        public float attackCost { get; set; }
        public float phenoMutationFlux { get; set; }
        public float phenoMutationSharpness { get; set; }
        public float baseAttackDmg { get; set; }
        public int maturationTime { get; set; }
        public int tickRate { get; set; }
        public float foodWorldCoverage { get; set; }
        public float plantGrowthRate { get; set; }
        public bool maintainPopulation { get; set; }
        public bool maxSpeed { get; set; }
        #endregion

    }
    public static class Config
    {
        //SET THESE!!
        public static string MainConfigFile = @"C:\Users\ethan\source\repos\twlv12\NEMO\NemoViewer\Config.json"; //FILE
        public static string RuntimeConfigFile = @"C:\Users\ethan\source\repos\twlv12\NEMO\NEMOViewer\runtimeConfig.json"; //FILE
        public static string StimuliFile = @"C:\Users\ethan\source\repos\twlv12\NEMO\NEMOViewer\stimuli.json"; //FILE
        public static string EditorActionFile = @"C:\Users\ethan\source\repos\twlv12\NEMO\NEMOViewer\editorAction.json"; //FILE
        public static string GraphOutputFolder = @"C:\Users\ethan\source\repos\twlv12\NEMO\NEMOViewer\"; //FOLDER

        #region Set2
        public static int minGenes;
        public static int baseGenes;
        public static int maxGenes;
        public static float neuronReuse;
        public static float globalMutationRate;
        public static float topologyMutationRate;
        public static float mathSuppressionExponent;
        public static float mathWeightMultiplier;
        public static float baseActionWeight;
        public static float baseSensorWeight;
        public static float weightSharpness;
        public static float weightFlux;
        public static float wSignFlipChance;
        public static float floatDataSharpness;
        public static float floatDataFlux;
        public static float boolFlipChance;
        public static float intRandChance;
        public static float slotFlipChance;
        public static float rewireOneChance;
        public static float regenOneChance;
        public static float neuronReplaceChance;
        public static float sameTypeChance;
        public static float globalNewGeneRate;
        public static float geneToggleChance;
        public static float geneSplitChance;
        public static float geneDuplicationChance;
        public static float geneInsertionChance;
        public static float geneRemovalChance;
        public static bool printMutations;
        public static int currentView;
        public static int worldWidth;
        public static int worldHeight;
        public static int creatureCount;
        public static float baseNutrition;
        public static float meatNutritionMultiplier;
        public static float movementCost;
        public static float costOfLiving;
        public static float baseStartingEnergy;
        public static float attackCost;
        public static float phenoMutationFlux;
        public static float phenoMutationSharpness;
        public static float baseAttackDmg;
        public static int maturationTime;
        public static int tickRate;
        public static float foodWorldCoverage;
        public static float plantGrowthRate;
        public static bool maintainPopulation;
        public static bool maxSpeed;
        #endregion

        public static ConfigData configData = new();
        public static void Load()
        {
            string json = File.ReadAllText(MainConfigFile);
            ConfigData data = JsonSerializer.Deserialize<ConfigData>(json)!;
            Apply(data);
        }
        public static void ReloadRuntime()
        {
            if (!File.Exists(RuntimeConfigFile))
                return;
            try{
                string json;
                using (FileStream stream =
                    new FileStream(
                        RuntimeConfigFile,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.ReadWrite))

                using (StreamReader reader = new StreamReader(stream)){
                    json = reader.ReadToEnd();
                }

                ConfigData runtime = JsonSerializer.Deserialize<ConfigData>(json)!;
                if (runtime != null){
                    Apply(runtime);
                }
            }
            catch{
                return;
            }
        }
        private static void Apply(ConfigData data)
        {
            FieldInfo[] configFields = typeof(Config).GetFields(
                     BindingFlags.Public 
                   | BindingFlags.Static );

            PropertyInfo[] dataProperties = typeof(ConfigData).GetProperties();

            foreach (FieldInfo field in configFields)
            {
                PropertyInfo? prop = dataProperties.FirstOrDefault(
                        p => p.Name == field.Name );

                if (prop == null)
                    continue;

                object? value = prop.GetValue(data);
                field.SetValue(null, value);
            }
        }
    }
}