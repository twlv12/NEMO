using System.Reflection;
using System.Text.Json;

namespace NEMO
{
    public static class Config
    {
        public static int worldWidth;
        public static int worldHeight;
        public static int tickRate;
        public static bool maxSpeed;

        public static int creatureCount;
        public static float governorStrength;
        public static float globalEnergyMultiplier;
        public static float wastePenaltyMultiplier;
        public static float momentumInfluence;

        public static float elevation;
        public static float frequency;
        public static float caveFrequency;
        public static float amplitude;
        public static int numOctaves;
        public static int maxGenAttempts;
        public static float migrationSpeed;

        public static float foodWorldCoverage;
        public static float plantClustering;
        public static float plantFrequency;
        public static float plantCutoff;
        public static float lingeringPlants;
        public static float plantGrowthRate;

        public static float baseNutrition;
        public static float meatNutritionMultiplier;
        public static float meatEntropyMulti;
        public static float meatDecayRate;
        public static float deathEnergy;

        public static float baseStartingEnergy;
        public static int maturationTime;
        public static float birthEfficiency;
        public static float costOfLiving;
        public static float movementCost;
        public static float attackCost;
        public static float baseAttackDmg;
        public static float wallCollisionDmg;

        public static float paraEntropyMulti;
        public static float paraSomaticTax;
        public static float paraDrainPower;

        public static float selectionThreshold;
        public static float selectKinshipThreshold;
        public static int autoChampTickDelay;
        public static int recorderTickDelay;
        public static int recorderMaxGB;

        public static bool maintainPopulation;

        public static int minGenes;
        public static int baseGenes;
        public static int maxGenes;
        public static float neuronReuse;

        public static bool printMutations;
        public static float globalMutationRate;
        public static float topologyMutationRate;

        public static float phenoMutationFlux;
        public static float phenoMutationSharpness;

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

        public static bool runLegacyPatcher;
        public static bool hideConsole;

        public static int uiRate;
        public static bool pauseWithoutUI;
        public static bool compressRecordings;
        public static List<string> customIPs = new List<string>();

        public static string MainConfigFile = GetResolvedConfigPath();
        public static string projectDirectory = GetResolvedProjectDirectory();
        public static string RuntimeConfigFile = Path.Combine(projectDirectory, "runtimeConfig.json");

        public static string WebFolder = Path.Combine(projectDirectory, "Web");
        public static string SavedGenomesFolder = Path.Combine(projectDirectory, "SavedGenomes");
        public static string RecordingsFolder = Path.Combine(projectDirectory, "Recordings");

        public static bool IsDevMode => File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "NEMO.csproj"));

        private static string GetResolvedConfigPath()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;

            string localConfig = Path.Combine(baseDir, "Config.json");

            string devConfig = Path.GetFullPath(Path.Combine(baseDir, "..", "Config.json"));
            string devProjFile = Path.GetFullPath(Path.Combine(baseDir, "..", "NEMO.csproj"));

            if (File.Exists(devConfig) && File.Exists(devProjFile))
            {
                return devConfig;
            }

            return localConfig;
        }
        private static string GetResolvedProjectDirectory()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string devProjFile = Path.GetFullPath(Path.Combine(baseDir, "..", "NEMO.csproj"));

            if (File.Exists(devProjFile))
            {
                return Path.GetFullPath(Path.Combine(baseDir, ".."));
            }

            return baseDir;
        }

        public static void Load()
        {
            if (!File.Exists(MainConfigFile))
            {
                Console.WriteLine("[CONFIG] Main config file not found!");
                return;
            }
            ApplyJson(File.ReadAllText(MainConfigFile));
            Console.WriteLine($"[CONFIG] Loaded from {MainConfigFile}.");
        }
        public static void ReloadRuntime()
        {
            if (!File.Exists(RuntimeConfigFile)) return;
            try
            {
                ApplyJson(File.ReadAllText(RuntimeConfigFile));
            }
            catch { return; }
        }
        public static void ApplyJson(string json)
        {
            try
            {
                var options = new JsonDocumentOptions
                {
                    CommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true
                };

                using JsonDocument doc = JsonDocument.Parse(json, options);
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    var field = typeof(Config).GetField(prop.Name, BindingFlags.Public | BindingFlags.Static);
                    if (field != null)
                    {
                        if (field.FieldType == typeof(int)) field.SetValue(null, prop.Value.GetInt32());
                        else if (field.FieldType == typeof(float)) field.SetValue(null, (float)prop.Value.GetDecimal());
                        else if (field.FieldType == typeof(bool)) field.SetValue(null, prop.Value.GetBoolean());
                        else if (field.FieldType == typeof(string)) field.SetValue(null, prop.Value.GetString());

                        else if (field.FieldType == typeof(List<string>))
                        {
                            var list = new List<string>();
                            foreach (var item in prop.Value.EnumerateArray())
                            {
                                string s = item.GetString();
                                if (!string.IsNullOrWhiteSpace(s)) list.Add(s);
                            }
                            field.SetValue(null, list);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CONFIG] Error loading JSON: {ex.Message}");
            }
        }
    }
}