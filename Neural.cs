using System.Text.Json;

namespace NEMO
{
    public class Connection
    {
        public Neuron src;
        public Neuron tgt;

        public byte slot;
        public float weight;

        public int graphID = -1;
        
        public Connection(Neuron src, Neuron tgt, 
            byte slot, float weight)
        {
            this.src = src;
            this.tgt = tgt;
            this.weight = weight;
            this.slot = slot;
        }
    }

    public class Neuron
    {
        public NType type;
        public NFunc func;
        public uint ID;

        public float value = 0f; //current committed output
        public float slotASum = 0f; //new values for input
        public float slotBSum = 0f;

        public NeuronGeneData geneData;
        public List<NeuronDataField> dataFields;
        public List<Connection> outgoingConnections;
        public List<Connection> incomingConnections;

        public List<float> lastValues = new() {0}; //Used for random
        public float lastValue = 0f; //Used for pulse

        public void RunFunction()
        {
            switch (func)
            {
                case NFunc.Constant:
                    value = dataFields[0].floatVal;
                    break;
                case NFunc.GetRandom:
                    lastValues.Add(NeuralTools.rand.NextSingle() * 2f - 1f);
                    if (lastValues.Count > dataFields[0].intVal+1)
                        lastValues.RemoveAt(0);
                    value = lastValues.Average();
                    break;
                case NFunc.Blockage:
                    //TODO
                    break;
                case NFunc.Gradient:
                    //TODO
                    break;
                case NFunc.MoveDelta:
                    //TODO
                    break;
                case NFunc.Density:
                    //TODO
                    break;
                case NFunc.GetSignal:
                    //TODO
                    break;
                case NFunc.GeneSimilarity:
                    //TODO
                    break;

                case NFunc.Relay:
                    value = NeuralTools.FastTanh
                        (slotASum + slotBSum + dataFields[0].floatVal);
                    break;
                case NFunc.Threshold:
                    value = NeuralTools.FastTanh
                        (0.5f + dataFields[2].floatVal * 10f *
                        ((slotASum + slotBSum) - dataFields[0].floatVal))
                        * (dataFields[1].boolVal == false ?1 :-1);
                    break;
                case NFunc.Multiply:
                    if (dataFields[0].boolVal)//if using grouped mode
                        value = NeuralTools.FastTanh(slotASum*slotBSum);
                    else{
                        value = 1f;
                        foreach(var inconn in incomingConnections){
                            value = NeuralTools.FastTanh(value * inconn.src.value);
                        }
                    }
                    break;
                case NFunc.Memory:
                    value = Math.Clamp
                        ((value * dataFields[0].floatVal)
                        +(slotASum+slotBSum)*(1-dataFields[0].floatVal), -1, 1);
                    break;
                case NFunc.Compare:
                    if (dataFields[0].boolVal)
                        value = NeuralTools.FastTanh
                            ((slotASum - slotBSum) * (0.5f+dataFields[1].floatVal*7f));
                    else
                        value = NeuralTools.FastTanh
                            ((slotBSum - slotASum) * (0.5f+dataFields[1].floatVal*7f));
                    break;
                case NFunc.Amplify:
                    value = NeuralTools.FastTanh((slotASum+slotBSum) * (1f+ dataFields[0].floatVal *4f));
                    break;
                case NFunc.Pulse:
                    if (Math.Abs((slotASum+slotBSum) - lastValue) > dataFields[0].floatVal)
                        value = dataFields[1].floatVal;
                    else
                        value = 0f;
                    lastValue = (slotASum+slotBSum);
                    break;

                case NFunc.MoveX:
                    value = slotASum + slotBSum;
                    break;
                case NFunc.MoveY:
                    value = slotASum + slotBSum;
                    break;
                case NFunc.Jitter:
                    value = slotASum + slotBSum;
                    break;
                case NFunc.EmitSignal:
                    value = slotASum + slotBSum;
                    break;
            }
            value = Math.Clamp(value, -1f, 1f);
        }
        public void AccumulateConnections()
        {
            foreach (Connection conn in incomingConnections){
                switch (conn.slot)
                {
                    case 0:
                        slotASum += (conn.src.value * conn.weight);
                        break;
                    case 1:
                        slotBSum += (conn.src.value * conn.weight);
                        break;
                }
            }
        }

        public Neuron(NType type, NFunc func, uint id,
            List<NeuronDataField> fields, NeuronGeneData geneData)
        {
            this.type = type;
            this.func = func;
            ID = id;
            dataFields = fields;
            outgoingConnections = new();
            incomingConnections = new();
            lastValues = new();
            this.geneData = geneData;
        }
    }

    public class Brain
    {
        public List<Neuron> neurons;
        public List<Connection> connections;
        public Brain(List<Neuron> neurons, List<Connection> connections){
            this.neurons = neurons;
            this.connections = connections;
        }

