using System.Text.Json;
using System.Text.Json.Serialization;

namespace NEMO //Neural Emergence thru Mutating Organisms
{
    public static class NEMO
    {
        public static void Main()
        {
            int genomeUpdateTime = 250;
            int brainUpdateTime = 100;

            Config.Load();
            NeuronDicts.ExportNeuronDefs();
            NeuronDicts.ExportDataDefs();
            int previousView = Config.currentView;
            DateTime lastReload = DateTime.Now;

            List<Simulation> sims =
            [
                new("alpha", true),
                new("beta"),
                new("gamma"),
                new("delta")
            ];
            foreach (var sim in sims)
                sim.genome.PrintGenes();
            
            while (true)
            {
                ReloadRuntime(ref lastReload);
                if (previousView != Config.currentView){
                    OnViewChanged(sims);
                    previousView = Config.currentView;
                }

                switch (Config.currentView)
                {
                    case 0:
                        ApplyEditorActions(sims);
                        UpdateGenomeView(sims);
                        Thread.Sleep(genomeUpdateTime);
                        break;

                    case 1:
                        ApplyStimuli(sims);
                        UpdateBrainView(sims);
                        Thread.Sleep(brainUpdateTime);
                        break;
                }
            }
        }

        public static void UpdateBrainView(List<Simulation> sims)
        {
            foreach (var sim in sims){
                sim.brain.UpdateAllNeurons();
                NeuralTools.RenderGraph(sim.brain, sim.name);
            }
        }
        public static void UpdateGenomeView(List<Simulation> sims)
        {
            foreach (var sim in sims){
                GeneTools.MutateGenome(sim.genome);
                GeneTools.RenderGraph(sim.genome, sim.name);
            }
        }
        public static void OnViewChanged(List<Simulation> sims)
        {
            if (Config.currentView == 1){
                foreach (var sim in sims){
                    sim.brain = NeuralTools.GenomeToBrain(sim.genome);
                }
            }
        }

