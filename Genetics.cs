using Microsoft.VisualBasic.FileIO;
using System.Diagnostics;
using System.Text.Json;

namespace NEMO
{
    public class Genome
    {
        public List<Gene> genes;
        public int nextGeneID = 0;

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

        public uint GetNextNeuronID()
        {
            uint max = 0;
            foreach (var gene in genes)
            {
                if (gene.src.ID > max)
                    max = gene.src.ID;
                if (gene.tgt.ID > max)
                    max = gene.tgt.ID;
            }
            return max + 1;
        }
        public int GetNextGeneID()
        {
            int max = 0;

            foreach (var gene in genes)
            {
                if (gene.graphID > max)
                    max = gene.graphID;
            }

            return max + 1;
        }
    }

    public class Gene
    {
        public NeuronGeneData src;
        public NeuronGeneData tgt;

        public byte slot; // 2/8 bits
        public ushort weight; //16 bits

        public bool disabled; //1 bit
        public int graphID = -1;

        public override string ToString()
        {
            string srcDatas = "";
            foreach (DataField dataField in NeuronDicts.DataDefinitions[src.func])
            {
                srcDatas += $"{dataField.name.Substring(0, 4)}=";
                srcDatas += Math.Round(GeneTools.DecodeField(src.data, dataField), 2);
                srcDatas += "; ";
            }
            string tgtDatas = "";
            foreach (DataField dataField in NeuronDicts.DataDefinitions[tgt.func])
            {
                tgtDatas += $"{dataField.name.Substring(0, 4)}=";
                tgtDatas += Math.Round(GeneTools.DecodeField(tgt.data, dataField), 2);
                tgtDatas += "; ";
            }

            string srcText = $"{src.type.ToString()}:{src.func.ToString()}:{src.ID.ToString()}:({srcDatas})";
            string tgtText = $"{tgt.type.ToString()}:{tgt.func.ToString()}:{tgt.ID.ToString()}:({tgtDatas})";

            double decWeight = Math.Round((weight / 65535.0) * 2f - 1f, 2);

            return $"{graphID}:::{srcText} ==({decWeight})==> {tgtText}";
        }
    }

    public class NeuronGeneData
    {
        public NType type;
        public NFunc func;
        public uint ID;
        public ushort data;
    }

    public static class GeneTools
    {
        public static Random rand = new Random();

