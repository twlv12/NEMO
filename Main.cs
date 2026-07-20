using Fleck;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NEMO
{
    public static class NEMO
    {
        public static List<IWebSocketConnection> clients = new List<IWebSocketConnection>();

        public static void Main()
        {
            int genomeUpdateTime = 250;
            int brainUpdateTime = 100;

            Config.Load();
            NeuronDicts.ExportNeuronDefs();
            NeuronDicts.ExportDataDefs();
            int previousView = Config.currentView;
            DateTime lastReload = DateTime.Now;

            List<Simulation> sims = [
                new("alpha", true),
                new("beta"),
                new("gamma"),
                new("delta")
            ];

            foreach (Simulation sim in sims)
            {
                Console.WriteLine($"Genome for [{sim.name}] graph:");
                sim.genome.PrintGenes();
            }

            #region Servers startup
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
            #endregion

            while (true)
            {
                if (previousView != Config.currentView)
                    {
                        OnViewChanged(sims);
                        previousView = Config.currentView;
                    }
                switch (Config.currentView)
                {
                    case 0:                        
                        UpdateGenomeView(sims);
                        Thread.Sleep(genomeUpdateTime);
                        break;

                    case 1:
                        UpdateBrainView(sims);
                        Thread.Sleep(brainUpdateTime);
                        break;
                }
                
            }
        }

        public static void UpdateBrainView(List<Simulation> sims)
        {
            foreach (var sim in sims)
            {
                sim.brain.UpdateAllNeurons();
                NeuralTools.RenderGraph(sim.brain, sim.name);
            }
        }
        public static void UpdateGenomeView(List<Simulation> sims)
        {
            foreach (var sim in sims)
            {
                GeneTools.MutateGenome(sim.genome);
                GeneTools.RenderGraph(sim.genome, sim.name);
            }
        }
        public static void OnViewChanged(List<Simulation> sims)
        {
            if (Config.currentView == 1)
            {
                foreach (var sim in sims)
                {
                    sim.brain = NeuralTools.GenomeToBrain(sim.genome);
                }
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

                    Simulation? sim = sims.FirstOrDefault(s => s.name == graph);
                    if (sim != null)
                    {
                        Neuron? neuron = sim.brain.neurons.FirstOrDefault(n => $"{n.func}_{n.ID}" == node);
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

                    if (actionType == "updateConfig")
                    {
                        string key = root.GetProperty("key").GetString()!;
                        float val = (float)root.GetProperty("value").GetDecimal();

                        // Use reflection to instantly update the static Config field in RAM
                        var field = typeof(Config).GetField(key, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                        if (field != null)
                        {
                            if (field.FieldType == typeof(int)) field.SetValue(null, (int)val);
                            else if (field.FieldType == typeof(float)) field.SetValue(null, val);
                            else if (field.FieldType == typeof(bool)) field.SetValue(null, val > 0.5f);
                        }
                        return; // Stop processing
                    }

                    if (actionType == "reloadConfig")
                    {
                        Config.Load(); // Reset RAM to default Config.json
                        return; // Stop processing
                    }

                    EditorAction? action = JsonSerializer.Deserialize<EditorAction>(
                        jsonMessage, new JsonSerializerOptions
                        {
                            IncludeFields = true,
                            Converters = { new JsonStringEnumConverter() }
                        });

                    if (action == null) return;

                    Simulation? sim = sims.FirstOrDefault(s => s.name == action.graph);
                    if (sim == null) return;

                    switch (action.action)
                    {
                        case "editNeuron":
                            {
                                Gene? gene = sim.genome.genes.FirstOrDefault(g =>
                                    $"{g.src.func}_{g.src.ID}" == action.nodeID
                                    || $"{g.tgt.func}_{g.tgt.ID}" == action.nodeID);
                                if (gene == null) break;

                                NeuronGeneData neuron = $"{gene.src.func}_{gene.src.ID}" == action.nodeID
                                    ? gene.src : gene.tgt;
                                neuron.data = GeneTools.EncodeFields(neuron.func, action.fields);
                                break;
                            }
                        case "addConnection":
                            {
                                NeuronGeneData? src = null;
                                NeuronGeneData? tgt = null;
                                foreach (var gene in sim.genome.genes)
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
                                newGene.graphID = sim.genome.GetNextGeneID();
                                sim.genome.genes.Add(newGene);
                                break;
                            }
                        case "addNeuron":
                            {
                                NeuronGeneData? src = null;
                                foreach (var gene in sim.genome.genes)
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
                                newNeuron.ID = sim.genome.GetNextNeuronID();
                                newNeuron.data = GeneTools.GenerateData(func);

                                if (!NormalizeDirection(ref src, ref newNeuron)) break;

                                Gene newGene = GeneTools.CreateGene(src, newNeuron, 0,
                                    GeneTools.EncodeFloat(1, 16, FType.SignedFloat));
                                newGene.graphID = sim.genome.GetNextGeneID();
                                sim.genome.genes.Add(newGene);
                                break;
                            }
                        case "toggleSlot":
                            {
                                Gene? gene = sim.genome.genes.FirstOrDefault(g => g.graphID == action.edgeID);
                                if (gene == null) break;

                                gene.slot = (byte)(gene.slot == 0 ? 1 : 0);
                                break;
                            }
                        case "changeWeight":
                            {
                                Gene? gene = sim.genome.genes.FirstOrDefault(g => g.graphID == action.edgeID);
                                if (gene == null) break;

                                float w = (gene.weight / 65535f) * 2f - 1f;
                                w += action.delta;
                                w = Math.Clamp(w, -1f, 1f);
                                gene.weight = (ushort)((w + 1f) * 0.5f * 65535f);
                                break;
                            }
                        case "deleteEdge":
                            {
                                Gene? gene = sim.genome.genes.FirstOrDefault(g => g.graphID == action.edgeID);
                                if (gene != null) sim.genome.genes.Remove(gene);
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
        public bool isSimple;

        public Simulation(string name, bool isSimple=false)
        {
            this.name = name;
            if (!isSimple)
                genome = GeneTools.GenerateGenome();
            else
                genome = GeneTools.GenerateSimpleGenome();
            brain = NeuralTools.GenomeToBrain(genome);
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
