using System.Diagnostics;
using System.Text.Json;

namespace NEMO
{
    public class Genome
    {
        public List<Gene> genes;

        public Genome(List<Gene> genes)
        {
            this.genes = genes;
        }

        public void PrintGenes()
        {
            foreach (var gene in genes)
            {
                Console.WriteLine(gene.ToString());
            }
        }
    }

    public class Gene
    {
        public NType srcType; // 2/8 bits
        public NFunc srcFunc; // 6/8 bits
        public byte srcID; // 8/8 bits
        public ushort srcData; //16 bits

        public NType tgtType;
        public NFunc tgtFunc;
        public byte tgtID;
        public ushort tgtData;

        public byte slot; // 2/8 bits
        public ushort weight; //16 bits

        public bool disabled; //1 bit

        public override string ToString()
        {
            string srcDatas = "";
            foreach (DataField dataField in NeuronDicts.DataDefinitions[srcFunc])
            {
                srcDatas += $"{dataField.name.Substring(0, 4)}=";
                srcDatas += Math.Round(GeneTools.DecodeField(srcData, dataField), 2);
                srcDatas += "; ";
            }
            string tgtDatas = "";
            foreach (DataField dataField in NeuronDicts.DataDefinitions[tgtFunc])
            {
                tgtDatas += $"{dataField.name.Substring(0, 4)}=";
                tgtDatas += Math.Round(GeneTools.DecodeField(tgtData, dataField), 2);
                tgtDatas += "; ";
            }

            string srcText = $"{srcType.ToString()}:{srcFunc.ToString()}:{srcID.ToString()}:({srcDatas})";
            string tgtText = $"{tgtType.ToString()}:{tgtFunc.ToString()}:{tgtID.ToString()}:({tgtDatas})";

            double decWeight = Math.Round((weight / 65535.0) * 2f - 1f, 2);

            return $"{srcText} ==({decWeight})==> {tgtText}";
        }
    }

    public class GeneTools
    {
        public static Random rand = new Random();

        public static List<GeneField> template = new List<GeneField>
        {
            new GeneField("srcType", 2),
            new GeneField("srcFunc", 6),
            new GeneField("srcData", 16),
            new GeneField("srcID", 8),

            new GeneField("tgtType", 2),
            new GeneField("tgtFunc", 6),
            new GeneField("tgtData", 16),
            new GeneField("tgtID", 8),

            new GeneField("slot", 1),
            new GeneField("weight", 16),
        };

