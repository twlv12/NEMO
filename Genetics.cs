using System.Diagnostics;

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
            Genome mutatedGenome = genome;

            int numWeightPeturbs = rand.Next();

            return mutatedGenome;
        }

        public static Genome GenerateGenome(int length)
        {
            Genome genome = new Genome(new List<Gene>());
            List<(byte ID, NType type, NFunc func, ushort data)> existingNeurons = new();
            byte nextNeuronID = 0;

            for (int i = 0; i < length; i++)
            {
                Gene gene = new Gene();

                gene.srcType = (NType) rand.Next(0, 2); ;
                var srcFuncs = NeuronDicts.FuncsOfType[gene.srcType];
                gene.srcFunc = srcFuncs[rand.Next(srcFuncs.Count)];

                (gene.srcID, gene.srcData) = GetNeuronID(
                    existingNeurons, gene.srcType, gene.srcFunc, ref nextNeuronID);

                gene.tgtType = (NType) rand.Next(1, 3);
                var tgtFuncs = NeuronDicts.FuncsOfType[gene.tgtType];
                gene.tgtFunc = tgtFuncs[rand.Next(tgtFuncs.Count)];

                (gene.tgtID, gene.tgtData) = GetNeuronID(
                    existingNeurons, gene.tgtType, gene.tgtFunc, ref nextNeuronID);

                gene.slot = (byte) rand.Next(0, 2);
                gene.weight = (ushort) rand.Next(0, 65536);

                genome.genes.Add(gene);
            }

            return genome;
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
            var compatNeurons = existingNeurons;

            bool reuse =
                compatNeurons.Count > 0
                &&
                rand.NextSingle() <= Config.neuronReuseProb;

            if (reuse)
            {
                var neuron = compatNeurons[rand.Next(0, compatNeurons.Count)];
                return (neuron.ID, neuron.data);
            }

            ushort data = GenerateData(func);

            byte ID = nextNeuronID;
            nextNeuronID++;

            existingNeurons.Add((ID, type, func, data));
            return (ID, data);
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

        public static void RenderGraphViz(Genome genome)
        {
            List<string> lines = new();
            HashSet<string> emittedNodes = new();

            string BuildNodeLabel(NFunc func, ushort data)
            {
                string label = func.ToString();

                foreach (var field in
                    NeuronDicts.DataDefinitions[func])
                {
                    string val = DecodeFieldToString(data, field);

                    label +=
                        $"\\n{field.name}=" +
                        $"{val}";
                }

                return label;
            }
            void AddNode(string name, NType type, NFunc func, ushort data)
            {
                if (emittedNodes.Contains(name))
                    return;

                emittedNodes.Add(name);

                string fill =
                    type switch
                    {
                        NType.Sensor => "skyblue",
                        NType.Math => "palegreen",
                        NType.Action => "tomato",
                        _ => "white"
                    };

                string label = BuildNodeLabel(func, data);

                lines.Add(
                    $"\"{name}\" " +
                    $"[" +
                    $"label=\"{label}\", " +
                    $"style=filled, " +
                    $"fillcolor=\"{fill}\"" +
                    $"];"
                );
            }

            lines.Add("digraph G {");
            lines.Add("overlap=false;");
            lines.Add("splines=true;");
            lines.Add("node [fontsize=10];");
            lines.Add("edge [fontsize=8];");
            foreach (Gene gene in genome.genes)
            {
                string src =
                    $"{gene.srcFunc}_{gene.srcID}";
                string tgt =
                    $"{gene.tgtFunc}_{gene.tgtID}";

                AddNode(
                    src,
                    gene.srcType,
                    gene.srcFunc,
                    gene.srcData
                    );
                AddNode(
                    tgt,
                    gene.tgtType,
                    gene.tgtFunc,
                    gene.tgtData
                    );

                float weight = (gene.weight / 65535f) * 2f - 1f;
                string color = weight >= 0 ?"green" :"red";
                string style = gene.slot == 0 ?"solid" :"dashed";

                lines.Add(
                    $"\"{src}\" -> \"{tgt}\" " +
                    $"[" +
                    $"label=\"{Math.Round(weight, 1)}\", " +
                    $"penwidth=\"{1f + Math.Abs(weight) * 4f}\", " +
                    $"color=\"{color}\", " +
                    $"style=\"{style}\"" +
                    $"];"
                );
            }
            lines.Add("}");

            string fileName = "graph.dot";
            string filePath = @$"C:\Users\ethan\source\repos\NEMO\";
            File.WriteAllLines(filePath+fileName, lines);
            Console.WriteLine($"Wrote all genes to {filePath+fileName}.");

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = @"C:\Program Files (x86)\Graphviz\bin\dot.exe",
                Arguments = $"-Tsvg \"{filePath + fileName}\" -o \"{filePath}graph.svg\"",
                CreateNoWindow = true,
                UseShellExecute = false
            };
            Process process = Process.Start(startInfo);
            process.WaitForExit();
            Process.Start(new ProcessStartInfo
            {
                FileName = $"{filePath}graph.svg",
                UseShellExecute = true
            });
            Console.WriteLine($"Opened graph.svg");
        }
    }
}