        public static void ApplyStimuli(List<Simulation> sims)
        {
            try
            {
                string json = File.ReadAllText(Config.StimuliFile);

                if (json.Trim() == "{}")
                    return;

                using JsonDocument doc = JsonDocument.Parse(json);
                string graph = doc.RootElement.GetProperty("graph").GetString();
                string node = doc.RootElement.GetProperty("node").GetString();
                float value = (float)doc.RootElement.GetProperty("value").GetDecimal();

                Simulation? sim = sims.FirstOrDefault
                    (s => s.name == graph);
                if (sim != null)
                {
                    Neuron? neuron = sim.brain.neurons.FirstOrDefault
                        (n => $"{n.func}_{n.ID}" == node);
                    if (neuron != null)
                    {
                        //Console.WriteLine($"{neuron.func}_{neuron.ID} Clicked!");
                        neuron.slotASum = value;
                        neuron.slotBSum = value;
                        neuron.value = value;
                    }
                }

                File.WriteAllText(Config.StimuliFile, "{}");
            }
            catch{
                return;
            }
        }
        public static void ApplyEditorActions(List<Simulation> sims)
        {
            EditorAction? action = null;
            try
            {
                string json = File.ReadAllText(Config.EditorActionFile);
                if (string.IsNullOrWhiteSpace(json))
                    return;

                action = JsonSerializer.Deserialize<EditorAction>
                    (json, new JsonSerializerOptions
                    {
                        IncludeFields = true,
                        Converters = { new JsonStringEnumConverter() }
                    });
                if (action == null)
                    return;
            }
            catch { return; }
            
            Simulation? sim = sims.FirstOrDefault
                (s => s.name == action.graph);
            if (sim == null)
                return;

            switch (action.action)
            {
                case "editNeuron":
                    {
                        Gene? gene = sim.genome.genes.FirstOrDefault(g =>
                            $"{g.src.func}_{g.src.ID}" == action.nodeID
                            || $"{g.tgt.func}_{g.tgt.ID}" == action.nodeID);
                        if (gene == null)
                            break;

                        NeuronGeneData neuron =$"{gene.src.func}_{gene.src.ID}" == action.nodeID
                            ? gene.src
                            : gene.tgt;
                        neuron.data = GeneTools.EncodeFields(neuron.func, action.fields);

                        break;
                    }
                case "addConnection":
                    {
                        NeuronGeneData? src = null;
                        NeuronGeneData? tgt = null;
                        foreach (var gene in sim.genome.genes)
                        {
                            if ($"{gene.src.func}_{gene.src.ID}" == action.src)
                                src = gene.src;
                            if ($"{gene.tgt.func}_{gene.tgt.ID}" == action.src)
                                src = gene.tgt;
                            if ($"{gene.src.func}_{gene.src.ID}" == action.tgt)
                                tgt = gene.src;
                            if ($"{gene.tgt.func}_{gene.tgt.ID}" == action.tgt)
                                tgt = gene.tgt;
                        }
                        if (src == null || tgt == null)
                            break;

                        if (!NormalizeDirection(ref src, ref tgt))
                            break;

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
                            if ($"{gene.src.func}_{gene.src.ID}" == action.src)
                                src = gene.src;
                            if ($"{gene.tgt.func}_{gene.tgt.ID}" == action.src)
                                src = gene.tgt;
                        }
                        if (src == null)
                            break; 

                        NFunc func = Enum.Parse<NFunc>(action.func);
                        NType type = NeuronDicts.TypesOfFuncs[func];
                        NeuronGeneData newNeuron = new();
                        newNeuron.type = type;
                        newNeuron.func = func;
                        newNeuron.ID = sim.genome.GetNextNeuronID();
                        newNeuron.data = GeneTools.GenerateData(func);

                        if (!NormalizeDirection(ref src, ref newNeuron))
                            break;

                        Gene newGene = GeneTools.CreateGene(src, newNeuron, 0, 
                            GeneTools.EncodeFloat(1,16,FType.SignedFloat));
                        newGene.graphID = sim.genome.GetNextGeneID();
                        sim.genome.genes.Add(newGene);
                        break;
                    }
                case "toggleSlot":
                    {
                        Gene? gene =sim.genome.genes.FirstOrDefault
                            (g => g.graphID == action.edgeID);
                        if (gene == null)
                            break;

                        gene.slot = (byte)(gene.slot == 0 ? 1 : 0);
                        break;
                    }
                case "changeWeight":
                    {
                        Gene? gene = sim.genome.genes.FirstOrDefault
                            (g => g.graphID == action.edgeID);
                        if (gene == null)
                            break;

                        float w = (gene.weight / 65535f) * 2f - 1f;
                        w += action.delta;
                        w = Math.Clamp(w, -1f, 1f);
                        gene.weight = (ushort)((w + 1f) * 0.5f * 65535f);

                        break;
                    }
                case "deleteEdge":
                    {
                        Gene? gene =
                            sim.genome.genes.FirstOrDefault
                            (g => g.graphID == action.edgeID);
                        if (gene != null)
                            sim.genome.genes.Remove(gene);

                        break;
                    }
            }
            try
            {
                File.WriteAllText(Config.EditorActionFile,"{}");
            }
            catch { }
        }
        public static bool NormalizeDirection(
            ref NeuronGeneData src,
            ref NeuronGeneData tgt)
        {
            if (tgt.type == NType.Sensor
            && src.type != NType.Sensor)
            {
                (src, tgt) = (tgt, src);
            }

            if (src.type == NType.Action
            && tgt.type != NType.Action)
            {
                (src, tgt) = (tgt, src);
            }

            if (src.type == NType.Action)
                return false;
            if (tgt.type == NType.Sensor)
                return false;

            return true;
        }

        public static void ReloadRuntime(ref DateTime lastReload)
        {
            if ((DateTime.Now - lastReload).TotalMilliseconds > 250)
            {
                Config.ReloadRuntime();
                lastReload = DateTime.Now;
            }
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