        public static Genome MutateGenome(Genome genome)
        {
            List<Gene> genesToRemove = new();
            List<Gene> genesToAdd = new();

            List<(byte id, NType type, NFunc func, ushort data)> allNeurons = new();
            foreach (Gene gene in genome.genes)
            {
                if (!allNeurons.Contains((gene.srcID, gene.srcType, gene.srcFunc, gene.srcData)))
                    allNeurons.Add((gene.srcID, gene.srcType, gene.srcFunc, gene.srcData));

                if (!allNeurons.Contains((gene.tgtID, gene.tgtType, gene.tgtFunc, gene.tgtData)))
                    allNeurons.Add((gene.tgtID, gene.tgtType, gene.tgtFunc, gene.tgtData));
            }
            byte nextID = (byte) allNeurons.Count;

            //Gene Duplication
            if (rand.NextSingle() <= Config.geneDuplicationChance){
                if (genome.genes.Count < Config.maxGenes){
                    Gene chosenGene = genome.genes[rand.Next(0, genome.genes.Count)];
                    genome.genes.Add(chosenGene);
                }
            }

            foreach (Gene gene in genome.genes)
            {
                //Weight Flux
                float w = (gene.weight / 65535f) * 2f - 1f;
                w += Gaussian(Config.weightSharpness) * Config.weightFlux;
                gene.weight = (ushort)((w + 1f) * 0.5f * 65535f);

                //Data Flux Src
                gene.srcData = MutateData(gene.srcData, gene.srcFunc);
                //Data Flux Tgt
                gene.srcData = MutateData(gene.tgtData, gene.tgtFunc);

                //Slot Flip
                if (rand.NextSingle() <= Config.slotFlipChance){
                    gene.slot = (byte)(gene.slot == 0 ? 1 : 0);
                }

                //Weight Sign Flip
                if (rand.NextSingle() <= Config.wSignFlipChance){
                    gene.weight = (ushort)-gene.weight;
                }

                //RewireOne & RegenOne
                if (rand.NextSingle() <= Config.rewireOneChance)
                {
                    var newNeuron = allNeurons[rand.Next(0, allNeurons.Count)];
                    if (rand.NextSingle() > 0.5f) //Mutate Src
                    {
                        gene.srcType = newNeuron.type;
                        gene.srcFunc = newNeuron.func;
                        gene.srcID = newNeuron.id;
                        gene.srcData = newNeuron.data;
                    }
                    else
                    {
                        gene.tgtType = newNeuron.type;
                        gene.tgtFunc = newNeuron.func;
                        gene.tgtID = newNeuron.id;
                        gene.tgtData = newNeuron.data;
                    }
                }
                if (rand.NextSingle() <= Config.regenOneChance){
                    Gene newGene = GenerateGene(ref nextID, allNeurons);
                    if (rand.NextSingle() > 0.5f) //Mutate Src
                    {
                        gene.srcType = newGene.srcType;
                        gene.srcFunc = newGene.srcFunc;
                        gene.srcID = newGene.srcID;
                        gene.srcData = newGene.srcData;
                    }
                    else
                    {
                        gene.tgtType = newGene.tgtType;
                        gene.tgtFunc = newGene.tgtFunc;
                        gene.tgtID = newGene.tgtID;
                        gene.tgtData = newGene.tgtData;
                    }
                }

                //Toggle Active
                if (rand.NextSingle() <= Config.geneToggleChance){
                    gene.disabled = !gene.disabled;
                }

                //Gene Splitting
                if (rand.NextSingle() <= Config.geneSplitChance)
                {
                    (byte ID, NType type, NFunc func, ushort data) newNeuron = new();

                    newNeuron.type = (NType)1;
                    var funcs = NeuronDicts.FuncsOfType[newNeuron.type];
                    newNeuron.func = NFunc.Relay;
                    newNeuron.ID = nextID;
                    nextID++;
                    newNeuron.data = GenerateData(newNeuron.func);

                    Gene gene1 = new Gene();
                    gene1.srcType = gene.srcType;
                    gene1.srcFunc = gene.srcFunc;
                    gene1.srcID = gene.srcID;
                    gene1.srcData = gene.srcData;

                    gene1.tgtType = newNeuron.type;
                    gene1.tgtFunc = newNeuron.func;
                    gene1.tgtID = newNeuron.ID;
                    gene1.tgtData = newNeuron.data;

                    gene1.weight = 1;
                    gene1.slot = 0;

                    Gene gene2 = new Gene();
                    gene2.srcType = newNeuron.type;
                    gene2.srcFunc = newNeuron.func;
                    gene2.srcID = newNeuron.ID;
                    gene2.srcData = newNeuron.data;

                    gene2.tgtType = gene.tgtType;
                    gene2.tgtFunc = gene.tgtFunc;
                    gene2.tgtID = gene.tgtID;
                    gene2.tgtData = gene.tgtData;

                    gene2.weight = gene.weight;
                    gene2.slot = gene.slot;

                    genesToAdd.Add(gene1);
                    genesToAdd.Add(gene2);
                    genesToRemove.Add(gene);
                }

                //Neuron Replacement
                if (rand.NextSingle() <= Config.neuronReplaceChance)
                {
                    bool replacingSource;
                    (byte ID, NType type, NFunc func, ushort data) oldNeuron = new();
                    if (rand.NextSingle() <= 0.5f)
                    {
                        oldNeuron.type = gene.srcType;
                        oldNeuron.func = gene.srcFunc;
                        oldNeuron.ID = gene.srcID;
                        oldNeuron.data = gene.srcData;
                        replacingSource = true;
                    }
                    else
                    {
                        oldNeuron.type = gene.tgtType;
                        oldNeuron.func = gene.tgtFunc;
                        oldNeuron.ID = gene.tgtID;
                        oldNeuron.data = gene.tgtData;
                        replacingSource = false;
                    }

                    (byte ID, NType type, NFunc func, ushort data) newNeuron = new();
                    newNeuron.ID = oldNeuron.ID;

                    if (rand.NextSingle() <= Config.sameTypeChance){
                        newNeuron.type = oldNeuron.type;
                    }
                    else{ 
                        newNeuron.type = NType.Math; 
                    }

                    var funcs = NeuronDicts.FuncsOfType[newNeuron.type];
                    newNeuron.func = funcs[rand.Next(0, funcs.Count)];
                    newNeuron.data = GenerateData(newNeuron.func);

                    if (replacingSource)
                    {
                        gene.srcType = newNeuron.type;
                        gene.srcFunc = newNeuron.func;
                        gene.srcID = newNeuron.ID;
                        gene.srcData = newNeuron.data;
                    }
                    else
                    {
                        gene.tgtType = newNeuron.type;
                        gene.tgtFunc = newNeuron.func;
                        gene.tgtID = newNeuron.ID;
                        gene.tgtData = newNeuron.data;
                    }

                    foreach (Gene gene3 in genome.genes)
                    {
                        if (gene3.srcID == oldNeuron.ID)
                        {
                            gene3.srcType = newNeuron.type;
                            gene3.srcFunc = newNeuron.func;
                            gene3.srcID = newNeuron.ID;
                            gene3.srcData = newNeuron.data;
                        }
                        if (gene3.tgtID == oldNeuron.ID)
                        {
                            gene3.tgtType = newNeuron.type;
                            gene3.tgtFunc = newNeuron.func;
                            gene3.tgtID = newNeuron.ID;
                            gene3.tgtData = newNeuron.data;
                        }
                    }
                }
            }

            //Gene Insertion & Removal
            if (rand.NextSingle() <= Config.geneInsertionChance){
                if (genome.genes.Count < Config.maxGenes){
                    genome.genes.Add(GenerateGene(ref nextID, allNeurons));
                }
            }
            if (rand.NextSingle() <= Config.geneRemovalChance){
                if (genome.genes.Count > Config.minGenes){
                    genome.genes.RemoveAt(rand.Next(0,genome.genes.Count));
                }
            }

            foreach (Gene gene in genesToRemove){
                genome.genes.Remove(gene);
            }
            foreach (Gene gene in genesToAdd){
                genome.genes.Add(gene);
            }
            return genome;
        }
        public static ushort MutateData(ushort data, NFunc func)
        {
            foreach (DataField field in NeuronDicts.DataDefinitions[func])
            {
                if (field.isSignedFloat)
                {
                    float value = DecodeField(data, field);
                    value += Gaussian(Config.floatDataSharpness) * Config.floatDataFlux * field.mutateSensitivity;
                    value = Math.Clamp(value, -1f, 1f);
                    ushort encoded = (ushort)(((value + 1f) * 0.5f)
                                     *
                                     ((1 << field.bitLength) - 1));
                    data = SetField(data, field, encoded);
                }
                else if (field.isFloat)
                {
                    float value = DecodeField(data, field);
                    value += Gaussian(Config.floatDataSharpness) * Config.floatDataFlux * 0.5f *field.mutateSensitivity;
                    value = Math.Clamp(value, 0f, 1f);
                    ushort encoded = (ushort)(value
                                     *
                                     ((1 << field.bitLength) - 1));
                    data = SetField(data, field, encoded);
                }
                else if (field.isBool)
                {
                    if (rand.NextSingle() < Config.boolFlipChance)
                    {
                        ushort raw = ExtractField(data, field);
                        raw = (ushort)(raw == 0 ? 1 : 0);
                        data = SetField(data, field, raw);
                    }
                }
                else
                {
                    if (rand.NextSingle() < Config.intRandChance)
                    {
                        ushort value;
                        if (field.maxValue.HasValue)
                        {
                            value = (ushort)rand.Next(0, field.maxValue.Value + 1);
                        }
                        else
                        {
                            value = (ushort)rand.Next(0, 1 << field.bitLength);
                        }

                        data = SetField(data, field, value);
                    }
                }
            }
            return data;
        }