        public static Genome MutateGenome(Genome genome)
        {
            PrintMut($"Mutating Genome...");

            List<Gene> genesToRemove = new();
            List<Gene> genesToAdd = new();

            HashSet<NeuronGeneData> mutatedNeurons = new();
            List <NeuronGeneData> allNeurons = new();   
            foreach (Gene gene in genome.genes)
            {
                if (!allNeurons.Contains(gene.src))
                    allNeurons.Add(gene.src);

                if (!allNeurons.Contains(gene.tgt))
                    allNeurons.Add(gene.tgt);
            }
            uint nextID = (allNeurons.Max(n => n.ID) + 1);

            float scale = Config.globalNewGeneRate / (float)genome.genes.Count;

            foreach (Gene gene in genome.genes)
            {
                //Weight Flux
                float w = (gene.weight / 65535f) * 2f - 1f;
                w += Gaussian(Config.weightSharpness) * Config.weightFlux *Config.globalMutationRate;
                gene.weight = (ushort)((w + 1f) * 0.5f * 65535f);

                //Data Flux Src
                if (!mutatedNeurons.Contains(gene.src))
                {
                    gene.src.data = MutateData(gene.src.data, gene.src.func);
                    mutatedNeurons.Add(gene.src);
                }
                //Data Flux Tgt
                if (!mutatedNeurons.Contains(gene.tgt))
                {
                    gene.tgt.data = MutateData(gene.tgt.data, gene.tgt.func);
                    mutatedNeurons.Add(gene.tgt);
                }

                //Slot Flip
                if (rand.NextSingle() <= 
                    Config.slotFlipChance * Config.globalMutationRate*Config.topologyMutationRate)
                {
                    gene.slot = (byte)(gene.slot == 0 ? 1 : 0);
                    PrintMut($"{gene.graphID}:::Slot Flipped");
                }

                //Weight Sign Flip
                if (rand.NextSingle() <= 
                    Config.wSignFlipChance * Config.globalMutationRate * Config.topologyMutationRate)
                {
                    float w2 = (gene.weight / 65535f) * 2f - 1f;
                    w2 = -w2;
                    gene.weight = (ushort)((w2 + 1f) * 0.5f * 65535f);
                    PrintMut($"{gene.graphID}:::W Sign Flipped");
                }

                //RewireOne & RegenOne
                if (rand.NextSingle() <= 
                    Config.rewireOneChance * Config.globalMutationRate*Config.topologyMutationRate)
                {
                    if (rand.NextSingle() > 0.5f){ //Mutate Src
                        var compatible = allNeurons.Where(
                            n => n.type == NType.Sensor
                               || n.type == NType.Math).ToList();
                        var newNeuron = compatible[rand.Next(0, compatible.Count)];
                        gene.src = newNeuron;

                        PrintMut($"{gene.graphID}:::Rewired SRC");
                    }
                    else{
                        var compatible = allNeurons.Where(
                            n => n.type == NType.Action
                               || n.type == NType.Math).ToList();
                        var newNeuron = compatible[rand.Next(0, compatible.Count)];
                        gene.tgt = newNeuron;

                        PrintMut($"{gene.graphID}:::Rewired TGT");
                    }
                }
                if (rand.NextSingle() <= 
                    Config.regenOneChance*Config.globalMutationRate*Config.topologyMutationRate)
                {
                    NeuronGeneData newN = new();
                    bool mutateSource = rand.NextSingle() > 0.5f;

                    if (mutateSource){
                        newN.type = ChooseNeuronType(genome, true, false);
                    }
                    else{
                        newN.type = ChooseNeuronType(genome, false, true);
                    }

                    newN = RandNeuronOfType(newN.type, ref nextID);
                    allNeurons.Add(newN);

                    if (mutateSource){
                        gene.src = newN;
                        PrintMut($"{gene.graphID}:::Regenerated SRC");
                    }
                    else{
                        gene.tgt = newN;
                        PrintMut($"{gene.graphID}:::Regenerated TGT");
                    }
                }

                //Toggle Active
                if (rand.NextSingle() <= 
                    Config.geneToggleChance*Config.globalMutationRate*Config.topologyMutationRate){
                    gene.disabled = !gene.disabled;
                    PrintMut($"{gene.graphID}:::Toggled Active");
                }

                //Gene Splitting
                if (rand.NextSingle() <= 
                    Config.geneSplitChance*scale*Config.globalMutationRate*Config.topologyMutationRate)
                {
                    NeuronGeneData newNeuron = new();

                    newNeuron.type = (NType)1;
                    var funcs = NeuronDicts.FuncsOfType[newNeuron.type];
                    newNeuron.func = NFunc.Relay;
                    newNeuron.ID = nextID;
                    nextID++;
                    newNeuron.data = GenerateData(newNeuron.func);
                    allNeurons.Add(newNeuron);

                    Gene gene1 = new Gene();
                    gene1.src = gene.src;
                    gene1.tgt = newNeuron;
                    gene1.weight = 1;
                    gene1.slot = 0;

                    Gene gene2 = new Gene();
                    gene2.src = newNeuron;
                    gene2.tgt = gene.tgt;
                    gene2.weight = gene.weight;
                    gene2.slot = gene.slot;

                    genesToAdd.Add(gene1);
                    genesToAdd.Add(gene2);
                    genesToRemove.Add(gene);

                    PrintMut($"{gene.graphID}:::Split to {gene1.graphID}+{gene2.graphID}");
                }

                //Neuron Replacement
                if (rand.NextSingle() <= 
                    Config.neuronReplaceChance*Config.globalMutationRate*Config.topologyMutationRate)
                {
                    bool replacingSource;
                    NeuronGeneData oldNeuron = new();
                    if (rand.NextSingle() <= 0.5f)
                    {
                        oldNeuron = gene.src;
                        replacingSource = true;
                    }
                    else
                    {
                        oldNeuron = gene.tgt;
                        replacingSource = false;
                    }

                    NeuronGeneData newNeuron = new();
                    newNeuron.ID = oldNeuron.ID;

                    if (rand.NextSingle() <= Config.sameTypeChance){
                        newNeuron.type = oldNeuron.type;
                    }
                    else{
                        bool validAsSource = false;
                        bool validAsTarget = false;
                        foreach (Gene gene4 in genome.genes)
                        {
                            if (gene4.src == oldNeuron)
                                validAsSource = true;
                            if (gene4.tgt == oldNeuron)
                                validAsTarget = true;
                        }
                        newNeuron.type = ChooseNeuronType(genome, validAsSource, validAsTarget);
                    }

                    newNeuron = RandNeuronOfType(newNeuron.type, ref nextID);

                    if (replacingSource){
                        gene.src = newNeuron;
                    }
                    else{
                        gene.tgt = newNeuron;
                    }

                    foreach (Gene gene3 in genome.genes)
                    {
                        if (gene3.src == oldNeuron){
                            gene3.src = newNeuron;
                        }
                        if (gene3.tgt == oldNeuron){
                            gene3.tgt = newNeuron;
                        }
                    }

                    allNeurons.Remove(oldNeuron);
                    allNeurons.Add(newNeuron);
                    PrintMut($"{gene.graphID}:::Replaced {oldNeuron.func.ToString()} -> {newNeuron.func.ToString()}");
                }

                //Gene Duplication
                if (rand.NextSingle() <= 
                    Config.geneDuplicationChance*scale*Config.globalMutationRate*Config.topologyMutationRate)
                {
                    if (genome.genes.Count < Config.maxGenes)
                    {
                        Gene chosenGene = genome.genes[rand.Next(0, genome.genes.Count)];
                        Gene clone = new();
                        clone.src = chosenGene.src;
                        clone.tgt = chosenGene.tgt;
                        clone.weight = chosenGene.weight;
                        clone.slot = chosenGene.slot;
                        clone.disabled = chosenGene.disabled;
                        clone.graphID = genome.nextGeneID;
                        genome.nextGeneID++;
                        genesToAdd.Add(clone);
                    }

                    PrintMut($"{gene.graphID}:::Duplicated");
                }

                //Gene Insertion & Removal
                if (rand.NextSingle() <= 
                    Config.geneInsertionChance*scale*Config.globalMutationRate*Config.topologyMutationRate)
                {
                    if (genome.genes.Count < Config.maxGenes)
                    {
                        Gene newGene = GenerateGene(ref nextID, allNeurons);
                        newGene.graphID = genome.nextGeneID;
                        genome.nextGeneID++;
                        genesToAdd.Add(newGene);
                        PrintMut($"{newGene.graphID}:::<- Inserted New Gene");
                    }
                }
                if (rand.NextSingle() <= 
                    Config.geneRemovalChance *scale*Config.globalMutationRate*Config.topologyMutationRate)
                {
                    if (genome.genes.Count > Config.minGenes)
                    {
                        Gene geneToRemove = genome.genes[rand.Next(0, genome.genes.Count)];
                        genesToRemove.Add(geneToRemove);
                        PrintMut($"{geneToRemove.graphID}:::<- Removed");
                    }
                }
            }

            foreach (Gene gene in genesToRemove){
                if (genome.genes.Count > Config.minGenes){
                    genome.genes.Remove(gene);
                }
            }
            foreach (Gene gene in genesToAdd){
                if (genome.genes.Count < Config.maxGenes){
                    gene.graphID = genome.nextGeneID;
                    genome.nextGeneID++;
                    genome.genes.Add(gene);
                }
            }
            return genome;
        }
        public static ushort MutateData(ushort data, NFunc func)
        {
            foreach (DataField field in NeuronDicts.DataDefinitions[func])
            {
                if (field.fieldType==FType.SignedFloat)
                {
                    float value = DecodeField(data, field);
                    value += Gaussian(Config.floatDataSharpness) * Config.floatDataFlux * field.mutateSensitivity * Config.globalMutationRate;
                    value = Math.Clamp(value, -1f, 1f);
                    ushort encoded = (ushort)(((value + 1f) * 0.5f)
                                     *
                                     ((1 << field.bitLength) - 1));
                    data = SetField(data, field, encoded);
                }
                else if (field.fieldType==FType.Float)
                {
                    float value = DecodeField(data, field);
                    value += Gaussian(Config.floatDataSharpness) * Config.floatDataFlux * 0.5f *field.mutateSensitivity * Config.globalMutationRate;
                    value = Math.Clamp(value, 0f, 1f);
                    ushort encoded = (ushort)(value
                                     *
                                     ((1 << field.bitLength) - 1));
                    data = SetField(data, field, encoded);
                }
                else if (field.fieldType==FType.Bool)
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

        public static NType ChooseNeuronType(Genome genome, 
            bool validAsSrc, bool validAsTgt)
        {
            float sWeight = Config.baseSensorWeight;
            float aWeight = Config.baseActionWeight;

            if (validAsSrc && validAsTgt)
                return NType.Math;
            if (!validAsSrc)
                sWeight = 0;
            if (!validAsTgt)
                aWeight = 0;

            HashSet<NeuronGeneData> mathNeurons = new();
            HashSet<NeuronGeneData> allNeurons = new();
            foreach (Gene gene in genome.genes)
            {
                allNeurons.Add(gene.src);
                allNeurons.Add(gene.tgt);
                if (gene.src.type == NType.Math){
                    mathNeurons.Add(gene.src);}
                if (gene.tgt.type == NType.Math){
                    mathNeurons.Add(gene.tgt);}
            }

            float mathFraction = (float)mathNeurons.Count / (float)allNeurons.Count;
            float mWeight = (float) Math.Pow(1 - mathFraction, Config.mathSuppressionExponent);

            mWeight *= Config.mathWeightMultiplier;

            float total = sWeight + aWeight + mWeight;
            float r = rand.NextSingle() * total;

            if (r < sWeight){
                return NType.Sensor;
            }
            r -= sWeight;
            if (r < mWeight){
                return NType.Math;
            }
            return NType.Action;
        }
        public static NeuronGeneData RandNeuronOfType(NType type, ref uint nextID)
        {
            NeuronGeneData newNeuron = new NeuronGeneData();
            var funcs = NeuronDicts.FuncsOfType[type];
            newNeuron.type = type;
            newNeuron.func = funcs[rand.Next(0, funcs.Count)];
            newNeuron.data = GenerateData(newNeuron.func);
            newNeuron.ID = nextID;
            nextID++;
            return newNeuron;
        }

        public static Genome GenerateGenome()
        {
            int length = Config.baseGenes;
            Genome genome = new Genome(new List<Gene>());
            List<NeuronGeneData> existingNeurons = new();
            uint nextNeuronID = 0;

            for (int i = 0; i < length; i++)
            {
                Gene newGene = GenerateGene(ref nextNeuronID, existingNeurons);
                newGene.graphID = genome.nextGeneID;
                genome.nextGeneID++;
                genome.genes.Add(newGene);
            }

            return genome;
        }
        public static Gene GenerateGene(ref uint nextNeuronID, 
            List<NeuronGeneData>  existingNeurons)
        {
            Gene gene = new Gene();

            gene.src = GetOrCreateNeuron(
                ref nextNeuronID, existingNeurons, true);
            gene.tgt = GetOrCreateNeuron(
                ref nextNeuronID, existingNeurons, false);

            gene.slot = (byte)rand.Next(0, 2);
            gene.weight = (ushort)rand.Next(0, 65536);
            gene.disabled = false;

            return gene;
        }
        public static Gene CreateGene(NeuronGeneData src,NeuronGeneData tgt,
            byte slot,ushort weight)
        {
            return new Gene
            {
                src = src,
                tgt = tgt,
                slot = slot,
                weight = weight,
                disabled = false
            };
        }
        public static Genome GenerateSimpleGenome()
        {
            NeuronGeneData constant = new()
            {
                type = NType.Sensor,
                func = NFunc.Constant,
                ID = 0,
                data = GenerateData(NFunc.Constant)
            };
            NeuronGeneData relay = new()
            {
                type = NType.Math,
                func = NFunc.Relay,
                ID = 1,
                data = GenerateData(NFunc.Relay)
            };
            Gene gene = new Gene
            {
                src = constant,
                tgt = relay,
                slot = 0,
                weight = EncodeFloat(1,16,FType.SignedFloat)
            };
            List<Gene> genes = new List<Gene> { gene };
            Genome genome = new Genome(genes);

            return genome;
        }

        public static NeuronGeneData GetOrCreateNeuron(ref uint nextNeuronID, 
            List<NeuronGeneData> existingNeurons, bool isSource)
        {
            bool reuse =
                existingNeurons.Count > 0
                &&
                rand.NextSingle() <= Config.neuronReuse;

            if (reuse)
            {
                var compatible = existingNeurons.Where(
                    n => 
                    isSource ?n.type != NType.Action :n.type != NType.Sensor
                    ).ToList();

                if (compatible.Any()){
                    return compatible[rand.Next(compatible.Count)];
                }
            }

            NeuronGeneData neuron = new();

            if (isSource){
                neuron.type = (NType)rand.Next(0, 2); }
            else{
                neuron.type = (NType)rand.Next(1, 3); }

            var funcs = NeuronDicts.FuncsOfType[neuron.type];
            neuron.func = funcs[rand.Next(0, funcs.Count)];

            neuron.ID = nextNeuronID;
            nextNeuronID++;

            neuron.data = GenerateData(neuron.func);
            
            existingNeurons.Add(neuron);
            return neuron;

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

            if (field.fieldType==FType.SignedFloat) {
                return rawVal / ((1 << field.bitLength) - 1f) * 2f - 1f;
            }
            if (field.fieldType == FType.Float) {
                return rawVal / ((1 << field.bitLength) - 1f);
            }
            return rawVal;
        }
        public static string DecodeFieldToString(ushort data, DataField field)
        {
            float rawVal = ExtractField(data, field);

            if (field.fieldType==FType.SignedFloat) {
                return Math.Round((rawVal / ((1 << field.bitLength) - 1f) * 2f - 1f), 2).ToString();
            }
            if (field.fieldType == FType.Float) {
                return Math.Round((rawVal / ((1 << field.bitLength) - 1f)), 2).ToString();
            }
            if (field.fieldType == FType.Bool) {
                return rawVal == 1 ?"X|True" :"Y|False";
            }

            return Math.Round(rawVal,1).ToString();
        }
        public static ushort EncodeFloat(float value, int bits, FType type)
        {
            int max = (1 << bits) - 1;
            value = type switch
            {
                FType.Float => Math.Clamp(value, 0f, 1f),
                FType.SignedFloat => Math.Clamp(value, -1f, 1f),
                _ => value
            };
            float normalized = type == FType.SignedFloat
                ? (value + 1f) * 0.5f
                : value;
            return (ushort)(normalized * max);
        }
        public static ushort EncodeFields(NFunc func, List<NeuronDataField> fields)
        {
            ushort data = 0;
            List<DataField> defs = NeuronDicts.DataDefinitions[func];
            for (int i = 0; i < defs.Count; i++)
            {
                DataField def = defs[i];
                NeuronDataField field = fields[i];
                ushort encoded = 0;

                switch (field.type)
                {
                    case FType.Bool:
                        encoded = (ushort)(field.boolVal ?1 :0);
                        break;
                    case FType.Int:
                        encoded = (ushort)field.intVal;
                        break;
                    case FType.Float:
                    case FType.SignedFloat:
                        encoded =EncodeFloat(
                                field.floatVal,
                                def.bitLength,
                                field.type);
                        break;
                }

                data = SetField(data,def,encoded);
            }
            return data;
        }

        public static void RenderGraph(Genome genome, string graphID)
        {
            HashSet<string> emittedNodes = new();
            List<object> nodes = new();
            List<object> edges = new();

            string BuildNodeLabel(string name, NeuronGeneData neuron)
            {
                string label = name;
                foreach (var field in NeuronDicts.DataDefinitions[neuron.func]){
                    string val = DecodeFieldToString(neuron.data, field);
                    label += $"\n{field.name}={val}";
                }
                return label;
            }
            void AddNode(NeuronGeneData neuron)
            {
                string name = $"{neuron.func}_{neuron.ID}";
                if (emittedNodes.Contains(name))
                    return;

                emittedNodes.Add(name);
                string color = neuron.type switch
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
                    label = neuron.func.ToString(),
                    title = BuildNodeLabel(name, neuron),
                    fields = ExportFields(neuron),
                    color = color,
                    shape = "dot",
                    size = 25,
                    font = new
                    {
                        color = "white"
                    }
                });
            }
            List<DataFieldLive> ExportFields(NeuronGeneData neuron)
            {
                List<DataFieldLive> fields = new();
                foreach (var fieldDef in NeuronDicts.DataDefinitions[neuron.func])
                {
                    DataFieldLive export = new();
                    export.name = fieldDef.name;
                    export.type = fieldDef.fieldType.ToString();

                    float decoded = DecodeField(neuron.data, fieldDef);

                    switch (fieldDef.fieldType)
                    {
                        case FType.Float:
                        case FType.SignedFloat:
                            export.floatVal = decoded;
                            break;
                        case FType.Int:
                            export.intVal = (int)decoded;
                            break;
                        case FType.Bool:
                            export.boolVal = decoded > 0.5f;
                            break;
                    }

                    fields.Add(export);
                }

                return fields;
            }

            foreach (Gene gene in genome.genes)
            {
                string srcName = $"{gene.src.func}_{gene.src.ID}";
                string tgtName = $"{gene.tgt.func}_{gene.tgt.ID}";

                AddNode(gene.src); //src
                AddNode(gene.tgt); //tgt

                float weight = (gene.weight / 65535f) * 2f - 1f;
                string color = weight >= 0 ?"green" :"red";
                bool dashed = gene.slot == 1;

                edges.Add(new
                {
                    id = gene.graphID,
                    from = srcName,
                    to = tgtName,
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

            string path = $"{Config.GraphOutputFolder}{graphID}.json";

            File.WriteAllText(path, json);
            //Console.WriteLine($"{id}:::Wrote graph to {path}");
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

        public static void PrintMut(string text){
            if (Config.printMutations){
                Console.WriteLine(text);
            }
        }
    }
}