        public void UpdateAllNeurons()
        {
            foreach (Neuron n in neurons){
                n.slotASum = 0;
                n.slotBSum = 0;
                n.AccumulateConnections();
            }
            foreach (Neuron n in neurons){
                n.RunFunction();
                //if (n.value != 0)
                //{
                //    Console.WriteLine($"{n.func}_{n.ID} has value {n.value}");
                //}
            }
        }
    }

    public static class NeuralTools
    {
        public static Random rand = new Random();

        public static Brain GenomeToBrain(Genome genome)
        {
            Dictionary<uint, Neuron> neurons = new();
            List<Connection> connections = new();

            foreach (Gene gene in genome.genes)
            {
                Neuron src = GetOrCreateNeuron(neurons, gene.src);
                Neuron tgt = GetOrCreateNeuron(neurons, gene.tgt);
                Connection c = ConnectTwoNeurons(src, tgt, gene);
                c.graphID = connections.Count;
                connections.Add(c);
            }

            return new Brain(neurons.Values.ToList(), connections);
        }

        public static Neuron GetOrCreateNeuron
            (Dictionary<uint, Neuron> neurons,
            NeuronGeneData geneData)
        {
            if (neurons.TryGetValue(geneData.ID, out Neuron existing)){
                return existing;
            }

            var fields = NeuronDataToFields(geneData);
            Neuron neuron = new Neuron(geneData.type, geneData.func, geneData.ID, fields, geneData);
            neurons.Add(geneData.ID, neuron);

            return neuron;
        }
        public static List<NeuronDataField> NeuronDataToFields
            (NeuronGeneData neuronData)
        {
            List<NeuronDataField> datas = new();
            foreach (DataField field in 
                NeuronDicts.DataDefinitions[neuronData.func])
            {
                if (field.fieldType==FType.Float || field.fieldType==FType.SignedFloat){
                    float floatValue = GeneTools.DecodeField(neuronData.data, field);
                    NeuronDataField data = new(field.fieldType, floatVal: floatValue);
                    data.name = field.name;
                    datas.Add(data);
                }
                else if (field.fieldType==FType.Bool){
                    bool boolValue = GeneTools.DecodeField(neuronData.data, field) != 0;
                    NeuronDataField data = new(FType.Bool, boolVal: boolValue);
                    data.name = field.name;
                    datas.Add(data);
                }
                else{
                    int intValue = (int)GeneTools.DecodeField(neuronData.data, field);
                    NeuronDataField data = new(FType.Int, intVal: intValue);
                    data.name = field.name;
                    datas.Add(data);
                }
            }
            return datas;
        }
        public static Connection ConnectTwoNeurons
            (Neuron src, Neuron tgt, Gene gene)
        {
            Connection connection = new(src, tgt, gene.slot,
                (gene.weight / 65535f) * 2f - 1f);

            src.outgoingConnections.Add(connection);
            tgt.incomingConnections.Add(connection);

            return connection;
        }

        public static float FastTanh(float x){
            return x / (1f + MathF.Abs(x));
        }

        public static void RenderGraph(Brain brain, string graphID)
        {
            HashSet<string> emittedNodes = new();
            List<object> nodes = new();
            List<object> edges = new();

            string BuildNodeLabel(string name, Neuron neuron)
            {
                string label = name;
                label += $"\nSumA = {neuron.slotASum}";
                label += $"\nSumB = {neuron.slotBSum}";
                label += $"\nValue = {neuron.value}\n";
                foreach (var field in neuron.dataFields){
                    label += "\n"+field.ToString();
                }
                return label;
            }
            void AddNode(Neuron neuron)
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
                    neuronType = neuron.type.ToString(),
                    activation = neuron.value,
                    label = neuron.func.ToString(),
                    title = BuildNodeLabel(name, neuron),
                    incoming = neuron.incomingConnections.Count,
                    outgoing = neuron.outgoingConnections.Count,
                    color = color,
                    shape = "dot",
                    size = 25,
                    font = new
                    {
                        color = "white"
                    }
                });
            }

            foreach (Connection conn in brain.connections)
            {
                string srcName = $"{conn.src.func}_{conn.src.ID}";
                string tgtName = $"{conn.tgt.func}_{conn.tgt.ID}";

                AddNode(conn.src);
                AddNode(conn.tgt);

                string color = conn.weight >= 0 ? "green" : "red";
                bool dashed = conn.slot == 1;

                edges.Add(new
                {
                    id = conn.graphID,
                    signal = conn.src.value * conn.weight,
                    from = srcName,
                    to = tgtName,
                    color = color,
                    width = 1f + Math.Abs(conn.weight) * 4f,
                    dashes = dashed,
                    arrows = "to",
                    smooth = true
                });
            }

            var graph = new { nodes = nodes, edges = edges };
            string json = JsonSerializer.Serialize(graph,
                    new JsonSerializerOptions
                    {
                        WriteIndented = true
                    });

            string path = $"{Config.GraphOutputFolder}{graphID}_Brain.json";

            File.WriteAllText(path, json);
        }
    }
}