        public static Genome GenerateGenome(int length)
        {
            Genome genome = new Genome(new List<Gene>());
            List<(byte ID, NType type, NFunc func, ushort data)> existingNeurons = new();
            byte nextNeuronID = 0;

            for (int i = 0; i < length; i++)
            {
                genome.genes.Add(GenerateGene(ref nextNeuronID, existingNeurons));
            }

            return genome;
        }
        public static Gene GenerateGene(ref byte nextNeuronID, List<(byte ID, NType type, NFunc func, ushort data)>  existingNeurons)
        {
            Gene gene = new Gene();

            gene.srcType = (NType)rand.Next(0, 2); ;
            var srcFuncs = NeuronDicts.FuncsOfType[gene.srcType];
            gene.srcFunc = srcFuncs[rand.Next(srcFuncs.Count)];

            (gene.srcID, gene.srcData) = GetNeuronID(
                existingNeurons, gene.srcType, gene.srcFunc, ref nextNeuronID);

            gene.tgtType = (NType)rand.Next(1, 3);
            var tgtFuncs = NeuronDicts.FuncsOfType[gene.tgtType];
            gene.tgtFunc = tgtFuncs[rand.Next(tgtFuncs.Count)];

            (gene.tgtID, gene.tgtData) = GetNeuronID(
                existingNeurons, gene.tgtType, gene.tgtFunc, ref nextNeuronID);

            gene.slot = (byte)rand.Next(0, 2);
            gene.weight = (ushort)rand.Next(0, 65536);
            gene.disabled = false;

            return gene;
        }

