using Fleck;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NEMO
{    
    //TODO
    //add feedback to genome edit buttons like instantiate.
    //

    public static class NEMO
    {
        #region Initializations
        public static List<IWebSocketConnection> clients = new List<IWebSocketConnection>();
        public static World? activeWorld = null;
        public static bool isPaused = false;
        public static int extinctionCount = 0;
        public static int savedGenomesTotal = 0;
        public static int savedGenomesSession= 0;

        public static Stopwatch tpsTimer = Stopwatch.StartNew();
        public static int ticksThisSecond = 0;
        public static int currentTPS = 0;
        public static bool autoRestart = false;

        public static bool pauseOnExtinction = true;
        public static bool worldWasEmpty = false;

        public static double emaSimTime = 0;
        public static double emaUiTime = 0;
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
            savedGenomesTotal = Directory.GetFiles("SavedGenomes", "*.json").Length;

            #region Servers Handler
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

            AppDomain.CurrentDomain.ProcessExit += (s, e) => {
                if (!pythonServer.HasExited) pythonServer.Kill();
            };

            var server = new WebSocketServer("ws://127.0.0.1:8181");
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
                    ProcessSocketMessage(message, sims);
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

                if (activeWorld != null && !isPaused)
                {
                    int delay = Config.maxSpeed ? 0 : 1000 / Config.tickRate;
                    if (delay == 0 || tickTimer.ElapsedMilliseconds >= delay)
                    {
                        activeWorld.Update();
                        if (delay > 0) tickTimer.Restart();

                        ticksThisSecond++;
                        if (tpsTimer.ElapsedMilliseconds >= 1000)
                        {
                            currentTPS = ticksThisSecond;
                            ticksThisSecond = 0;
                            tpsTimer.Restart();
                        }

                        bool worldIsEmpty = activeWorld.creatures.Count == 0 && !Config.maintainPopulation;

                        if (worldIsEmpty && !worldWasEmpty)
                        {
                            extinctionCount++;

                            string logLine = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] extinction #{extinctionCount} | ticks: {activeWorld.totalTicks} | max Gen: {activeWorld.highestGeneration} | avg Ein: {activeWorld.emaEnergyIn:F1} | avg Eout: {activeWorld.emaEnergyOut:F1}";
                            File.AppendAllText($"{Config.SavedGenomesFolder}ExtinctionLogs.txt", logLine + Environment.NewLine);

                            if (activeWorld.bestGenome != null)
                                SaveGenomeToDisk(activeWorld.bestGenome, $"Ext{extinctionCount}_Gen{activeWorld.highestGeneration}");

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

                if (petriTimer.ElapsedMilliseconds >= 33)
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
                if (!Config.maxSpeed || isPaused) Thread.Sleep(1);
            }
        }

        public static void BroadcastState()
        {
            var state = new { @event = "syncState", isPaused = isPaused, 
                autoRestart = autoRestart, pauseOnExtinction = pauseOnExtinction };
            string json = JsonSerializer.Serialize(state);
            foreach (var client in clients.ToList()) client.Send(json);
        }

        public static void UpdatePetriView(World world)
        {
            string stateJson = world.GetStateJson();
            foreach (var client in clients.ToList())
            {
                client.Send(stateJson);
            }
        }
        public static void UpdateBrainView(List<Simulation> sims)
        {
            foreach (var sim in sims)
            {
                bool isDeadOrEmpty = sim.trackedCreature == null || sim.trackedCreature.isDead;

                if (isPaused || isDeadOrEmpty)
                {
                    if (sim.trackedCreature != null)
                    {
                        sim.brain.UpdateAllNeurons();
                    }
                }

                NeuralTools.RenderGraph(sim.brain, sim.name, isDeadOrEmpty, isPaused);
            }
        }
        public static void UpdateGenomeView(List<Simulation> sims)
        {
            foreach (var sim in sims)
            {
                GeneTools.RenderGraph(sim.genome, sim.name);
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
                IncludeFields = true 
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
                    IncludeFields = true 
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

        public static void ProcessSocketMessage(string jsonMessage, List<Simulation> sims)
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
                        activeWorld = new World(Config.worldWidth, Config.worldHeight, new List<Genome>());
                        isPaused = false;
                        BroadcastState();
                        return;
                    }
                    if (actionType == "togglePause")
                    {
                        isPaused = !isPaused;
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
                            activeWorld.creatures.Add(c);
                            activeWorld.grid[x, y].occupant = c;

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
                        return;
                    }
                    if (actionType == "reloadConfig")
                    {
                        Config.Load();
                        return;
                    }
                    if (actionType == "saveChampion")
                    {
                        if (activeWorld != null && activeWorld.creatures.Count > 0)
                        {
                            var champ = activeWorld.creatures.OrderByDescending(c => c.generation).ThenByDescending(c => c.energy).First();
                            SaveGenomeToDisk(champ.genome, $"ManualChamp_Gen{champ.generation}");
                        }
                        return;
                    }
                    if (actionType == "saveSpecific")
                    {
                        string slot = root.GetProperty("slot").GetString()!;
                        var targetSim = sims.FirstOrDefault(s => s.name == slot);
                        if (targetSim != null && targetSim.genome.genes.Count > 0)
                        {
                            SaveGenomeToDisk(targetSim.genome, $"Slot_{slot.ToUpper()}");
                        }
                        return;
                    }
                    if (actionType == "loadSpecific")
                    {
                        string slot = root.GetProperty("slot").GetString()!;
                        string fileData = root.GetProperty("fileData").GetString()!;
                        var targetSim = sims.FirstOrDefault(s => s.name == slot);

                        Genome? gen = LoadGenomeFromDisk(fileData);
                        if (gen != null)
                        {
                            targetSim.genome = gen;
                            RebuildLiveBrain(targetSim);
                        }
                        return;
                    }
                    if (actionType == "randGenome")
                    {
                        string slot = root.GetProperty("slot").GetString()!;
                        var targetSim = sims.FirstOrDefault(s => s.name == slot);

                        targetSim.trackedCreature = null;

                        targetSim.genome = GeneTools.GenerateGenome();
                        RebuildLiveBrain(targetSim);
                    }
                    if (actionType == "newCreature")
                    {
                        string slot = root.GetProperty("slot").GetString()!;
                        var targetSim = sims.FirstOrDefault(s => s.name == slot);

                        Random rand = new Random();

                        while (true)
                        {
                            int x = rand.Next(0, activeWorld.width);
                            int y = rand.Next(0, activeWorld.height);

                            if (!activeWorld.grid[x, y].isBlock && activeWorld.grid[x, y].occupant == null)
                            {
                                Creature c = new Creature(x, y, targetSim.genome, activeWorld);
                                activeWorld.creatures.Add(c);
                                activeWorld.grid[x, y].occupant = c;

                                return;
                            }
                        }
                    }
                    if (actionType == "togglePauseOnExtinction")
                    {
                        pauseOnExtinction = !pauseOnExtinction;
                        BroadcastState();
                        return;
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
                                Gene? gene = actionSim.genome.genes.FirstOrDefault(g =>
                                    $"{g.src.func}_{g.src.ID}" == action.nodeID
                                    || $"{g.tgt.func}_{g.tgt.ID}" == action.nodeID);
                                if (gene == null) break;

                                NeuronGeneData neuron = $"{gene.src.func}_{gene.src.ID}" == action.nodeID
                                    ? gene.src : gene.tgt;
                                neuron.data = GeneTools.EncodeFields(neuron.func, action.fields);

                                // Hot-reload live neuron variables
                                var liveNeuron = actionSim.brain.neurons.FirstOrDefault(n => n.ID == neuron.ID);
                                if (liveNeuron != null)
                                {
                                    // FIXED: Now correctly calling NeuralTools
                                    liveNeuron.dataFields = NeuralTools.NeuronDataToFields(neuron);

                                    // FIXED: Regenerate immediately instead of assigning null to array
                                    if (liveNeuron.func == NFunc.Blockage || liveNeuron.func == NFunc.GeneSimilarity)
                                        liveNeuron.GenerateVisionLUT();
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
                                NType type = NeuronDicts.TypesOfFuncs[func];
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