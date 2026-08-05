using Fleck;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace NEMO
{
    //TODO - NEVER IN ORDER
    //add signal ovelay
    //add family tree
    //why oflactory sensitivity?
    //add trainer stop at generation
    //trainer disable kills
    //trainer only alive

    public static class NEMO
    {
        #region Initializations
        public static bool isBroadcasting = false;
        public static List<IWebSocketConnection> clients = new List<IWebSocketConnection>();
        public static World? activeWorld = null;
        public static bool isPaused = false;
        public static string trackedCreatureId = "";

        public static string currentMode = "Petri";
        public static TWorld? activeTrainer = null;
        public static List<Simulation> sims = new List<Simulation>();

        public static int extinctionCount = 0;
        public static int savedGenomesTotal = 0;
        public static int savedGenomesSession = 0;
        public static long totalRecordingBytes = -1;

        public static List<(byte r, byte g, byte b)> sessionSavedColors = new();

        public static Stopwatch tpsTimer = Stopwatch.StartNew();
        public static int ticksThisSecond = 0;
        public static int currentTPS = 0;
        public static bool autoRestart = true;

        public static bool pauseOnExtinction = true;
        public static bool worldWasEmpty = false;
        public static bool safeEditMode = true;
        public static bool disableGovernor = false;
        public static bool disableEnergyDrain = false;
        public static bool disableReproduction = false;

        public static double emaSimTime = 0;
        public static double emaUiTime = 0;

        public static bool repopExtinct = false;
        public static bool repopChamp = false;
        public static bool repopEditor = false;
        public static bool isRecording = false;

        public static int isGenerating = 0;
        public static int terrainVersion = 0;
        public static int lastBroadcastTerrainVersion = -1;
        public static long requestedPlaybackTick = -1;

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool AllocConsole();

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr GetConsoleWindow();

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

        public static bool hasUIConnectedOnce = false;
        const int SW_HIDE = 0;
        const int SW_SHOW = 5;

        public static WebConsoleWriter ConsoleLog = null!;
        public class WebConsoleWriter : StringWriter
        {
            private TextWriter originalOut;
            public WebConsoleWriter(TextWriter original) { originalOut = original; }

            public override void WriteLine(string? value)
            {
                originalOut.WriteLine(value);

                if (NEMO.clients.Count > 0 && !string.IsNullOrEmpty(value))
                {
                    var msg = JsonSerializer.Serialize(new { @event = "consoleLog", text = value, color = "#cccccc" });
                    foreach (var client in NEMO.clients.ToList()) { try { client.Send(msg); } catch { } }
                }
            }
            public void WriteColored(string text, string hexColor, ConsoleColor conColor)
            {
                Console.ForegroundColor = conColor;
                originalOut.WriteLine(text);
                Console.ResetColor();

                if (NEMO.clients.Count > 0 && !string.IsNullOrEmpty(text))
                {
                    var msg = JsonSerializer.Serialize(new { @event = "consoleLog", text = text, color = hexColor });
                    foreach (var client in NEMO.clients.ToList()) { try { client.Send(msg); } catch { } }
                }
            }
        }

        public static void Log(string text, string hexColor = "#8fdfff", ConsoleColor conColor = ConsoleColor.Cyan)
        {
            if (ConsoleLog != null) ConsoleLog.WriteColored(text, hexColor, conColor);
            else Console.WriteLine(text);
        }
        #endregion

        public static void Main()
        {
            #region Startup
            var currentProcess = Process.GetCurrentProcess();
            foreach (var process in Process.GetProcessesByName(currentProcess.ProcessName))
            {
                if (process.Id != currentProcess.Id)
                {
                    try { process.Kill(); process.WaitForExit(1000); } catch { }
                }
            }

            Config.Load();

            if (!Config.hideConsole)
            {
                AllocConsole();

                IntPtr handle = GetConsoleWindow();
                if (handle != IntPtr.Zero)
                {
                    MoveWindow(handle, 0, 0, 590, 920, true);
                }

                StreamWriter standardOutput = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
                Console.SetOut(standardOutput);
            }
            ConsoleLog = new WebConsoleWriter(Console.Out);
            Console.SetOut(ConsoleLog);
            FleckLog.Level = LogLevel.Error;

            NeuronDicts.ExportNeuronDefs();
            NeuronDicts.ExportDataDefs();
            sims = new List<Simulation> { new("alpha"), new("beta"), new("gamma"), new("delta") };

            Directory.CreateDirectory(Config.SavedGenomesFolder);
            Directory.CreateDirectory(Config.RecordingsFolder);
            savedGenomesTotal = Directory.GetFiles(Config.SavedGenomesFolder, "*.json").Length;

            if (Config.runLegacyPatcher)
                PatchLegacyRecordings();

            StartServers();

            Stopwatch tickTimer = Stopwatch.StartNew();
            Stopwatch petriTimer = Stopwatch.StartNew();
            Stopwatch brainTimer = Stopwatch.StartNew();
            Stopwatch genomeTimer = Stopwatch.StartNew();
            Stopwatch profiler = new Stopwatch();
            #endregion   

            while (true)
            {
                profiler.Restart();

                if (Config.pauseWithoutUI && clients.Count == 0)
                {
                    Thread.Sleep(100);
                    continue;
                }

                if (!isPaused)
                {
                    double delay = Config.maxSpeed ? 0 : 1000.0 / Config.tickRate;
                    if (delay == 0 || tickTimer.ElapsedMilliseconds >= delay)
                    {
                        profiler.Restart();

                        try
                        {
                            if (currentMode == "Petri" && activeWorld != null)
                            {
                                activeWorld.Update();

                                if (activeWorld.creatures.Count == 0 && !worldWasEmpty)
                                {
                                    extinctionCount++;
                                    worldWasEmpty = true;
                                    if (pauseOnExtinction) isPaused = true;

                                    var msg = JsonSerializer.Serialize(new { @event = "simEnded" });
                                    foreach (var client in clients.ToList()) client.Send(msg);
                                }
                                else if (activeWorld.creatures.Count > 0)
                                {
                                    worldWasEmpty = false;
                                }
                            }
                            else if (currentMode == "Trainer" && activeTrainer != null)
                            {
                                activeTrainer.Update();
                            }
                        }
                        catch (Exception ex)
                        {
                            NEMO.Log($"[ERROR]: {ex.Message}\n{ex.StackTrace}", "tomato", ConsoleColor.Red);
                            isPaused = true;
                        }

                        if (delay > 0) tickTimer.Restart();

                        ticksThisSecond++;
                        if (tpsTimer.ElapsedMilliseconds >= 1000)
                        {
                            currentTPS = ticksThisSecond;
                            ticksThisSecond = 0;
                            tpsTimer.Restart();
                        }
                    }
                }

                if (petriTimer.ElapsedMilliseconds >= Config.uiRate)
                {
                    if (currentMode == "Petri" && activeWorld != null)
                        UpdatePetriView(activeWorld);
                    else if (currentMode == "Trainer" && activeTrainer != null)
                        UpdateTrainerView(activeTrainer);

                    petriTimer.Restart();
                }

                if (brainTimer.ElapsedMilliseconds >= 100)
                {
                    UpdateBrainView(sims);
                    brainTimer.Restart();
                }
                if (genomeTimer.ElapsedMilliseconds >= 100)
                {
                    UpdateGenomeView(sims);
                    genomeTimer.Restart();
                }

                emaUiTime = (emaUiTime * 0.95) + (profiler.Elapsed.TotalMilliseconds * 0.05);
                if (isPaused) Thread.Sleep(10);
            }
        }
        
        public static void BroadcastState()
        {
            var state = new
            {
                @event = "syncState",
                isDevMode = Config.IsDevMode,
                disableGovernor = disableGovernor,
                disableEnergyDrain = disableEnergyDrain,
                isPaused = isPaused,
                autoRestart = autoRestart,
                pauseOnExtinction = pauseOnExtinction,
                safeEditMode = safeEditMode,
                repopExtinct = repopExtinct,
                repopChamp = repopChamp,
                repopEditor = repopEditor,
                maxSpeed = Config.maxSpeed,
                tickRate = Config.tickRate,
                uiRate = Config.uiRate,
                gem = Config.globalEnergyMultiplier,
                wastePenalty = Config.wastePenaltyMultiplier,
                isRecording = isRecording,
                recorderTickDelay = Config.recorderTickDelay,
                compressRecordings = Config.compressRecordings
            };
            string json = JsonSerializer.Serialize(state);
            foreach (var client in clients.ToList()) client.Send(json);
        }

        public static void UpdateTrainerView(TWorld trainer)
        {
            if (isBroadcasting) return;
            isBroadcasting = true;

            TCreature[] activeSnap = trainer.activeCreatures?.ToArray() ?? Array.Empty<TCreature>();
            TCreature[] baitSnap = trainer.baitCreatures?.ToArray() ?? Array.Empty<TCreature>();
            TCreature[] allCreatures = activeSnap.Concat(baitSnap).ToArray();

            IWebSocketConnection[] clientsSnap = clients.ToArray();

            Task.Run(() =>
            {
                Stopwatch uiProfiler = Stopwatch.StartNew();
                try
                {
                    string stateJson = trainer.GetStateJson(allCreatures);
                    foreach (var client in clientsSnap) client.Send(stateJson);
                }
                catch (Exception ex) { Console.WriteLine($"[UI] {ex.Message}"); }
                finally
                {
                    isBroadcasting = false;
                    NEMO.emaUiTime = (NEMO.emaUiTime * 0.95) + (uiProfiler.Elapsed.TotalMilliseconds * 0.05);
                }
            });
        }
        public static void UpdatePetriView(World world)
        {
            if (isBroadcasting) return;
            isBroadcasting = true;

            Creature[] creaturesSnap = world.creatures.ToArray();
            FoodItem[] foodsSnap = world.activeFoods.ToArray();
            IWebSocketConnection[] clientsSnap = clients.ToArray();

            World.ExportBlock[] blocksSnap = null!;
            if (terrainVersion != lastBroadcastTerrainVersion)
            {
                blocksSnap = world.staticBlocks.ToArray();
                lastBroadcastTerrainVersion = terrainVersion;
            }

            Task.Run(() =>
            {
                Stopwatch uiProfiler = Stopwatch.StartNew();
                try
                {
                    string stateJson = world.GetStateJson(creaturesSnap, foodsSnap, blocksSnap);
                    foreach (var client in clientsSnap) client.Send(stateJson);
                }
                catch (Exception ex) { Console.WriteLine($"[UI] {ex.Message}"); }
                finally
                {
                    isBroadcasting = false;
                    NEMO.emaUiTime = (NEMO.emaUiTime * 0.95) + (uiProfiler.Elapsed.TotalMilliseconds * 0.05);
                }
            });
        }
        public static void UpdateBrainView(List<Simulation> sims)
        {
            foreach (var sim in sims)
            {
                bool isDead = sim.trackedCreature != null && sim.trackedCreature.isDead;

                if (sim.trackedCreature == null)
                    sim.brain.UpdateAllNeurons();

                NeuralTools.RenderGraph(sim.brain, sim.name, isDead, isPaused, sim.trackedCreature != null && !isDead);
            }
        }
        public static void UpdateGenomeView(List<Simulation> sims)
        {
            foreach (var sim in sims)
            {
                bool isAlive = sim.trackedCreature != null && !sim.trackedCreature.isDead;
                GeneTools.RenderGraph(sim.genome, sim.name, isAlive);
            }
        }

        public static void OnViewChanged(List<Simulation> sims)
        {
            foreach (var sim in sims)
            {
                RebuildLiveBrain(sim);
            }
        }

        public static void RebuildLiveBrain(Simulation sim)
        {
            sim.brain = NeuralTools.GenomeToBrain(sim.genome);
            if (sim.trackedCreature != null && !sim.trackedCreature.isDead)
            {
                sim.trackedCreature.brain = sim.brain;
                foreach (var n in sim.brain.neurons) n.host = sim.trackedCreature;
            }
        }

        public static void SaveGenomeToDisk(Genome genome, string prefix)
        {
            Directory.CreateDirectory(Config.SavedGenomesFolder);
            string safeHash = genome.GenerateExactHash().ToString("X");

            JsonSerializerOptions options = new JsonSerializerOptions
            {
                WriteIndented = true,
                IncludeFields = true,
                Converters = { new JsonStringEnumConverter() }
            };
            string jsonGenome = JsonSerializer.Serialize(genome, options);

            File.WriteAllText($"{Config.SavedGenomesFolder}/{prefix}_{safeHash}.json", jsonGenome);

            savedGenomesTotal++;
            savedGenomesSession++;
            NEMO.Log($"[SAVED] {prefix}_{safeHash}.json", "#8fdfff", ConsoleColor.Cyan);
        }
        public static Genome? LoadGenomeFromDisk(string jsonText)
        {
            try
            {
                JsonSerializerOptions options = new JsonSerializerOptions
                {
                    IncludeFields = true,
                    Converters = { new JsonStringEnumConverter() }
                };
                Genome? genome = JsonSerializer.Deserialize<Genome>(jsonText, options);
                Console.ForegroundColor = ConsoleColor.Blue;
                NEMO.Log($"[LOAD] Successfully deserialized genome.", "#8fdfff", ConsoleColor.Blue);

                return genome;
            }
            catch (Exception ex)
            {
                NEMO.Log($"[LOAD] Failed to deserialize genome: {ex.Message}", "tomato", ConsoleColor.Red);
                return null;
            }
        }

        public static void TryAutoSaveChampion(World currentWorld, string prefix)
        {
            if (currentWorld.bestGenome != null && currentWorld.highestSurvivalRatio >= Config.selectionThreshold)
            {
                var genColor = currentWorld.bestGenome.GenerateColor();
                bool isTooSimilar = false;

                foreach (var savedColor in sessionSavedColors)
                {
                    float rDiff = MathF.Abs(genColor.r - savedColor.r);
                    float gDiff = MathF.Abs(genColor.g - savedColor.g);
                    float bDiff = MathF.Abs(genColor.b - savedColor.b);

                    float kinship = 1f - ((rDiff + gDiff + bDiff) / 765f);

                    if (kinship > Config.selectKinshipThreshold)
                    {
                        isTooSimilar = true;
                        break;
                    }
                }

                if (!isTooSimilar)
                {
                    sessionSavedColors.Add((genColor.r, genColor.g, genColor.b));
                    SaveGenomeToDisk(currentWorld.bestGenome, $"{prefix}_Gen{currentWorld.highestGeneration}_Sig{currentWorld.highestSurvivalRatio:F1}");
                }
            }
        }
        public static void RecordWorldState(World world, string runID = null)
        {
            long currentTick = world.totalTicks;
            var blocks = world.staticBlocks.ToList();

            var foods = new List<World.ExportFood>();
            lock (world.activeFoods)
            {
                foods = world.activeFoods.Select(f => new World.ExportFood { x = f.x, y = f.y, meat = f.isMeat }).ToList();
            }

            int pop = world.creatures.Count;
            int plants = foods.Count(f => !f.meat);
            int meat = foods.Count(f => f.meat);
            float totalBioE = world.creatures.Sum(c => c.energy);
            float totalPlantE = foods.Where(f => !f.meat).Sum(f => Config.baseNutrition);
            float totalMeatE = foods.Where(f => f.meat).Sum(f => Config.baseNutrition);
            float avgAge = pop > 0 ? (float)world.creatures.Average(c => c.age) : 0;
            float avgGen = pop > 0 ? (float)world.creatures.Average(c => c.generation) : 0;
            float avgCarnivory = pop > 0 ? world.creatures.Average(c => c.GetPheno(PType.CarnivoryBias)) : 0;
            float avgGenes = pop > 0 ? (float)world.creatures.Average(c => c.genome.genes.Count) : 0;

            int herbivores = 0, hunters = 0, scavengers = 0, parasites = 0, omnivores = 0;
            foreach (var c in world.creatures)
            {
                float carn = c.GetPheno(PType.CarnivoryBias);
                float para = c.GetPheno(PType.Parasitism);
                float scav = c.GetPheno(PType.ScavengerTolerance);
                if (para > 0.2f) parasites++;
                else if (carn > 0.65f) { if (scav > 0.5f) scavengers++; else hunters++; }
                else if (carn < 0.35f) herbivores++;
                else omnivores++;
            }

            var frameStats = new Dictionary<string, float>
            {
                { "pop", pop }, { "extinctions", NEMO.extinctionCount }, { "highestSurvivalRatio", world.highestSurvivalRatio },
                { "plants", plants }, { "meat", meat }, { "eIn", world.emaEnergyIn }, { "eOut", world.emaEnergyOut },
                { "totalCreatureE", totalBioE }, { "totalPlantE", totalPlantE }, { "totalMeatE", totalMeatE },
                { "births", world.emaBirths }, { "deaths", world.emaDeaths }, { "lifeMeas", world.emaLifespan }, { "lifeMath", world.govMathLifespan },
                { "plantsEaten", world.emaPlantsEaten }, { "meatsEaten", world.emaMeatsEaten }, { "attacks", world.emaAttacks }, { "killRate", world.emaKills },
                { "avgAge", avgAge }, { "avgGen", avgGen }, { "maxGen", world.highestGeneration },
                { "herbivores", herbivores }, { "omnivores", omnivores }, { "hunters", hunters }, { "scavengers", scavengers }, { "parasites", parasites },
                { "avgCarnivory", avgCarnivory }, { "avgGenes", avgGenes },
                { "govCap", world.govDynamicCapacity }, { "govCurE", world.govCurrentEnergy }, { "govBaseE", world.govBaselineEnergy },
                { "govActLife", world.govActiveLifespan }, { "govBlend", world.govBlendFactor }, { "govMom", world.govMomentum },
                { "govWastePen", world.govWastePenalty }, { "govDiet", world.govDietFactor }
            };

            var creatures = world.creatures.Select(c => new CreatureSnapshot
            {
                id = c.ID.ToString(),
                x = c.x,
                y = c.y,
                dir = c.facingDirection,
                r = c.colorR,
                g = c.colorG,
                b = c.colorB,
                energy = c.energy,
                age = c.age,
                generation = c.generation,
                lineageLifespan = c.lineageLifespan,
                genome = c.genome.Clone(),
                parentId = c.parentID
            }).ToList();

            Task.Run(async () =>
            {
                try
                {
                    string recPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Config.RecordingsFolder);

                    if (totalRecordingBytes == -1)
                    {
                        var dir = new DirectoryInfo(recPath);
                        totalRecordingBytes = dir.Exists ? dir.EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length) : 0;
                    }

                    float safeMaxGB = Config.recorderMaxGB > 0 ? Config.recorderMaxGB : 10f;
                    double maxBytes = safeMaxGB * 1024 * 1024 * 1024;

                    if (totalRecordingBytes >= maxBytes)
                    {
                        return;
                    }

                    var masses = world.creatures.Select(c => c.GetPheno(PType.BodyMass)).OrderBy(m => m).ToList();
                    float p10 = masses.Count > 0 ? masses[(int)(masses.Count * 0.10f)] : 0;
                    float p50 = masses.Count > 0 ? masses[(int)(masses.Count * 0.50f)] : 0;
                    float p90 = masses.Count > 0 ? masses[(int)(masses.Count * 0.90f)] : 0;

                    var frameStats = new Dictionary<string, float>
                    {
                    { "pop", pop }, { "extinctions", NEMO.extinctionCount }, { "highestSurvivalRatio", world.highestSurvivalRatio }, { "highestCEQ", world.highestCEQ },
                    { "plants", plants }, { "meat", meat }, { "eIn", world.emaEnergyIn }, { "eOut", world.emaEnergyOut },
                    { "totalCreatureE", totalBioE }, { "totalPlantE", totalPlantE }, { "totalMeatE", totalMeatE },
                    { "births", world.emaBirths }, { "deaths", world.emaDeaths }, { "lifeMeas", world.emaLifespan }, { "lifeMath", world.govMathLifespan },
                    { "plantsEaten", world.emaPlantsEaten }, { "meatsEaten", world.emaMeatsEaten }, { "attacks", world.emaAttacks }, { "killRate", world.emaKills },
                    { "avgAge", avgAge }, { "avgGen", avgGen }, { "maxGen", world.highestGeneration },
                    { "herbivores", herbivores }, { "omnivores", omnivores }, { "hunters", hunters }, { "scavengers", scavengers }, { "parasites", parasites },
                    { "avgCarnivory", avgCarnivory }, { "avgGenes", avgGenes },
                    { "massP10", p10 }, { "massP50", p50 }, { "massP90", p90 },
                    { "govCap", world.govDynamicCapacity }, { "govCurE", world.govCurrentEnergy }, { "govBaseE", world.govBaselineEnergy },
                    { "govActLife", world.govActiveLifespan }, { "govBlend", world.govBlendFactor }, { "govMom", world.govMomentum },
                    { "govWastePen", world.govWastePenalty }, { "govDiet", world.govDietFactor }
                    };

                    var snap = new WorldSnapshot
                    {
                        tick = currentTick,
                        width = world.width,
                        height = world.height,
                        stats = frameStats,
                        blocks = blocks,
                        foods = foods,
                        creatures = creatures
                    };
                    JsonSerializerOptions options = new JsonSerializerOptions { IncludeFields = true, Converters = { new JsonStringEnumConverter() } };
                    string json = JsonSerializer.Serialize(snap, options);

                    string runFolder = Path.Combine(recPath, runID ?? world.runID);
                    Directory.CreateDirectory(runFolder);

                    string telemetryPath = Path.Combine(runFolder, "telemetry.json");
                    string newStatJson = JsonSerializer.Serialize(frameStats);

                    if (!File.Exists(telemetryPath))
                    {
                        File.WriteAllText(telemetryPath, "[" + newStatJson + "]");
                    }
                    else
                    {
                        using (var stream = new FileStream(telemetryPath, FileMode.Open, FileAccess.ReadWrite))
                        {
                            stream.Seek(-1, SeekOrigin.End);
                            byte[] appendBytes = System.Text.Encoding.UTF8.GetBytes("," + newStatJson + "]");
                            stream.Write(appendBytes, 0, appendBytes.Length);
                        }
                    }

                    string ext = Config.compressRecordings ? ".json.gz" : ".json";
                    string filePath = Path.Combine(runFolder, $"Frame_{currentTick:D9}{ext}");

                    if (Config.compressRecordings)
                    {
                        using (FileStream fs = new FileStream(filePath, FileMode.Create))
                        using (GZipStream gz = new GZipStream(fs, CompressionLevel.Optimal))
                        using (StreamWriter sw = new StreamWriter(gz))
                        {
                            await sw.WriteAsync(json);
                        }
                    }
                    else
                    {
                        await File.WriteAllTextAsync(filePath, json);
                    }

                    FileInfo fi = new FileInfo(filePath);
                    Interlocked.Add(ref totalRecordingBytes, fi.Length);
                    NEMO.Log($"[RECORDER] Recorded frame.", "#aaa", ConsoleColor.DarkGray);
                }
                catch (Exception ex)
                {
                    NEMO.Log($"[RECORDER] Disk write failed: {ex.Message}", "tomato", ConsoleColor.Red);
                }
            });
        }
        public static void PatchLegacyRecordings()
        {
            string recPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Config.RecordingsFolder);
            if (!Directory.Exists(recPath)) return;

            var files = Directory.GetFiles(recPath, "*.json.gz", SearchOption.AllDirectories);
            int patchedCount = 0;

            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.WriteLine($"[PATCHER] Scanning {files.Length} legacy frames for repair...");
            Console.ResetColor();

            int progress = 0;
            int max = files.Length;
            int lastProg = 0;
            foreach (var file in files)
            {
                try
                {
                    bool isModified = false;
                    string json;

                    using (FileStream fs = new FileStream(file, FileMode.Open, FileAccess.Read))
                    using (GZipStream gz = new GZipStream(fs, CompressionMode.Decompress))
                    using (StreamReader sr = new StreamReader(gz))
                    {
                        json = sr.ReadToEnd();
                    }

                    string vampirismPattern = @"""Vampirism""\s*:\s*\{[^}]+\}\s*,?";
                    if (Regex.IsMatch(json, vampirismPattern, RegexOptions.IgnoreCase))
                    {
                        json = Regex.Replace(json, vampirismPattern, "", RegexOptions.IgnoreCase);
                        isModified = true;
                    }

                    var options = new JsonSerializerOptions
                    {
                        IncludeFields = true,
                        PropertyNameCaseInsensitive = true,
                        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
                        Converters = { new JsonStringEnumConverter() }
                    };

                    WorldSnapshot? snap = JsonSerializer.Deserialize<WorldSnapshot>(json, options);
                    if (snap == null) continue;

                    if (snap.width == 0 || snap.height == 0)
                    {
                        snap.width = Config.worldWidth;
                        snap.height = Config.worldHeight;
                        isModified = true;
                    }

                    if (snap.creatures != null)
                    {
                        foreach (var c in snap.creatures)
                        {
                            if (c.genome != null)
                            {
                                var trueColor = c.genome.GenerateColor();
                                if (c.r != trueColor.r || c.g != trueColor.g || c.b != trueColor.b)
                                {
                                    c.r = trueColor.r;
                                    c.g = trueColor.g;
                                    c.b = trueColor.b;
                                    isModified = true;
                                }
                            }
                        }
                    }

                    if (isModified)
                    {
                        string newJson = JsonSerializer.Serialize(snap, options);
                        using (FileStream fs = new FileStream(file, FileMode.Create))
                        using (GZipStream gz = new GZipStream(fs, CompressionLevel.Optimal))
                        using (StreamWriter sw = new StreamWriter(gz))
                        {
                            sw.Write(newJson);
                        }
                        patchedCount++;
                        Log($"[PATCHER] {patchedCount} Frames patched.", "aaa", ConsoleColor.DarkGray);
                    }
                }
                catch (Exception ex)
                {
                    Log($"[PATCHER ERROR] Failed on {file}: {ex.Message}", "tomato", ConsoleColor.Red);
                }
                int percent = (int)Math.Truncate(((float)progress / (float)max) * 100);
                if (percent != lastProg)
                {
                    lastProg = percent;
                    Log($"[PATCHER] {percent}% Complete...");
                }
                progress++;
            }

            Log($"[PATCHER] Successfully patched {patchedCount} legacy frames.", "palegreen", ConsoleColor.Green);
        }

        public static void ProcessSocketMessage(string jsonMessage, List<Simulation> sims, IWebSocketConnection client)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(jsonMessage);
                JsonElement root = doc.RootElement;

                if (root.TryGetProperty("value", out JsonElement valueElement) && !root.TryGetProperty("action", out _))
                {
                    string graph = root.GetProperty("graph").GetString()!;
                    string node = root.GetProperty("node").GetString()!;
                    float value = (float)valueElement.GetDecimal();

                    Simulation? valueSim = sims.FirstOrDefault(s => s.name == graph);
                    if (valueSim != null)
                    {
                        Neuron? neuron = valueSim.brain.neurons.FirstOrDefault(n => $"{n.func}_{n.ID}" == node);
                        if (neuron != null)
                        {
                            neuron.slotASum = value;
                            neuron.slotBSum = value;
                            neuron.value = value;
                        }
                    }
                }
                else if (root.TryGetProperty("action", out JsonElement actionElement))
                {
                    string actionType = actionElement.GetString()!;
                    NEMO.Log($"[UI] Request: {actionType}", "#8fdfff", ConsoleColor.Cyan);

                    if (actionType == "startWorld")
                    {
                        if (Interlocked.CompareExchange(ref isGenerating, 1, 0) == 1) return;
                        Task.Run(() =>
                        {
                            try
                            {
                                foreach (var s in sims)
                                {
                                    if (s.trackedCreature != null) s.trackedCreature.trackedSlot = null;
                                    s.trackedCreature = null;

                                    foreach (var n in s.brain.neurons) n.host = null;
                                }
                                activeWorld = null;
                                activeTrainer = null;
                                currentMode = "Petri";

                                var genMsg = JsonSerializer.Serialize(new { @event = "worldGenerating" });
                                foreach (var x in clients.ToList()) x.Send(genMsg);

                                World newWorld = new World(Config.worldWidth, Config.worldHeight, new List<Genome>());

                                activeWorld = newWorld;
                                terrainVersion++;
                                isPaused = false;
                                BroadcastState();
                            }
                            finally
                            {
                                Interlocked.Exchange(ref isGenerating, 0);
                            }
                        });
                        return;
                    }
                    if (actionType == "copyToNewWorld")
                    {
                        if (Interlocked.CompareExchange(ref isGenerating, 1, 0) == 1) return;

                        Task.Run(() =>
                        {
                            try
                            {
                                List<Genome> currentGenomes = new List<Genome>();
                                if (activeWorld != null)
                                {
                                    currentGenomes.AddRange(activeWorld.creatures.Select(c => c.genome.Clone()));
                                    currentGenomes.AddRange(activeWorld.pendingNewborns.Select(c => c.genome.Clone()));
                                }

                                foreach (var s in sims)
                                {
                                    if (s.trackedCreature != null) s.trackedCreature.trackedSlot = null;
                                    s.trackedCreature = null;
                                    foreach (var n in s.brain.neurons) n.host = null;
                                }

                                activeWorld = null;
                                activeTrainer = null;
                                currentMode = "Petri";

                                var genMsg = JsonSerializer.Serialize(new { @event = "worldGenerating" });
                                foreach (var x in clients.ToList()) x.Send(genMsg);

                                World newWorld = new World(Config.worldWidth, Config.worldHeight, currentGenomes);

                                activeWorld = newWorld;
                                terrainVersion++;
                                isPaused = false;
                                BroadcastState();
                            }
                            finally
                            {
                                Interlocked.Exchange(ref isGenerating, 0);
                            }
                        });
                        return;
                    }
                    if (actionType == "saveSnapshot")
                    {
                        if (activeWorld != null)
                        {
                            string snapId = "WRLD_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");

                            RecordWorldState(activeWorld, snapId);

                            NEMO.Log($"[WORLD] World state saved to archives as {snapId}", "#8fdfff", ConsoleColor.Cyan);

                            var bankMsg = JsonSerializer.Serialize(new { action = "getWorldBank" });
                            ProcessSocketMessage(bankMsg, sims, client);
                        }
                        return;
                    }
                    if (actionType == "togglePause")
                    {
                        if (safeEditMode && sims.Any(s => s.trackedCreature != null))
                        {
                            Console.WriteLine("Cannot unpause - Graph is LIVE and Safe Edit is ON");
                            return;
                        }

                        isPaused = !isPaused;
                        BroadcastState();
                        return;
                    }
                    if (actionType == "toggleSafeEditMode")
                    {
                        safeEditMode = !safeEditMode;
                        BroadcastState();
                        return;
                    }
                    if (actionType == "forcePause")
                    {
                        isPaused = true;
                        BroadcastState();
                        return;
                    }
                    if (actionType == "toggleAutoRestart")
                    {
                        autoRestart = !autoRestart;
                        BroadcastState();
                        return;
                    }
                    if (actionType == "killCreature")
                    {
                        string creatureId = root.GetProperty("creatureId").GetString()!;
                        var c = activeWorld?.creatures.FirstOrDefault(x => x.ID.ToString() == creatureId);
                        if (c != null) c.energy = 0;
                        return;
                    }
                    if (actionType == "respawnCreature")
                    {
                        string slot = root.GetProperty("slot").GetString()!;
                        var targetSim = sims.FirstOrDefault(s => s.name == slot);

                        if (targetSim != null && activeWorld != null)
                        {
                            int x = World.rand.Next(0, activeWorld.width);
                            int y = World.rand.Next(0, activeWorld.height);

                            Creature c = new Creature(x, y, targetSim.genome, activeWorld);
                            c.energy = c.startingEnergy * (c.GetPheno(PType.ReproductionThreshold) + Config.deathEnergy) / 2f;

                            activeWorld.grid[x + (y * activeWorld.width)].occupant = c;
                            activeWorld.pendingNewborns.Enqueue(c);

                            targetSim.trackedCreature = c;
                            targetSim.brain = c.brain;
                        }
                        return;
                    }
                    if (actionType == "loadGenome")
                    {
                        string slot = root.GetProperty("slot").GetString()!;
                        string creatureId = root.GetProperty("creatureId").GetString()!;
                        Console.WriteLine($"[EDITOR] Moved to slot: {slot} | Creature ID: {creatureId}");

                        var loadSim = sims.FirstOrDefault(s => s.name == slot);
                        if (loadSim != null)
                        {
                            if (activeWorld != null)
                            {
                                var c = activeWorld.creatures.FirstOrDefault(x => x.ID.ToString() == creatureId);
                                if (c != null)
                                {
                                    loadSim.trackedCreature = c;
                                    loadSim.genome = c.genome;
                                    loadSim.brain = c.brain;
                                    loadSim.trackedCreature.trackedSlot = slot;

                                    if (safeEditMode)
                                        isPaused = true;
                                    BroadcastState();
                                }
                            }
                            else if (activeTrainer != null)
                            {
                                var tc = activeTrainer.activeCreatures.FirstOrDefault(x => x.ID.ToString() == creatureId);
                                if (tc != null)
                                {
                                    loadSim.trackedCreature = null;
                                    loadSim.genome = tc.genome.Clone();
                                    RebuildLiveBrain(loadSim);

                                    if (safeEditMode)
                                        isPaused = true;
                                    BroadcastState();
                                }
                            }
                        }
                        return;
                    }
                    if (actionType == "updateConfig")
                    {
                        string key = root.GetProperty("key").GetString()!;
                        float val = (float)root.GetProperty("value").GetDecimal();

                        var field = typeof(Config).GetField(key, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                        if (field != null)
                        {
                            if (field.FieldType == typeof(int)) field.SetValue(null, (int)val);
                            else if (field.FieldType == typeof(float)) field.SetValue(null, val);
                            else if (field.FieldType == typeof(bool)) field.SetValue(null, val > 0.5f);
                        }
                        BroadcastState();
                        return;
                    }
                    if (actionType == "reloadConfig")
                    {
                        float liveGem = Config.globalEnergyMultiplier;
                        float liveWaste = Config.wastePenaltyMultiplier;
                        int liveTickRate = Config.tickRate;
                        bool liveMaxSpeed = Config.maxSpeed;
                        int liveUiRate = Config.uiRate;

                        Config.Load(); 

                        Config.globalEnergyMultiplier = liveGem;
                        Config.wastePenaltyMultiplier = liveWaste;
                        Config.tickRate = liveTickRate;
                        Config.maxSpeed = liveMaxSpeed;
                        Config.uiRate = liveUiRate;

                        BroadcastState(); // Push the sync back to the UI!
                        NEMO.Log("[CONFIG] Reloaded defaults.", "palegreen", ConsoleColor.Green);
                        return;
                    }
                    if (actionType == "randGenome")
                    {
                        string slot = root.GetProperty("slot").GetString()!;
                        var targetSim = sims.FirstOrDefault(s => s.name == slot);

                        if (targetSim != null)
                        {
                            targetSim.genome = GeneTools.GenerateGenome();
                            if (targetSim.trackedCreature != null)
                                targetSim.trackedCreature.genome = targetSim.genome;
                            RebuildLiveBrain(targetSim);
                        }
                        return;
                    }
                    if (actionType == "simpleGenome")
                    {
                        string slot = root.GetProperty("slot").GetString()!;
                        var targetSim = sims.FirstOrDefault(s => s.name == slot);

                        if (targetSim != null)
                        {
                            targetSim.genome = GeneTools.GenerateSimpleGenome();
                            if (targetSim.trackedCreature != null)
                                targetSim.trackedCreature.genome = targetSim.genome;
                            RebuildLiveBrain(targetSim);
                        }
                        return;
                    }
                    if (actionType == "clearGenome")
                    {
                        string slot = root.GetProperty("slot").GetString()!;
                        var targetSim = sims.FirstOrDefault(s => s.name == slot);

                        if (targetSim != null)
                        {
                            targetSim.genome = new Genome(new List<Gene>());
                            targetSim.genome.InitializeDefaultPhenotypes();

                            if (targetSim.trackedCreature != null)
                                targetSim.trackedCreature.genome = targetSim.genome;
                            RebuildLiveBrain(targetSim);
                        }
                        return;
                    }
                    if (actionType == "togglePauseOnExtinction")
                    {
                        pauseOnExtinction = !pauseOnExtinction;
                        BroadcastState();
                        return;
                    }
                    if (actionType == "unlinkSlot")
                    {
                        string slot = root.GetProperty("slot").GetString()!;
                        var targetSim = sims.FirstOrDefault(s => s.name == slot);
                        if (targetSim != null)
                        {
                            if (targetSim.trackedCreature != null) targetSim.trackedCreature.trackedSlot = null;
                            targetSim.trackedCreature = null;

                            targetSim.genome = targetSim.genome.Clone();
                            RebuildLiveBrain(targetSim);

                            foreach (var n in targetSim.brain.neurons) n.host = null;
                        }
                        return;
                    }
                    if (actionType == "killAll")
                    {
                        if (activeWorld != null)
                            foreach (Creature c in activeWorld.creatures)
                                c.energy = 0;
                        return;
                    }
                    if (actionType == "editPhenotype")
                    {
                        string slot = root.GetProperty("slot").GetString()!;
                        string traitKey = root.GetProperty("trait").GetString()!;
                        float val = (float)root.GetProperty("value").GetDecimal();

                        var targetSim = sims.FirstOrDefault(s => s.name == slot);
                        if (targetSim != null)
                        {
                            if (Enum.TryParse<PType>(traitKey, out PType parsedTrait))
                            {
                                if (targetSim.genome.phenotypes.ContainsKey(parsedTrait))
                                {
                                    targetSim.genome.phenotypes[parsedTrait].value = val;

                                    if (targetSim.trackedCreature != null)
                                    {
                                        targetSim.trackedCreature.genome.phenotypes[parsedTrait].value = val;
                                        targetSim.trackedCreature.phenoCache[(int)parsedTrait] = val;
                                    }
                                }
                            }
                        }
                        return;
                    }
                    if (actionType == "trackCreature")
                    {
                        trackedCreatureId = root.GetProperty("id").GetString()!;
                        return;
                    }
                    if (actionType == "draw")
                    {
                        if (activeWorld == null) return;
                        string type = root.GetProperty("type").GetString()!;
                        int x = root.GetProperty("x").GetInt32();
                        int y = root.GetProperty("y").GetInt32();

                        if (x < 0 || x >= activeWorld.width || y < 0 || y >= activeWorld.height) return;

                        if (type == "erase")
                        {
                            activeWorld.grid[x + (y * activeWorld.width)].isBlock = false;
                            activeWorld.staticBlocks.RemoveAll(b => b.x == x && b.y == y);
                            terrainVersion++;

                            if (activeWorld.grid[x + (y * activeWorld.width)].foodItem != null)
                            {
                                lock (activeWorld.activeFoods)
                                {
                                    activeWorld.activeFoods.Remove(activeWorld.grid[x + (y * activeWorld.width)].foodItem!);
                                }
                                activeWorld.grid[x + (y * activeWorld.width)].foodItem = null;
                            }
                            if (activeWorld.grid[x + (y * activeWorld.width)].occupant != null)
                            {
                                activeWorld.grid[x + (y * activeWorld.width)].occupant.energy = -1;
                            }
                        }
                        else if (type == "wall")
                        {
                            if (activeWorld.grid[x + (y * activeWorld.width)].occupant == null && activeWorld.grid[x + (y * activeWorld.width)].foodItem == null)
                            {
                                activeWorld.grid[x + (y * activeWorld.width)].isBlock = true;
                                if (!activeWorld.staticBlocks.Any(b => b.x == x && b.y == y))
                                    activeWorld.staticBlocks.Add(new World.ExportBlock { x = x, y = y });
                                terrainVersion++;
                            }
                        }
                        else if (type == "plant" || type == "meat")
                        {
                            if (!activeWorld.grid[x + (y * activeWorld.width)].isBlock && activeWorld.grid[x + (y * activeWorld.width)].foodItem == null && activeWorld.grid[x + (y * activeWorld.width)].occupant == null)
                            {
                                var f = new FoodItem(x, y, type == "meat");
                                if (type == "meat") f.nutrition = 1000f;
                                activeWorld.grid[x + (y * activeWorld.width)].foodItem = f;

                                lock (activeWorld.activeFoods)
                                {
                                    activeWorld.activeFoods.Add(f);
                                }
                            }
                        }
                        else if (type == "dummy")
                        {
                            if (!activeWorld.grid[x + (y * activeWorld.width)].isBlock && activeWorld.grid[x + (y * activeWorld.width)].occupant == null)
                            {
                                Genome dummyGen = new Genome(new List<Gene>());
                                dummyGen.InitializeDefaultPhenotypes();

                                Creature dummy = new Creature(x, y, dummyGen, activeWorld);
                                dummy.startingEnergy = Config.baseStartingEnergy;
                                dummy.energy = Config.baseStartingEnergy;

                                activeWorld.grid[x + (y * activeWorld.width)].occupant = dummy;
                                activeWorld.pendingNewborns.Enqueue(dummy);
                            }
                        }
                        return;
                    }
                    if (actionType == "moveCreature")
                    {
                        if (activeWorld == null) return;
                        string creatureId = root.GetProperty("creatureId").GetString()!;
                        int targetX = root.GetProperty("x").GetInt32();
                        int targetY = root.GetProperty("y").GetInt32();
                        int dir = root.GetProperty("dir").GetInt32();

                        var c = activeWorld.creatures.FirstOrDefault(x => x.ID.ToString() == creatureId);
                        if (c != null && targetX >= 0 && targetX < activeWorld.width && targetY >= 0 && targetY < activeWorld.height)
                        {
                            if (activeWorld.grid[c.x + (c.y * activeWorld.width)].occupant == c)
                            {
                                activeWorld.grid[c.x + (c.y * activeWorld.width)].occupant = null;
                            }

                            if (!activeWorld.grid[targetX + (targetY * activeWorld.width)].isBlock)
                            {
                                c.x = targetX;
                                c.y = targetY;
                                c.lastX = targetX;
                                c.lastY = targetY;
                                c.facingDirection = Math.Clamp(dir, 0, 7);
                                c.lastFacing = c.facingDirection;

                                activeWorld.grid[targetX + (targetY * activeWorld.width)].occupant = c;
                            }
                        }
                        return;
                    }
                    if (actionType == "getGenomeBank")
                    {
                        DirectoryInfo d = new DirectoryInfo(Config.SavedGenomesFolder);
                        if (!d.Exists) d.Create();

                        var allFiles = d.GetFiles("*.json");
                        var extinct = new List<string>();
                        var champ = new List<string>();
                        var editor = new List<string>();

                        foreach (var f in allFiles)
                        {
                            if (f.Name.StartsWith("Ext")) extinct.Add(f.Name);
                            else if (f.Name.StartsWith("Champ") || f.Name.StartsWith("ManualChamp") || f.Name.StartsWith("AutoChamp")) champ.Add(f.Name);
                            else editor.Add(f.Name);
                        }

                        var msg = JsonSerializer.Serialize(new
                        {
                            @event = "genomeBank",
                            extinct = extinct,
                            champ = champ,
                            editor = editor
                        });
                        client.Send(msg);
                        return;
                    }
                    if (actionType == "repopulateBank")
                    {
                        var filesArray = root.GetProperty("files").EnumerateArray().Select(x => x.GetString()!).ToList();
                        if (filesArray.Count == 0) return;

                        List<Genome> pool = new();
                        foreach (var f in filesArray)
                        {
                            string path = Path.Combine(Config.SavedGenomesFolder, f);
                            if (File.Exists(path))
                            {
                                Genome? g = LoadGenomeFromDisk(File.ReadAllText(path));
                                if (g != null) pool.Add(g);
                            }
                        }

                        if (pool.Count > 0 && activeWorld != null)
                        {
                            int currentTotal = activeWorld.creatures.Count + activeWorld.pendingNewborns.Count;
                            while (currentTotal < Config.creatureCount)
                            {
                                int x = World.rand.Next(0, activeWorld.width);
                                int y = World.rand.Next(0, activeWorld.height);
                                if (!activeWorld.grid[x + (y * activeWorld.width)].isBlock && activeWorld.grid[x + (y * activeWorld.width)].occupant == null)
                                {
                                    Genome gen = pool[World.rand.Next(pool.Count)].Clone();
                                    Creature c = new Creature(x, y, gen, activeWorld);
                                    c.energy = c.startingEnergy * (c.GetPheno(PType.ReproductionThreshold) + Config.deathEnergy) / 2f;

                                    activeWorld.grid[x + (y * activeWorld.width)].occupant = c;
                                    activeWorld.pendingNewborns.Enqueue(c);
                                    currentTotal++;
                                }
                            }
                        }
                        return;
                    }
                    if (actionType == "loadServerGenome")
                    {
                        string slot = root.GetProperty("slot").GetString()!;
                        string filename = root.GetProperty("filename").GetString()!;
                        var targetSim = sims.FirstOrDefault(s => s.name == slot);

                        string path = Path.Combine(Config.SavedGenomesFolder, filename);
                        if (File.Exists(path) && targetSim != null)
                        {
                            Genome? gen = LoadGenomeFromDisk(File.ReadAllText(path));
                            if (gen != null)
                            {
                                targetSim.genome = gen;
                                if (targetSim.trackedCreature != null)
                                {
                                    targetSim.trackedCreature.genome = targetSim.genome;
                                    foreach (var kvp in targetSim.genome.phenotypes)
                                        targetSim.trackedCreature.phenoCache[(int)kvp.Key] = kvp.Value.value;
                                }
                                RebuildLiveBrain(targetSim);
                            }
                        }
                        return;
                    }
                    if (actionType == "saveNamedGenome")
                    {
                        string type = root.GetProperty("type").GetString()!;
                        string name = root.GetProperty("name").GetString()!;
                        string prefix = $"{type}_{name}";

                        if (type == "Champ")
                        {
                            if (activeWorld != null && activeWorld.creatures.Count > 0)
                            {
                                float avgBurn = activeWorld.emaEnergyOut / Math.Max(1f, Config.creatureCount);
                                float mathLife = Config.baseStartingEnergy / Math.Max(0.001f, avgBurn);

                                var champ = activeWorld.creatures
                                    .OrderByDescending(c => ((c.age * 0.5f) + (c.lineageLifespan * 0.5f)) / Math.Max(1f, mathLife))
                                    .ThenByDescending(c => c.generation)
                                    .First();

                                SaveGenomeToDisk(champ.genome, prefix);
                            }
                        }
                        else if (type == "Editor")
                        {
                            string slot = root.GetProperty("slot").GetString()!;
                            var targetSim = sims.FirstOrDefault(s => s.name == slot);
                            if (targetSim != null && targetSim.genome.genes.Count > 0)
                            {
                                SaveGenomeToDisk(targetSim.genome, prefix);
                            }
                        }
                        return;
                    }
                    if (actionType == "toggleSimFlag")
                    {
                        string flag = root.GetProperty("flag").GetString()!;
                        if (flag == "disableGovernor") disableGovernor = !disableGovernor;
                        if (flag == "disableEnergyDrain") disableEnergyDrain = !disableEnergyDrain;
                        if (flag == "disableReproduction") disableReproduction = !disableReproduction;
                        BroadcastState();
                        return;
                    }
                    if (actionType == "scatterFood")
                    {
                        string type = root.GetProperty("type").GetString()!;
                        int amount = root.GetProperty("amount").GetInt32();
                        if (activeWorld != null)
                        {
                            for (int i = 0; i < amount; i++)
                            {
                                int x = World.rand.Next(activeWorld.width);
                                int y = World.rand.Next(activeWorld.height);
                                if (!activeWorld.grid[x + (y * activeWorld.width)].isBlock && activeWorld.grid[x + (y * activeWorld.width)].occupant == null && activeWorld.grid[x + (y * activeWorld.width)].foodItem == null)
                                {
                                    var f = new FoodItem(x, y, type == "meat");
                                    if (type == "meat") f.nutrition = 1000f;
                                    activeWorld.grid[x + (y * activeWorld.width)].foodItem = f;
                                    lock (activeWorld.activeFoods) activeWorld.activeFoods.Add(f);
                                }
                            }
                        }
                        return;
                    }
                    if (actionType == "scatterBait")
                    {
                        int amount = root.GetProperty("amount").GetInt32();
                        if (activeWorld != null)
                        {
                            Genome dummyGen = new Genome(new List<Gene>());
                            dummyGen.InitializeDefaultPhenotypes();

                            for (int i = 0; i < amount; i++)
                            {
                                int x = World.rand.Next(activeWorld.width);
                                int y = World.rand.Next(activeWorld.height);
                                if (!activeWorld.grid[x + (y * activeWorld.width)].isBlock && activeWorld.grid[x + (y * activeWorld.width)].occupant == null)
                                {
                                    Creature dummy = new Creature(x, y, dummyGen, activeWorld);
                                    dummy.startingEnergy = Config.baseStartingEnergy;
                                    dummy.energy = Config.baseStartingEnergy;

                                    activeWorld.grid[x + (y * activeWorld.width)].occupant = dummy;
                                    activeWorld.pendingNewborns.Enqueue(dummy);
                                }
                            }
                        }
                        return;
                    }
                    if (actionType == "cullSpecific")
                    {
                        string type = root.GetProperty("type").GetString()!;
                        if (activeWorld != null)
                        {
                            foreach (var c in activeWorld.creatures)
                            {
                                if (type == "herbivores" && c.GetPheno(PType.CarnivoryBias) < 0.5f) c.energy = 0;
                                if (type == "starving" && c.energy < (c.startingEnergy * Config.deathEnergy * 1.15f)) c.energy = 0;
                            }
                        }
                        return;
                    }
                    if (actionType == "findChampion")
                    {
                        string criteria = root.GetProperty("criteria").GetString()!;
                        if (activeWorld != null && activeWorld.creatures.Count > 0)
                        {
                            Creature? champ = null;
                            if (criteria == "kills") champ = activeWorld.creatures.OrderByDescending(c => c.kills).ThenByDescending(c => c.damageDealt).First();
                            else if (criteria == "meat") champ = activeWorld.creatures.OrderByDescending(c => c.meatsEaten).First();
                            else if (criteria == "plants") champ = activeWorld.creatures.OrderByDescending(c => c.plantsEaten).First();
                            else if (criteria == "age") champ = activeWorld.creatures.OrderByDescending(c => c.age).First();
                            else if (criteria == "sig")
                            {
                                champ = activeWorld.creatures
                                    .OrderByDescending(c => c.survivalRatio)
                                    .ThenByDescending(c => c.generation).First();
                            }
                            else
                            {
                                champ = activeWorld.creatures
                                    .OrderByDescending(c => c.ceqScore)
                                    .ThenByDescending(c => c.generation).First();
                            }

                            trackedCreatureId = champ.ID.ToString();
                            isPaused = true;
                            client.Send(JsonSerializer.Serialize(new { @event = "champFound", id = trackedCreatureId }));
                            BroadcastState();
                        }
                        return;
                    }
                    if (actionType == "getRawConfig")
                    {
                        string defaultJson = File.Exists(Config.MainConfigFile) ? File.ReadAllText(Config.MainConfigFile) : "{}";

                        var runtimeDict = new Dictionary<string, object>();
                        var fields = typeof(Config).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

                        foreach (var field in fields)
                        {
                            if (field.Name.EndsWith("File") || field.Name.EndsWith("Folder") || field.Name == "projectDirectory")
                                continue;

                            object? val = field.GetValue(null);
                            if (val != null)
                            {
                                runtimeDict[field.Name] = val;
                            }
                        }

                        string runtimeJson = JsonSerializer.Serialize(runtimeDict, new JsonSerializerOptions { WriteIndented = true });

                        var msg = JsonSerializer.Serialize(new
                        {
                            @event = "rawConfigData",
                            defaultJson = defaultJson,
                            runtimeJson = runtimeJson
                        });
                        client.Send(msg);
                        return;
                    }
                    if (actionType == "saveRawConfig")
                    {
                        string mode = root.GetProperty("mode").GetString()!;
                        string jsonText = root.GetProperty("json").GetString()!;

                        if (mode == "default")
                        {
                            File.WriteAllText(Config.MainConfigFile, jsonText);
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine("[CONFIG] DANGER: Config.json overwritten remotely.");
                            Console.ResetColor();
                        }
                        else if (mode == "runtime")
                        {
                            Config.ApplyJson(jsonText);
                            Console.ForegroundColor = ConsoleColor.Cyan;
                            Console.WriteLine("[CONFIG] Runtime configuration updated remotely.");
                            Console.ResetColor();
                            BroadcastState();
                        }
                        return;
                    }
                    if (actionType == "newCreature")
                    {
                        string slot = root.GetProperty("slot").GetString()!;
                        int amount = root.TryGetProperty("amount", out var amtEl) ? amtEl.GetInt32() : 1;
                        var targetSim = sims.FirstOrDefault(s => s.name == slot);

                        if (targetSim != null && activeWorld != null)
                        {
                            Random rand = new Random();
                            for (int i = 0; i < amount; i++)
                            {
                                for (int attempt = 0; attempt < 50; attempt++)
                                {
                                    int x = rand.Next(0, activeWorld.width);
                                    int y = rand.Next(0, activeWorld.height);

                                    if (!activeWorld.grid[x + (y * activeWorld.width)].isBlock && activeWorld.grid[x + (y * activeWorld.width)].occupant == null)
                                    {
                                        Creature c = new Creature(x, y, targetSim.genome, activeWorld);
                                        c.energy = c.startingEnergy * (c.GetPheno(PType.ReproductionThreshold) + Config.deathEnergy) / 2f;

                                        activeWorld.grid[x + (y * activeWorld.width)].occupant = c;
                                        activeWorld.pendingNewborns.Enqueue(c);
                                        break;
                                    }
                                }
                            }
                        }
                        return;
                    }
                    if (actionType == "armPlacement")
                    {
                        string slot = root.GetProperty("slot").GetString()!;
                        var msg = JsonSerializer.Serialize(new { @event = "armPlacement", slot = slot });
                        foreach (var c in clients.ToList()) c.Send(msg);
                        return;
                    }
                    if (actionType == "placeCreatureDir")
                    {
                        string slot = root.GetProperty("slot").GetString()!;
                        int x = root.GetProperty("x").GetInt32();
                        int y = root.GetProperty("y").GetInt32();
                        int dir = root.GetProperty("dir").GetInt32();

                        var targetSim = sims.FirstOrDefault(s => s.name == slot);
                        if (targetSim != null && activeWorld != null)
                        {
                            if (x >= 0 && x < activeWorld.width && y >= 0 && y < activeWorld.height)
                            {
                                if (!activeWorld.grid[x + (y * activeWorld.width)].isBlock && activeWorld.grid[x + (y * activeWorld.width)].occupant == null)
                                {
                                    Creature c = new Creature(x, y, targetSim.genome, activeWorld);
                                    c.energy = c.startingEnergy * (c.GetPheno(PType.ReproductionThreshold) + Config.deathEnergy) / 2f;

                                    c.facingDirection = Math.Clamp(dir, 0, 7);
                                    c.lastFacing = c.facingDirection;

                                    activeWorld.grid[x + (y * activeWorld.width)].occupant = c;
                                    activeWorld.pendingNewborns.Enqueue(c);
                                }
                            }
                        }

                        var msg = JsonSerializer.Serialize(new { @event = "placementDone" });
                        foreach (var c in clients.ToList()) c.Send(msg);
                        return;
                    }
                    if (actionType == "getWorldBank")
                    {
                        var dir = new DirectoryInfo(Config.RecordingsFolder);
                        var worlds = new List<object>();
                        if (dir.Exists)
                        {
                            foreach (var runDir in dir.GetDirectories())
                            {
                                var files = runDir.GetFiles("Frame_*");
                                if (files.Length > 0)
                                {
                                    var ticks = files.Select(f => long.Parse(f.Name.Replace("Frame_", "").Replace(".json.gz", "").Replace(".json", "").Replace(".nemo", ""))).OrderBy(t => t).ToList();

                                    long totalSize = 0;
                                    int compressedCount = 0;
                                    foreach (var f in files)
                                    {
                                        totalSize += f.Length;
                                        if (f.Name.EndsWith(".gz")) compressedCount++;
                                    }

                                    var telemetryFile = new FileInfo(Path.Combine(runDir.FullName, "telemetry.json"));
                                    if (telemetryFile.Exists) totalSize += telemetryFile.Length;

                                    int pct = (int)Math.Round((float)compressedCount / files.Length * 100f);

                                    worlds.Add(new
                                    {
                                        id = runDir.Name,
                                        frameCount = files.Length,
                                        lastTick = ticks.Last(),
                                        ticks = ticks,
                                        sizeBytes = totalSize,
                                        compressedPct = pct
                                    });
                                }
                            }
                        }
                        client.Send(JsonSerializer.Serialize(new { @event = "worldBank", worlds = worlds.OrderByDescending(w => ((dynamic)w).id) }));
                        return;
                    }
                    if (actionType == "loadFrame")
                    {
                        string runID = root.GetProperty("runID").GetString()!;
                        long tick = root.GetProperty("tick").GetInt32(); // Cast to long

                        Interlocked.Exchange(ref requestedPlaybackTick, tick);

                        string runFolder = Path.Combine(Config.RecordingsFolder, runID);
                        string nemoPath = Path.Combine(runFolder, $"Frame_{tick:D9}.nemo");
                        string jsonPath = Path.Combine(runFolder, $"Frame_{tick:D9}.json");
                        string gzPath = Path.Combine(runFolder, $"Frame_{tick:D9}.json.gz");

                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                if (Interlocked.Read(ref requestedPlaybackTick) != tick) return;

                                string rawJson = null;

                                if (File.Exists(nemoPath))
                                {
                                    using (FileStream fs = new FileStream(nemoPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan))
                                    using (StreamReader reader = new StreamReader(fs))
                                    {
                                        rawJson = await reader.ReadToEndAsync();
                                    }
                                }
                                else if (File.Exists(jsonPath))
                                {
                                    using (FileStream fs = new FileStream(jsonPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan))
                                    using (StreamReader reader = new StreamReader(fs))
                                    {
                                        rawJson = await reader.ReadToEndAsync();
                                    }
                                }
                                else if (File.Exists(gzPath))
                                {
                                    using (FileStream fsIn = new FileStream(gzPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan))
                                    using (GZipStream gz = new GZipStream(fsIn, CompressionMode.Decompress))
                                    using (StreamReader reader = new StreamReader(gz))
                                    {
                                        rawJson = await reader.ReadToEndAsync();
                                    }
                                }

                                if (Interlocked.Read(ref requestedPlaybackTick) != tick) return;

                                if (!string.IsNullOrEmpty(rawJson))
                                {
                                    string payload = $"{{\"event\":\"playbackFrame\",\"frameData\":{rawJson}}}";
                                    await client.Send(payload);
                                }
                                else
                                {
                                    Console.WriteLine($"[PLAYBACK] Frame {tick} not found.");
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"[PLAYBACK] Error at Frame {tick}: {ex.Message}");
                            }
                        });

                        return;
                    }
                    if (actionType == "loadRunTelemetry")
                    {
                        string runID = root.GetProperty("runID").GetString()!;
                        string telemetryPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Config.RecordingsFolder, runID, "telemetry.json");

                        if (File.Exists(telemetryPath))
                        {
                            string rawJson = File.ReadAllText(telemetryPath);
                            client.Send($"{{\"event\": \"playbackTelemetry\", \"statsList\": {rawJson}}}");
                        }
                        return;
                    }
                    if (actionType == "deleteWorlds")
                    {
                        var runIDs = root.GetProperty("runIDs").EnumerateArray().Select(x => x.GetString()!).ToList();
                        string recPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Config.RecordingsFolder);

                        Task.Run(() =>
                        {
                            foreach (var id in runIDs)
                            {
                                if (id.Contains("..") || id.Contains("/") || id.Contains("\\")) continue;

                                string dirToDelete = Path.Combine(recPath, id);
                                if (Directory.Exists(dirToDelete))
                                {
                                    try
                                    {
                                        Directory.Delete(dirToDelete, true);
                                        NEMO.Log($"[DELETE] Deleted archive {id}", "#ff99cc", ConsoleColor.Magenta);
                                    }
                                    catch (Exception ex) { Console.WriteLine($"[DELETE ERROR] {ex.Message}"); }
                                }
                            }

                            var baseDir = new DirectoryInfo(recPath);
                            totalRecordingBytes = baseDir.Exists ? baseDir.EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length) : 0;

                            var worlds = new List<object>();
                            if (baseDir.Exists)
                            {
                                foreach (var runDir in baseDir.GetDirectories())
                                {
                                    var files = runDir.GetFiles("Frame_*");
                                    if (files.Length > 0)
                                    {
                                        var ticks = files.Select(f => long.Parse(f.Name.Replace("Frame_", "").Replace(".json.gz", "").Replace(".json", "").Replace(".nemo", ""))).OrderBy(t => t).ToList();

                                        long totalSize = 0;
                                        int compressedCount = 0;
                                        foreach (var f in files)
                                        {
                                            totalSize += f.Length;
                                            if (f.Name.EndsWith(".gz")) compressedCount++;
                                        }

                                        var telemetryFile = new FileInfo(Path.Combine(runDir.FullName, "telemetry.json"));
                                        if (telemetryFile.Exists) totalSize += telemetryFile.Length;

                                        int pct = (int)Math.Round((float)compressedCount / files.Length * 100f);

                                        worlds.Add(new
                                        {
                                            id = runDir.Name,
                                            frameCount = files.Length,
                                            lastTick = ticks.Last(),
                                            ticks = ticks,
                                            sizeBytes = totalSize,
                                            compressedPct = pct
                                        });
                                    }
                                }
                            }
                            client.Send(JsonSerializer.Serialize(new { @event = "worldBank", worlds = worlds.OrderByDescending(w => ((dynamic)w).id) }));
                        });
                        return;
                    }
                    if (actionType == "toggleRecording")
                    {
                        isRecording = !isRecording;
                        BroadcastState();
                        return;
                    }
                    if (actionType == "restoreWorld")
                    {
                        string runID = root.GetProperty("runID").GetString()!;
                        long targetTick = root.GetProperty("tick").GetInt64();
                        string filePath = Path.Combine(Config.RecordingsFolder, runID, $"Frame_{targetTick:D9}.json.gz");

                        if (File.Exists(filePath))
                        {
                            NEMO.Log($"[RESTORE] Initiating restore to tick {targetTick}...", "#ffcc00", ConsoleColor.Yellow);

                            using FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                            using GZipStream gz = new GZipStream(fs, CompressionMode.Decompress);
                            using StreamReader sr = new StreamReader(gz);
                            string rawJson = sr.ReadToEnd();

                            JsonSerializerOptions options = new JsonSerializerOptions { IncludeFields = true, Converters = { new JsonStringEnumConverter() } };
                            WorldSnapshot? snap = JsonSerializer.Deserialize<WorldSnapshot>(rawJson, options);

                            if (snap != null)
                            {
                                var newWorld = new World(snap.width, snap.height, new List<Genome>());
                                newWorld.totalTicks = snap.tick;

                                newWorld.staticBlocks.Clear();
                                newWorld.activeFoods.Clear();
                                newWorld.creatures.Clear();
                                while (newWorld.pendingNewborns.TryDequeue(out _)) { }

                                for (int x = 0; x < snap.width; x++)
                                {
                                    for (int y = 0; y < snap.height; y++)
                                    {
                                        newWorld.grid[x + (y * newWorld.width)].isBlock = false;
                                        newWorld.grid[x + (y * newWorld.width)].occupant = null;
                                        newWorld.grid[x + (y * newWorld.width)].foodItem = null;
                                    }
                                }

                                foreach (var b in snap.blocks)
                                {
                                    newWorld.grid[b.x + (b.y * newWorld.width)].isBlock = true;
                                    newWorld.staticBlocks.Add(b);
                                }

                                foreach (var f in snap.foods)
                                {
                                    var food = new FoodItem(f.x, f.y, f.meat);
                                    newWorld.grid[f.x + (f.y * newWorld.width)].foodItem = food;
                                    newWorld.activeFoods.Add(food);
                                }

                                foreach (var c in snap.creatures)
                                {
                                    Creature restored = new Creature(c.x, c.y, c.genome, newWorld);

                                    restored.ID = string.IsNullOrEmpty(c.id) ? Guid.NewGuid() : Guid.Parse(c.id);

                                    restored.energy = c.energy;
                                    restored.facingDirection = c.dir;
                                    restored.lastFacing = c.dir;
                                    restored.age = c.age;
                                    restored.generation = c.generation;
                                    restored.lineageLifespan = c.lineageLifespan;
                                    restored.colorR = c.r;
                                    restored.colorG = c.g;
                                    restored.colorB = c.b;

                                    if (!string.IsNullOrEmpty(c.parentId)) restored.parentID = c.parentId;

                                    newWorld.grid[c.x + (c.y * newWorld.width)].occupant = restored;
                                    newWorld.creatures.Add(restored);
                                }

                                if (snap.stats != null)
                                {
                                    newWorld.emaEnergyIn = snap.stats.GetValueOrDefault("eIn", 0);
                                    newWorld.emaEnergyOut = snap.stats.GetValueOrDefault("eOut", 0);
                                    newWorld.emaBirths = snap.stats.GetValueOrDefault("births", 0);
                                    newWorld.emaDeaths = snap.stats.GetValueOrDefault("deaths", 0);
                                    newWorld.emaLifespan = snap.stats.GetValueOrDefault("lifeMeas", 0);
                                }

                                activeWorld = newWorld;
                                isPaused = true;

                                foreach (var client_ in clients.ToList()) client_.Send(JsonSerializer.Serialize(new { @event = "restoreComplete" }));
                                BroadcastState();
                                NEMO.Log($"[RESTORE] Successfully restored to tick {targetTick}!", "palegreen", ConsoleColor.Green);
                            }
                        }
                        return;
                    }
                    if (actionType == "decompressWorld")
                    {
                        string runID = root.GetProperty("runID").GetString()!;
                        string runFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Config.RecordingsFolder, runID);

                        if (Directory.Exists(runFolder))
                        {
                            Task.Run(() =>
                            {
                                var gzFiles = Directory.GetFiles(runFolder, "*.json.gz");
                                if (gzFiles.Length == 0) return;

                                long totalCompressedBytes = gzFiles.Sum(f => new FileInfo(f).Length);
                                double compressedMB = totalCompressedBytes / (1024.0 * 1024.0);

                                NEMO.Log($"[DECOMPRESS] Starting decompress of {gzFiles.Length} frames (~{compressedMB:F1}MB)...", "#ffcc00", ConsoleColor.Yellow);

                                Stopwatch timer = Stopwatch.StartNew();
                                int totalFiles = gzFiles.Length;
                                int decompressedCount = 0;
                                int lastReportedPercent = -1;
                                object socketLock = new object();

                                Parallel.ForEach(gzFiles, gzFile =>
                                {
                                    string jsonPath = gzFile.Substring(0, gzFile.Length - 3);

                                    try
                                    {
                                        using (FileStream fsIn = new FileStream(gzFile, FileMode.Open, FileAccess.Read, FileShare.Read))
                                        using (GZipStream gz = new GZipStream(fsIn, CompressionMode.Decompress))
                                        using (FileStream fsOut = new FileStream(jsonPath, FileMode.Create, FileAccess.Write))
                                        {
                                            gz.CopyTo(fsOut);
                                        }

                                        File.Delete(gzFile);

                                        int current = Interlocked.Increment(ref decompressedCount);
                                        int percent = (int)(((double)current / totalFiles) * 100);

                                        if (percent >= lastReportedPercent + 2 || current == totalFiles)
                                        {
                                            lock (socketLock)
                                            {
                                                lastReportedPercent = percent;
                                                client.Send(JsonSerializer.Serialize(new { @event = "decompressProgress", runID = runID, percent = percent }));
                                            }
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        Console.WriteLine($"[DECOMPRESS] Error {gzFile}: {ex.Message}");
                                    }
                                });

                                timer.Stop();
                                NEMO.Log($"[DECOMPRESS] {decompressedCount} frames decompressed in {timer.ElapsedMilliseconds}ms.", "palegreen", ConsoleColor.Green);

                                client.Send(JsonSerializer.Serialize(new { @event = "decompressComplete", runID = runID }));
                            });
                        }
                        return;
                    }
                    if(actionType == "startTrainer")
                    {
                        if (Interlocked.CompareExchange(ref isGenerating, 1, 0) == 1) return;

                        var filesArray = root.TryGetProperty("files", out var fElement)
                            ? fElement.EnumerateArray().Select(x => x.GetString()!).ToList()
                            : new List<string>();

                        Task.Run(() =>
                        {
                            try
                            {
                                foreach (var s in sims)
                                {
                                    if (s.trackedCreature != null) s.trackedCreature.trackedSlot = null;
                                    s.trackedCreature = null;
                                    foreach (var n in s.brain.neurons) n.host = null;
                                }

                                activeWorld = null;
                                currentMode = "Trainer";

                                var genMsg = JsonSerializer.Serialize(new { @event = "worldGenerating" });
                                foreach (var x in clients.ToList()) x.Send(genMsg);

                                TWorld newTrainer = new TWorld(Config.TworldWidth, Config.TworldHeight);

                                List<Genome> initPool = new List<Genome>();
                                if (filesArray.Count > 0)
                                {
                                    List<Genome> loadedGenomes = new List<Genome>();
                                    foreach (var f in filesArray)
                                    {
                                        string path = Path.Combine(Config.SavedGenomesFolder, f);
                                        if (File.Exists(path))
                                        {
                                            Genome? g = LoadGenomeFromDisk(File.ReadAllText(path));
                                            if (g != null) loadedGenomes.Add(g);
                                        }
                                    }

                                    for (int i = 0; i < Config.TnumActiveCreatures; i++)
                                    {
                                        if (loadedGenomes.Count > 0)
                                        {
                                            Genome baseGenome = loadedGenomes[TWorld.rand.Next(loadedGenomes.Count)].Clone();
                                            initPool.Add(GeneTools.MutateGenome(baseGenome));
                                        }
                                        else initPool.Add(GeneTools.GenerateGenome());
                                    }
                                }
                                else
                                {
                                    for (int i = 0; i < Config.TnumActiveCreatures; i++)
                                        initPool.Add(GeneTools.GenerateGenome());
                                }

                                newTrainer.ScatterActive(Config.TnumActiveCreatures, initPool);
                                newTrainer.ScatterBait(Config.TnumBaitCreatures);

                                activeTrainer = newTrainer;
                                isPaused = false;
                                BroadcastState();
                            }
                            finally
                            {
                                Interlocked.Exchange(ref isGenerating, 0);
                            }
                        });
                        return;
                    }
                    if (actionType == "updateTrainerWeights")
                    {
                        if (activeTrainer != null)
                        {
                            activeTrainer.weightKills = (float)root.GetProperty("kills").GetDecimal();
                            activeTrainer.weightEfficiency = (float)root.GetProperty("efficiency").GetDecimal();
                            activeTrainer.weightSurvival = (float)root.GetProperty("survival").GetDecimal();
                            activeTrainer.weightCEQ = (float)root.GetProperty("ceq").GetDecimal();

                            NEMO.Log($"[TWORLD] Weights -> Kills: {activeTrainer.weightKills}, Efficiency: {activeTrainer.weightEfficiency}, Survival: {activeTrainer.weightSurvival}, CEQ: {activeTrainer.weightCEQ}", "#ff99cc", ConsoleColor.Magenta);
                        }
                        return;
                    }
                    if (actionType == "findTrainerChampion")
                    {
                        if (activeTrainer != null)
                        {
                            var allCandidates = activeTrainer.activeCreatures.Concat(activeTrainer.deadActiveCreatures).ToList();
                            if (allCandidates.Count > 0)
                            {
                                var best = allCandidates.OrderByDescending(c => c.trainerScore).First();
                                trackedCreatureId = best.ID.ToString();
                                isPaused = true;
                                client.Send(JsonSerializer.Serialize(new { @event = "champFound", id = trackedCreatureId }));
                                BroadcastState();
                            }
                        }
                        return;
                    }
                    if (actionType == "validateCondition")
                    {
                        string code = root.GetProperty("code").GetString()!;
                        try
                        {
                            var script = CSharpScript.Create<bool>(code, ScriptOptions.Default.WithImports("System", "System.Math"), typeof(TVars));
                            var diagnostics = script.Compile();

                            var errors = diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error).ToList();
                            if (errors.Count > 0)
                            {
                                string errStr = string.Join("\n", errors.Select(e => e.GetMessage()));
                                client.Send(JsonSerializer.Serialize(new { @event = "conditionValidation", success = false, message = errStr }));
                            }
                            else
                            {
                                client.Send(JsonSerializer.Serialize(new { @event = "conditionValidation", success = true, message = "Compilation Successful." }));
                            }
                        }
                        catch (Exception ex)
                        {
                            client.Send(JsonSerializer.Serialize(new { @event = "conditionValidation", success = false, message = ex.Message }));
                        }
                        return;
                    }
                    if (actionType == "applyCondition")
                    {
                        string code = root.GetProperty("code").GetString()!;
                        if (activeTrainer != null)
                        {
                            if (string.IsNullOrWhiteSpace(code))
                            {
                                activeTrainer.customCondition = null;
                                NEMO.Log("[TRAINER] Custom condition removed.", "palegreen", ConsoleColor.Green);
                            }
                            else
                            {
                                try
                                {
                                    var script = CSharpScript.Create<bool>(code, ScriptOptions.Default.WithImports("System", "System.Math"), typeof(TVars));
                                    activeTrainer.customCondition = script.CreateDelegate();
                                    NEMO.Log("[TRAINER] Custom condition compiled successfully.", "palegreen", ConsoleColor.Green);
                                }
                                catch (Exception ex)
                                {
                                    NEMO.Log($"[TRAINER] Failed to condition: {ex.Message}", "tomato", ConsoleColor.Red);
                                }
                            }
                        }
                        return;
                    }
                }

                EditorAction? action = JsonSerializer.Deserialize<EditorAction>(
                        jsonMessage, new JsonSerializerOptions
                        {
                            IncludeFields = true,
                            Converters = { new JsonStringEnumConverter() }
                        });
                if (action == null) return;

                Simulation? actionSim = sims.FirstOrDefault(s => s.name == action.graph);
                if (actionSim == null) return;

                switch (action.action)
                {
                    case "editNeuron":
                        {
                            bool updated = false;

                            foreach (var g in actionSim.genome.genes)
                            {
                                if ($"{g.src.func}_{g.src.ID}" == action.nodeID)
                                {
                                    g.src.data = GeneTools.EncodeFields(g.src.func, action.fields);
                                    updated = true;
                                }
                                if ($"{g.tgt.func}_{g.tgt.ID}" == action.nodeID)
                                {
                                    g.tgt.data = GeneTools.EncodeFields(g.tgt.func, action.fields);
                                    updated = true;
                                }
                            }

                            if (updated)
                            {
                                RebuildLiveBrain(actionSim);
                                foreach (Neuron n in actionSim.trackedCreature.brain.neurons)
                                    if (n.func == NFunc.VisionBlockage || n.func == NFunc.VisionGenSim)
                                        n.GenerateVisionLUT();
                            }
                            break;
                        }
                    case "addConnection":
                        {
                            NeuronGeneData? src = null;
                            NeuronGeneData? tgt = null;
                            foreach (var gene in actionSim.genome.genes)
                            {
                                if ($"{gene.src.func}_{gene.src.ID}" == action.src) src = gene.src;
                                if ($"{gene.tgt.func}_{gene.tgt.ID}" == action.src) src = gene.tgt;
                                if ($"{gene.src.func}_{gene.src.ID}" == action.tgt) tgt = gene.src;
                                if ($"{gene.tgt.func}_{gene.tgt.ID}" == action.tgt) tgt = gene.tgt;
                            }
                            if (src == null || tgt == null) break;

                            if (!NormalizeDirection(ref src, ref tgt)) break;

                            Gene newGene = GeneTools.CreateGene(src, tgt, 0,
                                GeneTools.EncodeFloat(1f, 16, FType.SignedFloat));
                            newGene.graphID = actionSim.genome.GetNextGeneID();
                            actionSim.genome.genes.Add(newGene);
                            RebuildLiveBrain(actionSim);
                            break;
                        }
                    case "addNeuron":
                        {
                            NeuronGeneData? src = null;
                            foreach (var gene in actionSim.genome.genes)
                            {
                                if ($"{gene.src.func}_{gene.src.ID}" == action.src) src = gene.src;
                                if ($"{gene.tgt.func}_{gene.tgt.ID}" == action.src) src = gene.tgt;
                            }
                            if (src == null) break;

                            NFunc func = Enum.Parse<NFunc>(action.func);
                            NType type = NeuronDicts.TypesOfFuncs[(int)func];
                            NeuronGeneData newNeuron = new();
                            newNeuron.type = type;
                            newNeuron.func = func;
                            newNeuron.ID = actionSim.genome.GetNextNeuronID();
                            newNeuron.data = GeneTools.GenerateData(func);

                            if (!NormalizeDirection(ref src, ref newNeuron)) break;

                            Gene newGene = GeneTools.CreateGene(src, newNeuron, 0,
                                GeneTools.EncodeFloat(1, 16, FType.SignedFloat));
                            newGene.graphID = actionSim.genome.GetNextGeneID();
                            actionSim.genome.genes.Add(newGene);
                            RebuildLiveBrain(actionSim);
                            break;
                        }
                    case "toggleSlot":
                        {
                            Gene? gene = actionSim.genome.genes.FirstOrDefault(g => g.graphID == action.edgeID);
                            if (gene == null) break;
                            gene.slot = (byte)(gene.slot == 0 ? 1 : 0);
                            RebuildLiveBrain(actionSim);
                            break;
                        }
                    case "changeWeight":
                        {
                            Gene? gene = actionSim.genome.genes.FirstOrDefault(g => g.graphID == action.edgeID);
                            if (gene == null) break;

                            float w = (gene.weight / 65535f) * 2f - 1f;
                            w += action.delta;
                            w = Math.Clamp(w, -1f, 1f);
                            gene.weight = (ushort)((w + 1f) * 0.5f * 65535f);
                            RebuildLiveBrain(actionSim);
                            break;
                        }
                    case "deleteEdge":
                        {
                            Gene? gene = actionSim.genome.genes.FirstOrDefault(g => g.graphID == action.edgeID);
                            if (gene != null) actionSim.genome.genes.Remove(gene);
                            RebuildLiveBrain(actionSim);
                            break;
                        }
                }
            }
            catch (Exception ex)
            {
                NEMO.Log($"[ERROR] {ex.Message} \n {ex.StackTrace}", "tomato", ConsoleColor.Red);
            }
        }

        public static bool NormalizeDirection(ref NeuronGeneData src, ref NeuronGeneData tgt)
        {
            if (tgt.type == NType.Sensor && src.type != NType.Sensor)
                (src, tgt) = (tgt, src);

            if (src.type == NType.Action && tgt.type != NType.Action)
                (src, tgt) = (tgt, src);

            if (src.type == NType.Action) return false;
            if (tgt.type == NType.Sensor) return false;

            return true;
        }
        public static float Remap(float value, float from1, float to1, float from2, float to2)
        {
            return (value - from1) / (to1 - from1) * (to2 - from2) + from2;
        }

        public static void StartServers()
        {
            Process? browserProcess = null;

            void LaunchUI()
            {
                try
                {
                    if (browserProcess != null && !browserProcess.HasExited)
                    {
                        browserProcess.Kill();
                    }
                }
                catch { }

                try { browserProcess = Process.Start(new ProcessStartInfo { FileName = "chrome", Arguments = "--app=http://localhost:8000", UseShellExecute = true }); }
                catch
                {
                    try { browserProcess = Process.Start(new ProcessStartInfo { FileName = "msedge", Arguments = "--app=http://localhost:8000", UseShellExecute = true }); }
                    catch { browserProcess = Process.Start(new ProcessStartInfo { FileName = "http://localhost:8000", UseShellExecute = true }); }
                }
            }

            if (!Config.hideConsole)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine(@"
    _   _  _____ __  __  ____  
   | \ | || ____|  \/  |/ __ \ 
   |  \| ||  _| | |\/| | |  | |
   | |\  || |___| |  | | |__| |
   |_| \_||_____|_|  |_|\____/  v1.2
                                ");
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine("==================================================");
                Console.WriteLine(" PHYSICS ENGINE RUNNING");
                Console.WriteLine("--------------------------------------------------");
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine(" Controls:");
                Console.WriteLine(" [W] - Open / Reopen Web UI");
                Console.WriteLine(" [X] - Safely Quit Server");
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine("==================================================\n");
                Console.ResetColor();

                Task.Run(() =>
                {
                    while (true)
                    {
                        if (!Config.hideConsole && Console.KeyAvailable)
                        {
                            var key = Console.ReadKey(true).Key;
                            if (key == ConsoleKey.W) LaunchUI();
                            if (key == ConsoleKey.X) Environment.Exit(0);
                        }
                        Thread.Sleep(50);
                    }
                });
            }

            Task.Run(() =>
            {
                var listener = new System.Net.HttpListener();

                listener.Prefixes.Add("http://localhost:8000/");
                listener.Prefixes.Add("http://127.0.0.1:8000/");

                if (Config.customIPs != null)
                {
                    foreach (string ip in Config.customIPs)
                    {
                        if (string.IsNullOrWhiteSpace(ip)) continue;

                        string prefix = ip.Trim();
                        if (!prefix.StartsWith("http://") && !prefix.StartsWith("https://"))
                            prefix = "http://" + prefix;

                        if (prefix.LastIndexOf(':') <= 5)
                            prefix += ":8000";

                        if (!prefix.EndsWith("/"))
                            prefix += "/";

                        listener.Prefixes.Add(prefix);
                    }
                }

                bool httpStarted = false;
                bool fallbackAttempted = false;

                while (!httpStarted)
                {
                    try
                    {
                        listener.Start();
                        httpStarted = true;
                        if (!Config.hideConsole) NEMO.Log("[Network] Web Server running on port 8000", "palegreen", ConsoleColor.Green);
                        LaunchUI();
                    }
                    catch (System.Net.HttpListenerException e) when (e.ErrorCode == 5 && !fallbackAttempted)
                    {
                        fallbackAttempted = true;
                        NEMO.Log("[Network] Admin rights missing for custom network IPs. Falling back to localhost only...", "#ffcc00", ConsoleColor.Yellow);

                        try { listener.Close(); } catch { }

                        listener = new System.Net.HttpListener();
                        listener.Prefixes.Add("http://localhost:8000/");
                        listener.Prefixes.Add("http://127.0.0.1:8000/");
                    }
                    catch (Exception e)
                    {
                        NEMO.Log($"[Network] Waiting for port 8000 to free... ({e.Message})", "tomato", ConsoleColor.Red);
                        Thread.Sleep(1000);
                    }
                }

                while (true)
                {
                    try
                    {
                        var context = listener.GetContext();
                        string path = context.Request.Url.AbsolutePath;
                        if (path == "/") path = "/index.html";

                        string webDir = Config.WebFolder;
                        string fullPath = Path.GetFullPath(Path.Combine(webDir, path.TrimStart('/')));

                        bool isRootFile = false;
                        if (path.Equals("/Config.json", StringComparison.OrdinalIgnoreCase) ||
                            path.Equals("/neuronDefs.json", StringComparison.OrdinalIgnoreCase) ||
                            path.Equals("/dataDefs.json", StringComparison.OrdinalIgnoreCase))
                        {
                            string rootPath = Path.Combine(Config.projectDirectory, path.TrimStart('/'));
                            if (File.Exists(rootPath))
                            {
                                fullPath = rootPath;
                                isRootFile = true;
                            }
                        }

                        if (File.Exists(fullPath) && (fullPath.StartsWith(webDir) || isRootFile))
                        {
                            byte[] buffer = File.ReadAllBytes(fullPath);
                            context.Response.ContentLength64 = buffer.Length;
                            if (path.EndsWith(".html")) context.Response.ContentType = "text/html";
                            else if (path.EndsWith(".js")) context.Response.ContentType = "application/javascript";
                            else if (path.EndsWith(".css")) context.Response.ContentType = "text/css";
                            else if (path.EndsWith(".png")) context.Response.ContentType = "image/png";
                            else if (path.EndsWith(".json")) context.Response.ContentType = "application/json";

                            context.Response.OutputStream.Write(buffer, 0, buffer.Length);
                        }
                        else context.Response.StatusCode = 404;

                        context.Response.Close();
                    }
                    catch { }
                }
            });

            string GetToolPath(string toolName)
            {
                string localPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, toolName);
                if (File.Exists(localPath)) return localPath;

                string devPath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "Tools", toolName));
                if (File.Exists(devPath)) return devPath;

                return "";
            }

            string caddyPath = GetToolPath("caddy.exe");
            Process? caddyServer = null;
            if (!string.IsNullOrEmpty(caddyPath))
            {
                caddyServer = new Process();
                caddyServer.StartInfo.FileName = caddyPath;
                caddyServer.StartInfo.Arguments = "run";
                caddyServer.StartInfo.UseShellExecute = false;
                caddyServer.StartInfo.CreateNoWindow = true;
                caddyServer.StartInfo.WorkingDirectory = Path.GetDirectoryName(caddyPath);

                try { caddyServer.Start(); Console.ForegroundColor = ConsoleColor.Green; Console.WriteLine("[Caddy] Running on 8090"); Console.ResetColor(); } catch { }
            }

            string zrokPath = GetToolPath("zrok.exe");
            if (string.IsNullOrEmpty(zrokPath)) zrokPath = GetToolPath("zrok2.exe");

            Process? zrokServer = null;
            if (!string.IsNullOrEmpty(zrokPath) && !string.IsNullOrEmpty(caddyPath))
            {
                zrokServer = new Process();
                zrokServer.StartInfo.FileName = zrokPath;
                zrokServer.StartInfo.Arguments = "share public http://localhost:8090 -n public:nemo --backend-mode proxy";
                zrokServer.StartInfo.UseShellExecute = false;
                zrokServer.StartInfo.CreateNoWindow = true;
                zrokServer.StartInfo.WorkingDirectory = Path.GetDirectoryName(zrokPath);

                try { zrokServer.Start(); Console.ForegroundColor = ConsoleColor.Magenta; Console.WriteLine("[Zrok] Tunnel running."); Console.ResetColor(); } catch { }
            }

            AppDomain.CurrentDomain.ProcessExit += (s, e) =>
            {
                NEMO.Log("[SHUTDOWN] Initiating shutdown...", "tomato", ConsoleColor.Red);
                try { if (caddyServer != null && !caddyServer.HasExited) caddyServer.Kill(); } catch { }
                try { if (zrokServer != null && !zrokServer.HasExited) zrokServer.Kill(); } catch { }
                try { if (browserProcess != null && !browserProcess.HasExited) browserProcess.Kill(); } catch { }
            };

            var server = new WebSocketServer("ws://0.0.0.0:8181");
            bool socketStarted = false;
            while (!socketStarted)
            {
                try
                {
                    server.Start(socket =>
                    {
                        socket.OnOpen = () =>
                        {
                            clients.Add(socket);
                            hasUIConnectedOnce = true;
                            lastBroadcastTerrainVersion = -1;
                            NEMO.Log($"[Socket] UI Connected. Clients: {clients.Count}", "palegreen", ConsoleColor.Green);
                            BroadcastState();
                        };

                        socket.OnClose = () =>
                        {
                            clients.Remove(socket);
                            if (!Config.hideConsole) NEMO.Log($"[Socket] UI Disconnected. Clients: {clients.Count}", "tomato", ConsoleColor.Yellow);

                            if (clients.Count == 0 && Config.hideConsole && hasUIConnectedOnce)
                            {
                                Task.Delay(5000).ContinueWith(_ => {
                                    if (clients.Count == 0) Environment.Exit(0);
                                });
                            }
                        };

                        socket.OnMessage = message => { ProcessSocketMessage(message, sims, socket); };
                    });

                    socketStarted = true;
                }
                catch (Exception ex)
                {
                    NEMO.Log($"[Socket] Waiting for port 8181 to release... {ex}", "tomato", ConsoleColor.Red);
                    Thread.Sleep(1000);
                }
            }
        }
    }

    public class Simulation
    {
        public string name;
        public Genome genome;
        public Brain brain;
        public Creature? trackedCreature = null;

        public Simulation(string name)
        {
            this.name = name;
            this.genome = new Genome(new List<Gene>());
            this.genome.InitializeDefaultPhenotypes();
            this.brain = new Brain(new List<Neuron>(), new List<Connection>());
        }
    }

    public class EditorAction
    {
        public string action { get; set; }
        public float delta { get; set; }
        public string graph { get; set; }
        public string src { get; set; }
        public string tgt { get; set; }
        public string nodeID { get; set; }
        public int edgeID { get; set; }
        public string func { get; set; }
        public float x { get; set; }
        public float y { get; set; }
        public List<NeuronDataField> fields { get; set; }
    }

    public class WorldSnapshot
    {
        public long tick { get; set; }
        public int width { get; set; }
        public int height { get; set; }
        public Dictionary<string, float> stats { get; set; }
        public List<World.ExportBlock> blocks { get; set; }
        public List<World.ExportFood> foods { get; set; }
        public List<CreatureSnapshot> creatures { get; set; }
    }
    public class CreatureSnapshot
    {
        public string id { get; set; }
        public int x { get; set; }
        public int y { get; set; }
        public int dir { get; set; }
        public byte r { get; set; }
        public byte g { get; set; }
        public byte b { get; set; }
        public float energy { get; set; }
        public int age { get; set; }
        public int generation { get; set; }
        public float lineageLifespan { get; set; }
        public Genome genome { get; set; }
        public string parentId { get; set; }
    }
}