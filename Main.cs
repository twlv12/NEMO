using Fleck;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NEMO
{    
    //TODO - NEVER IN ORDER
    //add occasionaly auto-saving of selected champion mode
    //biome migration doesnt remove old fertility, governor conflict.
    //add fallback c# not running page, constantly running background service, repeats attempt loading
    //reert popdensity back to nicer slow gaussian blur since it pauses automatically now
    //add more sensory neurons to prioritize environment-reactive behaviour rather than random
    //fix brain structure to be more sensor-based, less redunant math
    //cull unsuable/useless neurons and connections from brain 
    //fix graph to be relative to simspeed
    //add hunting stats to telemetry
    //add world recording system to start/stop recording a world, then play it back,
    //and be able to restore to any point
    //test carnivore and parasite thermodynamics
    //add fertility, carnivory overlays

    public static class NEMO
    {
        #region Initializations
        public static bool isBroadcasting = false;
        public static List<IWebSocketConnection> clients = new List<IWebSocketConnection>();
        public static World? activeWorld = null;
        public static bool isPaused = false;
        public static string trackedCreatureId = "";

        public static int extinctionCount = 0;
        public static int savedGenomesTotal = 0;
        public static int savedGenomesSession= 0;

        public static List<(byte r, byte g, byte b)> sessionSavedColors = new();

        public static Stopwatch tpsTimer = Stopwatch.StartNew();
        public static int ticksThisSecond = 0;
        public static int currentTPS = 0;
        public static bool autoRestart = false;

        public static bool pauseOnExtinction = true;
        public static bool worldWasEmpty = false;
        public static bool safeEditMode = true;
        public static bool disableGovernor = false;
        public static bool disableEnergyDrain = false;

        public static double emaSimTime = 0;
        public static double emaUiTime = 0;

        public static bool repopExtinct = false;
        public static bool repopChamp = false;
        public static bool repopEditor = false;
        #endregion

        public static void Main()
        {
            Config.Load();
            NeuronDicts.ExportNeuronDefs();
            NeuronDicts.ExportDataDefs();
            int previousView = Config.currentView;

            List<Simulation> sims = [
                new("alpha"),
                new("beta"),
                new("gamma"),
                new("delta")
            ];

            Directory.CreateDirectory(Config.SavedGenomesFolder);
            savedGenomesTotal = Directory.GetFiles(Config.SavedGenomesFolder, "*.json").Length;

            #region Servers Handler
            #region Startup
            Process pythonServer = new Process();
            pythonServer.StartInfo.FileName = "python";
            pythonServer.StartInfo.Arguments = "-m http.server 8000";
            pythonServer.StartInfo.UseShellExecute = false;
            pythonServer.StartInfo.RedirectStandardOutput = true;
            pythonServer.StartInfo.RedirectStandardError = true;
            pythonServer.StartInfo.WorkingDirectory = Config.GraphOutputFolder;
            
            pythonServer.OutputDataReceived += (s, e) => { if (e.Data != null) Console.WriteLine($"[Python] {e.Data}"); };
            pythonServer.ErrorDataReceived += (s, e) => { if (e.Data != null) Console.WriteLine($"[Python] {e.Data}"); };
            
            pythonServer.Start();
            pythonServer.BeginOutputReadLine();
            pythonServer.BeginErrorReadLine();
            
            Process caddyServer = new Process();
            caddyServer.StartInfo.FileName = Path.Combine(Config.GraphOutputFolder, "caddy.exe");
            caddyServer.StartInfo.Arguments = "run";
            caddyServer.StartInfo.UseShellExecute = false;
            caddyServer.StartInfo.CreateNoWindow = true;
            caddyServer.StartInfo.WorkingDirectory = Config.GraphOutputFolder;
            
            caddyServer.StartInfo.RedirectStandardOutput = true;
            caddyServer.StartInfo.RedirectStandardError = true;
            caddyServer.OutputDataReceived += (s, e) => { if (e.Data != null) Console.WriteLine($"[Caddy] {e.Data}"); };
            caddyServer.ErrorDataReceived += (s, e) => { if (e.Data != null) Console.WriteLine($"[Caddy] {e.Data}"); };
            
            try
            {
                caddyServer.Start();
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("[Caddy] Running on 8090");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[Caddy] Failed to startup: {ex.Message}");
                Console.ResetColor();
            }
            
            Process zrokServer = new Process();
            zrokServer.StartInfo.FileName = Path.Combine(Config.GraphOutputFolder, "zrok2.exe");
            zrokServer.StartInfo.Arguments = "share public http://localhost:8090 -n public:nemo --backend-mode proxy";
            zrokServer.StartInfo.UseShellExecute = false;
            zrokServer.StartInfo.CreateNoWindow = true;
            zrokServer.StartInfo.WorkingDirectory = Config.GraphOutputFolder;
            
            try
            {
                zrokServer.Start();
                Console.ForegroundColor = ConsoleColor.Magenta;
                Console.WriteLine("[Zrok] Tunnel running.");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[Zrok] Failed to startup: {ex.Message}");
                Console.ResetColor();
            }
            
            AppDomain.CurrentDomain.ProcessExit += (s, e) => {
                if (!pythonServer.HasExited) pythonServer.Kill();
                if (!caddyServer.HasExited) caddyServer.Kill();
                if (!zrokServer.HasExited) zrokServer.Kill();
            };
            #endregion
            
            var server = new WebSocketServer("ws://0.0.0.0:8181");
            server.Start(socket =>
            {
                socket.OnOpen = () =>
                {
                    clients.Add(socket);
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"Browser connected {socket.ConnectionInfo.ClientIpAddress}");
                    Console.ResetColor();
                    BroadcastState();
                };
            
                socket.OnClose = () =>
                {
                    clients.Remove(socket);
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("Browser disconnected");
                    Console.ResetColor();
                };
            
                socket.OnMessage = message =>
                {
                    ProcessSocketMessage(message, sims, socket);
                };
            });
            
            Stopwatch tickTimer = Stopwatch.StartNew();
            Stopwatch petriTimer = Stopwatch.StartNew();
            Stopwatch brainTimer = Stopwatch.StartNew();
            Stopwatch genomeTimer = Stopwatch.StartNew();
            Stopwatch profiler = new Stopwatch();
            #endregion

            while (true)
            {
                profiler.Restart();

                World? currentWorld = activeWorld;

                if (currentWorld != null && !isPaused)
                {
                    double delay = Config.maxSpeed ? 0 : 1000.0 / Config.tickRate;
                    if (delay == 0 || tickTimer.ElapsedMilliseconds >= delay)
                    {
                        profiler.Restart();
                        currentWorld.Update();
                        emaSimTime = (emaSimTime * 0.95) + (profiler.Elapsed.TotalMilliseconds * 0.05);

                        if (delay > 0) tickTimer.Restart();

                        ticksThisSecond++;
                        if (tpsTimer.ElapsedMilliseconds >= 1000)
                        {
                            currentTPS = ticksThisSecond;
                            ticksThisSecond = 0;
                            tpsTimer.Restart();
                        }

                        bool worldIsEmpty = currentWorld.creatures.Count == 0 && !Config.maintainPopulation;

                        if (worldIsEmpty && !worldWasEmpty)
                        {
                            extinctionCount++;

                            string logLine = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] extinction #{extinctionCount} | ticks: {currentWorld.totalTicks} | max Gen: {currentWorld.highestGeneration} | avg Ein: {currentWorld.emaEnergyIn:F1} | avg Eout: {currentWorld.emaEnergyOut:F1}";
                            File.AppendAllText($"{Config.SavedGenomesFolder}ExtinctionLogs.txt", logLine + Environment.NewLine);

                            if (currentWorld.bestGenome != null && currentWorld.highestSignificance >= Config.selectionThreshold)
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
                                    SaveGenomeToDisk(currentWorld.bestGenome, $"Ext{extinctionCount}_Gen{currentWorld.highestGeneration}_Sig{currentWorld.highestSignificance:F1}");
                                }
                                else
                                {
                                    Console.ForegroundColor = ConsoleColor.DarkGray;
                                    Console.WriteLine($"[Extinction] Genome discarded. Too similar to an already saved champion.");
                                    Console.ResetColor();
                                }
                            }

                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine(logLine);
                            Console.ResetColor();

                            if (pauseOnExtinction)
                            {
                                isPaused = true;
                                foreach (var client in clients.ToList())
                                    client.Send(JsonSerializer.Serialize(new { @event = "simEnded" }));
                            }

                            BroadcastState();
                        }

                        worldWasEmpty = worldIsEmpty;
                    }
                }

                emaSimTime = (emaSimTime * 0.95) + (profiler.Elapsed.TotalMilliseconds * 0.05);
                profiler.Restart();

                if (petriTimer.ElapsedMilliseconds >= Config.uiRate)
                {
                    if (activeWorld != null) UpdatePetriView(activeWorld);
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
                if (isPaused)
                {
                    Thread.Sleep(10);
                }
            }
        }

        public static void BroadcastState()
        {
            var state = new
            {
                @event = "syncState",
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
                wastePenalty = Config.wastePenaltyMultiplier
            };
            string json = JsonSerializer.Serialize(state);
            foreach (var client in clients.ToList()) client.Send(json);
        }

        public static void UpdatePetriView(World world)
        {
            if (isBroadcasting) return;
            isBroadcasting = true;

            Creature[] creaturesSnap = world.creatures.ToArray();
            FoodItem[] foodsSnap = world.activeFoods.ToArray();
            World.ExportBlock[] blocksSnap = world.staticBlocks.ToArray();
            IWebSocketConnection[] clientsSnap = clients.ToArray();

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

                if (isPaused || isDead || sim.trackedCreature == null)
                {
                    sim.brain.UpdateAllNeurons();
                }

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

            File.WriteAllText($"{Config.SavedGenomesFolder}{prefix}_{safeHash}.json", jsonGenome);

            savedGenomesTotal++;
            savedGenomesSession++;
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"[SAVED] {prefix}_{safeHash}.json");
            Console.ResetColor();
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
                Console.WriteLine($"[LOAD] Loaded genome from disk.");

                return genome;
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[LOAD ERROR] Failed to deserialize genome: {ex.Message}");
                Console.ResetColor();
                return null;
            }
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
                    if (actionType == "startWorld")
                    {
                        foreach (var s in sims)
                        {
                            if (s.trackedCreature != null) s.trackedCreature.trackedSlot = null;
                            s.trackedCreature = null;

                            foreach (var n in s.brain.neurons) n.host = null;
                        }
                        activeWorld = null;

                        var genMsg = JsonSerializer.Serialize(new { @event = "worldGenerating" });
                        foreach (var x in clients.ToList()) x.Send(genMsg);

                        activeWorld = new World(Config.worldWidth, Config.worldHeight, new List<Genome>());
                        isPaused = false;
                        BroadcastState();
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
                            c.energy = c.startingEnergy;

                            activeWorld.grid[x, y].occupant = c;
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
                        Console.WriteLine($"[LOAD] Slot: {slot} | Creature ID: {creatureId}");

                        if (activeWorld != null)
                        {
                            var c = activeWorld.creatures.FirstOrDefault(x => x.ID.ToString() == creatureId);
                            if (c != null)
                            {
                                var loadSim = sims.FirstOrDefault(s => s.name == slot);
                                if (loadSim != null)
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
                        Config.Load();
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
                    if (actionType == "newCreature")
                    {
                        string slot = root.GetProperty("slot").GetString()!;
                        var targetSim = sims.FirstOrDefault(s => s.name == slot);

                        if (targetSim != null && activeWorld != null)
                        {
                            Random rand = new Random();
                            while (true)
                            {
                                int x = rand.Next(0, activeWorld.width);
                                int y = rand.Next(0, activeWorld.height);

                                if (!activeWorld.grid[x, y].isBlock && activeWorld.grid[x, y].occupant == null)
                                {
                                    Creature c = new Creature(x, y, targetSim.genome, activeWorld);
                                    c.energy = c.startingEnergy;

                                    activeWorld.grid[x, y].occupant = c;
                                    activeWorld.pendingNewborns.Enqueue(c); 
                                    return;
                                }
                            }
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
                            activeWorld.grid[x, y].isBlock = false;
                            activeWorld.staticBlocks.RemoveAll(b => b.x == x && b.y == y);

                            if (activeWorld.grid[x, y].foodItem != null)
                            {
                                lock (activeWorld.activeFoods)
                                {
                                    activeWorld.activeFoods.Remove(activeWorld.grid[x, y].foodItem!);
                                }
                                activeWorld.grid[x, y].foodItem = null;
                            }
                            if (activeWorld.grid[x, y].occupant != null)
                            {
                                activeWorld.grid[x, y].occupant.energy = -1;
                            }
                        }
                        else if (type == "wall")
                        {
                            if (activeWorld.grid[x, y].occupant == null && activeWorld.grid[x, y].foodItem == null)
                            {
                                activeWorld.grid[x, y].isBlock = true;
                                if (!activeWorld.staticBlocks.Any(b => b.x == x && b.y == y))
                                    activeWorld.staticBlocks.Add(new World.ExportBlock { x = x, y = y });
                            }
                        }
                        else if (type == "plant" || type == "meat")
                        {
                            if (!activeWorld.grid[x, y].isBlock && activeWorld.grid[x, y].foodItem == null && activeWorld.grid[x, y].occupant == null)
                            {
                                var f = new FoodItem(x, y, type == "meat");
                                if (type == "meat") f.nutrition = 1000f;
                                activeWorld.grid[x, y].foodItem = f;

                                lock (activeWorld.activeFoods)
                                {
                                    activeWorld.activeFoods.Add(f);
                                }
                            }
                        }
                        else if (type == "dummy")
                        {
                            if (!activeWorld.grid[x, y].isBlock && activeWorld.grid[x, y].occupant == null)
                            {
                                Genome dummyGen = new Genome(new List<Gene>());
                                dummyGen.InitializeDefaultPhenotypes();

                                Creature dummy = new Creature(x, y, dummyGen, activeWorld);
                                dummy.startingEnergy = Config.baseStartingEnergy;
                                dummy.energy = Config.baseStartingEnergy;

                                activeWorld.grid[x, y].occupant = dummy;
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
                            if (activeWorld.grid[c.x, c.y].occupant == c)
                            {
                                activeWorld.grid[c.x, c.y].occupant = null;
                            }

                            if (!activeWorld.grid[targetX, targetY].isBlock)
                            {
                                c.x = targetX;
                                c.y = targetY;
                                c.lastX = targetX;
                                c.lastY = targetY;
                                c.facingDirection = Math.Clamp(dir, 0, 7);
                                c.lastFacing = c.facingDirection;

                                activeWorld.grid[targetX, targetY].occupant = c;
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
                            else if (f.Name.StartsWith("Champ") || f.Name.StartsWith("ManualChamp")) champ.Add(f.Name);
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
                                if (!activeWorld.grid[x, y].isBlock && activeWorld.grid[x, y].occupant == null)
                                {
                                    Genome gen = pool[World.rand.Next(pool.Count)].Clone();
                                    Creature c = new Creature(x, y, gen, activeWorld);
                                    activeWorld.grid[x, y].occupant = c;
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
                                if (!activeWorld.grid[x, y].isBlock && activeWorld.grid[x, y].occupant == null && activeWorld.grid[x, y].foodItem == null)
                                {
                                    var f = new FoodItem(x, y, type == "meat");
                                    if (type == "meat") f.nutrition = 1000f;
                                    activeWorld.grid[x, y].foodItem = f;
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
                                if (!activeWorld.grid[x, y].isBlock && activeWorld.grid[x, y].occupant == null)
                                {
                                    Creature dummy = new Creature(x, y, dummyGen, activeWorld);
                                    dummy.startingEnergy = Config.baseStartingEnergy;
                                    dummy.energy = Config.baseStartingEnergy;

                                    activeWorld.grid[x, y].occupant = dummy;
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
                            else
                            {
                                champ = activeWorld.creatures
                                    .OrderByDescending(c => ((c.age * 0.5f) + (c.lineageLifespan * 0.5f)) / Math.Max(1f, c.startingEnergy / Math.Max(0.001f, c.GetBaseTickCost())))
                                    .ThenByDescending(c => c.generation).First();
                            }

                            trackedCreatureId = champ.ID.ToString();
                            isPaused = true;
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
                                    if (n.func == NFunc.Blockage || n.func == NFunc.GeneSimilarity)
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
            catch { return; }
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
}