        public static ushort GenerateData(NFunc func)
        {
            ushort fullDataField = 0;

            foreach (DataField field in NeuronDicts.DataDefinitions[func])
            {
                ushort dataField;

                if (field.maxValue.HasValue) {
                    dataField = (ushort) rand.Next(0, field.maxValue.Value +1);
                }
                else{
                    dataField = (ushort) rand.Next(0, 1<<field.bitLength);
                }

                ushort shiftedDataField = (ushort)(dataField << field.startBit);

                fullDataField |= shiftedDataField;
            }

            return fullDataField;
        }
        public static (byte ID, ushort data) GetNeuronID(
        List<(byte ID, NType type, NFunc func, ushort data)> existingNeurons,
        NType type, NFunc func, ref byte nextNeuronID)
        {
            bool reuse =
                existingNeurons.Count > 0
                &&
                rand.NextSingle() <= Config.neuronReuse;

            if (reuse)
            {
                var neuron = existingNeurons[rand.Next(0, existingNeurons.Count)];
                return (neuron.ID, neuron.data);
            }

            ushort data = GenerateData(func);

            byte ID = nextNeuronID;
            nextNeuronID++;

            existingNeurons.Add((ID, type, func, data));
            return (ID, data);
        }

        public static ushort SetField(ushort data, DataField field, ushort newData)
        {
            ushort mask = (ushort) 
                (((1 << field.bitLength) - 1) 
                << field.startBit);
            data = (ushort)(data & ~mask); //Now region to overwrite is cleared

            data |= (ushort)(newData << field.startBit);

            return data;
        }
        public static ushort ExtractField(ushort data, DataField field)
        {
            ushort mask = (ushort) ((1 << field.bitLength) -1);
            return (ushort) ((data >> field.startBit) & mask);
        }
        public static float DecodeField(ushort data, DataField field)
        {
            float rawVal = ExtractField(data, field);

            if (field.isSignedFloat) {
                return rawVal / ((1 << field.bitLength) - 1f) * 2f - 1f;
            }
            if (field.isFloat) {
                return rawVal / ((1 << field.bitLength) - 1f);
            }
            return rawVal;
        }
        public static string DecodeFieldToString(ushort data, DataField field)
        {
            float rawVal = ExtractField(data, field);

            if (field.isSignedFloat) {
                return Math.Round((rawVal / ((1 << field.bitLength) - 1f) * 2f - 1f), 2).ToString();
            }
            if (field.isFloat) {
                return Math.Round((rawVal / ((1 << field.bitLength) - 1f)), 2).ToString();
            }
            if (field.isBool) {
                return rawVal == 0 ?"X|True" :"Y|False";
            }

            return Math.Round(rawVal,1).ToString();
        }

