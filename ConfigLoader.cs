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
        public int screenHeight { get; set; }
        public int screenWidth { get; set; }
        public bool printMutations { get; set; }
        #endregion

    }
    public static class Config
    {
        //SET THESE!!
        public static string MainConfigFile = @"C:\Users\ethan\source\repos\NEMO\NemoViewer\Config.json"; //FILE
        public static string RuntimeConfigFile = @"C:\Users\ethan\source\repos\NEMO\NEMOViewer\runtimeConfig.json"; //FILE
        public static string GraphOutputFolder = @"C:\Users\ethan\source\repos\NEMO\NEMOViewer\"; //FOLDER

        #region Set1
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
        public static int screenHeight;
        public static int screenWidth;
        public static bool printMutations;
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