        public static void RenderGraph(Genome genome)
        {
            HashSet<string> emittedNodes = new();
            List<object> nodes = new();
            List<object> edges = new();

            string BuildNodeLabel(NFunc func, ushort data)
            {
                string label = func.ToString();
                foreach (var field in NeuronDicts.DataDefinitions[func]){
                    string val = DecodeFieldToString(data, field);
                    label += $"\n{field.name}={val}";
                }
                return label;
            }
            void AddNode(string name, NType type, NFunc func, ushort data)
            {
                if (emittedNodes.Contains(name))
                    return;

                emittedNodes.Add(name);
                string color = type switch
                    {
                        NType.Sensor =>
                            "skyblue",
                        NType.Math =>
                            "palegreen",
                        NType.Action =>
                            "tomato",
                        _ =>
                            "white"
                    };
                nodes.Add(new
                {
                    id = name,
                    label = BuildNodeLabel(func, data),
                    color = color,
                    shape = "dot",
                    size = 25,
                    font = new
                    {
                        color = "white"
                    }
                });
            }

            foreach (Gene gene in genome.genes)
            {
                string src = $"{gene.srcFunc}_{gene.srcID}";
                string tgt = $"{gene.tgtFunc}_{gene.tgtID}";

                AddNode(
                    src,
                    gene.srcType,
                    gene.srcFunc,
                    gene.srcData); //src
                AddNode(
                    tgt,
                    gene.tgtType,
                    gene.tgtFunc,
                    gene.tgtData); //tgt

                float weight = (gene.weight / 65535f) * 2f - 1f;
                string color = weight >= 0 ?"green" :"red";
                bool dashed = gene.slot == 1;

                edges.Add(new
                {
                    from = src,
                    to = tgt,
                    color = color,
                    width = 1f + Math.Abs(weight) * 4f,
                    dashes = dashed,
                    arrows = "to",
                    smooth = true
                });
            }

            var graph = new{nodes = nodes, edges = edges};
            string json =JsonSerializer.Serialize(graph,
                    new JsonSerializerOptions
                    {
                        WriteIndented = true
                    });

            string path = @"C:\Users\ethan\source\repos\NEMO\GenomeViewer\graph.json";

            File.WriteAllText(path, json);
            Console.WriteLine($"Wrote graph to {path}");
        }

        public static float Gaussian(float sharpness = 1f)
        {
            float x = 1f - rand.NextSingle();

            float y = 1f - rand.NextSingle();

            float normal =
                MathF.Sqrt(-2f * MathF.Log(x))
                *
                MathF.Cos(2f * MathF.PI * y);

            return normal / sharpness;
        }
    }
}