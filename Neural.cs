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

        public Creature host;

        public float value = 0f; //current committed output
        public float slotASum = 0f; //new values for input
        public float slotBSum = 0f;

        public NeuronGeneData geneData;
        public List<NeuronDataField> dataFields;
        public List<Connection> outgoingConnections;
        public List<Connection> incomingConnections;

        public List<float> lastValues = new() {0}; //used for random
        public float lastValue = 0f; //used for pulse
        public (List<(int dx, int dy, float weight)> offsets, float maxWeight)[] visionLUT; //used for blockage and gensim

        public void RunFunction()
        {
            float combinedInput = slotASum + slotBSum;
            switch (func)
            {
                case NFunc.Constant:
                    value = dataFields[0].floatVal;
                    break;
                case NFunc.GetRandom:
                    lastValues.Add(NeuralTools.rand.NextSingle() * 2f - 1f);
                    if (lastValues.Count > dataFields[0].intVal + 1)
                        lastValues.RemoveAt(0);
                    value = lastValues.Average();
                    break;
                case NFunc.Gradient:
                    int axis = dataFields[0].intVal;
                    value = axis == 0 ? (float)host.x / host.world.width : (float)host.y / host.world.height;
                    value = (value * 2f) - 1f;
                    break;
                case NFunc.MoveDelta:
                    bool checkRot = dataFields[0].boolVal;
                    if (checkRot)
                        value = host.facingDirection == host.lastFacing ? 0f : 1f;
                    else
                        value = (host.x != host.lastX || host.y != host.lastY) ? 1f : 0f;
                    break;
                case NFunc.Density:
                    int targetType = dataFields[0].intVal; // 0=All, 1=Food, 2=Creature, 3=Block
                    int r = dataFields[1].intVal;
                    int hits = 0;
                    int totalCells = 0;

                    for (int dx = -r; dx <= r; dx++)
                    {
                        for (int dy = -r; dy <= r; dy++)
                        {
                            int cx = host.x + dx;
                            int cy = host.y + dy;
                            if (cx >= 0 && cx < host.world.width && cy >= 0 && cy < host.world.height)
                            {
                                totalCells++;
                                Cell cell = host.world.grid[cx, cy];
                                if (targetType == 0 && (cell.occupant != null || cell.foodItem != null || cell.isBlock)) hits++;
                                else if (targetType == 1 && cell.foodItem != null) hits++;
                                else if (targetType == 2 && cell.occupant != null && cell.occupant != host) hits++;
                                else if (targetType == 3 && cell.isBlock) hits++;
                            }
                        }
                    }
                    value = totalCells > 0 ? (float)hits / totalCells : 0f;
                    break;
                case NFunc.GetSignal:
                    int channel = dataFields[0].intVal;
                    int radius = dataFields[1].intVal;
                    float maxSignal = 0f;

                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        for (int dy = -radius; dy <= radius; dy++)
                        {
                            int cx = host.x + dx;
                            int cy = host.y + dy;
                            if (cx >= 0 && cx < host.world.width && cy >= 0 && cy < host.world.height)
                            {
                                maxSignal = Math.Max(maxSignal, host.world.grid[cx, cy].signals[channel].intensity);
                            }
                        }
                    }

                    maxSignal *= host.GetPheno(PType.OlfactorySensitivity);
                    value = Math.Clamp(maxSignal, 0f, 1f);
                    break;
                case NFunc.Blockage:
                    if (visionLUT == null) GenerateVisionLUT();

                    int targetMode = dataFields[3].intVal;
                    bool sumMode = targetMode > 3;
                    int targetFilter = targetMode % 4; //0 all, 1 food, 2 creature, 3 block

                    var lut = visionLUT[host.facingDirection];
                    float accumulatedWeight = 0f;

                    foreach (var offset in lut.offsets)
                    {
                        int cx = host.x + offset.dx;
                        int cy = host.y + offset.dy;

                        if (cx < 0 || cx >= host.world.width || cy < 0 || cy >= host.world.height)
                        {
                            if (targetFilter == 0 || targetFilter == 3) accumulatedWeight += offset.weight;
                            if (!sumMode && accumulatedWeight > 0) break;
                            continue;
                        }

                        Cell cell = host.world.grid[cx, cy];
                        bool hit = false;

                        if (targetFilter == 0 && (cell.occupant != null || cell.foodItem != null || cell.isBlock)) hit = true;
                        else if (targetFilter == 1 && cell.foodItem != null) hit = true;
                        else if (targetFilter == 2 && cell.occupant != null && cell.occupant != host) hit = true;
                        else if (targetFilter == 3 && cell.isBlock) hit = true;

                        if (hit)
                        {
                            float visualWeight = offset.weight;
                            if (cell.occupant != null && cell.occupant != host)
                            {
                                visualWeight *= (1f - cell.occupant.GetPheno(PType.Camouflage));
                            }

                            accumulatedWeight += visualWeight;
                            if (!sumMode) break;
                        }
                    }

                    value = sumMode ? (lut.maxWeight > 0 ? accumulatedWeight / lut.maxWeight : 0f) : accumulatedWeight;
                    break;
                case NFunc.GeneSimilarity:
                    if (visionLUT == null) GenerateVisionLUT();

                    bool exactMatch = dataFields[3].boolVal; 
                    bool massMode = dataFields[4].boolVal; 
                    var simLut = visionLUT[host.facingDirection];

                    float totalSimScore = 0f;
                    value = 0f;

                    foreach (var offset in simLut.offsets)
                    {
                        int cx = host.x + offset.dx;
                        int cy = host.y + offset.dy;

                        if (cx >= 0 && cx < host.world.width && cy >= 0 && cy < host.world.height)
                        {
                            Creature target = host.world.grid[cx, cy].occupant;
                            if (target != null && target != host)
                            {
                                float currentSim = 0f;
                                if (exactMatch)
                                {
                                    currentSim = (host.genomeHash == target.genomeHash) ? 1f : -1f;
                                }
                                else
                                {
                                    float rDiff = MathF.Abs(host.colorR - target.colorR);
                                    float gDiff = MathF.Abs(host.colorG - target.colorG);
                                    float bDiff = MathF.Abs(host.colorB - target.colorB);

                                    float totalDiff = rDiff + gDiff + bDiff;
                                    currentSim = 1f - ((totalDiff / 765f) * 2f);
                                }

                                float visualWeight = offset.weight * (1f - target.GetPheno(PType.Camouflage));
                                totalSimScore += currentSim * visualWeight;
                                if (!massMode) break;
                            }
                        }
                    }

                    value = massMode ? (simLut.maxWeight > 0 ? totalSimScore / simLut.maxWeight : 0f) : totalSimScore;
                    break;
                case NFunc.Age:
                    value = Math.Clamp(((float)host.age / host.startingEnergy) * 3f, 0f, 1f);
                    break;

                case NFunc.Relay:
                    value = NeuralTools.FastTanh(combinedInput + dataFields[0].floatVal);
                    break;
                case NFunc.Threshold:
                    value = NeuralTools.FastTanh(0.5f + dataFields[2].floatVal * 10f * ((slotASum + slotBSum) - dataFields[0].floatVal)) * (dataFields[1].boolVal == false ? 1 : -1);
                    break;
                case NFunc.Multiply:
                    if (dataFields[0].boolVal)
                        value = NeuralTools.FastTanh(slotASum * slotBSum);
                    else
                    {
                        value = 1f;
                        foreach (var inconn in incomingConnections)
                        {
                            value = NeuralTools.FastTanh(value * inconn.src.value);
                        }
                    }
                    break;
                case NFunc.Memory:
                    value = Math.Clamp((value * dataFields[0].floatVal) + combinedInput * (1 - dataFields[0].floatVal), -1, 1);
                    break;
                case NFunc.Compare:
                    if (dataFields[0].boolVal)
                        value = NeuralTools.FastTanh((slotASum - slotBSum) * (0.5f + dataFields[1].floatVal * 7f));
                    else
                        value = NeuralTools.FastTanh((slotBSum - slotASum) * (0.5f + dataFields[1].floatVal * 7f));
                    break;
                case NFunc.Amplify:
                    value = NeuralTools.FastTanh(combinedInput * (1f + dataFields[0].floatVal * 4f));
                    break;
                case NFunc.Pulse:
                    if (Math.Abs(combinedInput - lastValue) > dataFields[0].floatVal)
                        value = dataFields[1].floatVal;
                    else
                        value = 0f;
                    lastValue = combinedInput;
                    break;

                case NFunc.Move:
                    bool absolute = dataFields[1].boolVal;
                    float moveStrength = combinedInput * (0.1f + dataFields[0].floatVal) * host.GetPheno(PType.MetabolicRate);
                    moveStrength *= host.GetPheno(PType.FastTwitchMuscle);

                    if (absolute)
                    {
                        bool isXAxis = dataFields[2].boolVal;
                        if (isXAxis) host.intentMoveX += moveStrength;
                        else host.intentMoveY += moveStrength;
                    }
                    else
                    {
                        host.intentMove += moveStrength;
                    }
                    value = combinedInput;
                    break;
                case NFunc.Rotate:
                    host.intentRotate += combinedInput * (0.1f + dataFields[0].floatVal);
                    value = combinedInput;
                    break;
                case NFunc.Jitter:
                    float strength = MathF.Abs(combinedInput) * dataFields[0].floatVal;
                    strength *= host.GetPheno(PType.JitterEfficiency);
                    bool isAbsolute = dataFields[1].boolVal;

                    if (isAbsolute)
                    {
                        if (NeuralTools.rand.NextDouble() > 0.5)
                            host.intentMoveX += (NeuralTools.rand.NextDouble() > 0.5 ? strength : -strength);
                        else
                            host.intentMoveY += (NeuralTools.rand.NextDouble() > 0.5 ? strength : -strength);
                    }
                    else
                    {
                        if (NeuralTools.rand.NextDouble() > 0.5)
                            host.intentMove += (NeuralTools.rand.NextDouble() > 0.5 ? strength : -strength);
                        else
                            host.intentRotate += (NeuralTools.rand.NextDouble() > 0.5 ? strength : -strength);
                    }

                    value = combinedInput;
                    break;
                case NFunc.EmitSignal:
                    int emitChannel = dataFields[0].intVal;
                    float customDecay = dataFields[1].floatVal;

                    if (combinedInput > 0)
                    {
                        float volume = host.GetPheno(PType.PheromoneVolume);
                        host.world.grid[host.x, host.y].signals[emitChannel].intensity += combinedInput * volume;
                        host.energy -= combinedInput * volume * 0.5f;

                        host.world.activeSignalCells.Add(host.world.grid[host.x, host.y]);

                        float mappedDecay = 0.2f + 0.797f * (1f - MathF.Pow(1f - customDecay, 3));
                        mappedDecay *= (1f / host.GetPheno(PType.ChemicalVolatility));

                        host.world.grid[host.x, host.y].signals[emitChannel].decayRate = Math.Clamp(mappedDecay, 0.1f, 0.999f);
                    }
                    value = combinedInput;
                    break;
                case NFunc.Consume:
                    host.intentConsume += combinedInput;
                    value = combinedInput;
                    break;
                case NFunc.Attack:
                    host.intentAttack += combinedInput * host.GetPheno(PType.MetabolicRate);
                    value = combinedInput;
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

        public void GenerateVisionLUT()
        {
            visionLUT = new (List<(int dx, int dy, float weight)> offsets, float maxWeight)[8];

            float requestedAngleOffset = dataFields[0].intVal * 45f;
            int fovMode = dataFields[1].intVal;

            int maxDist = (int)(dataFields[2].intVal * 
                host.GetPheno(PType.VisionAcuity) * (1 - host.GetPheno(PType.Camouflage)));
            maxDist = Math.Clamp(maxDist, 1, 20);

            int steepnessIndex = (this.func == NFunc.GeneSimilarity) ? 5 : 4;
            float steepness = 0.5f + (dataFields[steepnessIndex].intVal * 0.5f);
            steepness *= host.GetPheno(PType.FovSpecialization);

            float fovDegrees = fovMode switch
            {
                0 => 5f,
                1 => 45f,
                2 => 90f,
                3 => 180f,
                4 => 270f,
                _ => 45f
            };

            for (int facing = 0; facing < 8; facing++)
            {
                var offsets = new List<(int dx, int dy, float weight)>();

                float globalFacingAngle = facing * 45f;
                float targetAngle = (globalFacingAngle + requestedAngleOffset) % 360f;

                for (int dx = -maxDist; dx <= maxDist; dx++)
                {
                    for (int dy = -maxDist; dy <= maxDist; dy++)
                    {
                        if (dx == 0 && dy == 0) continue;

                        float dist = MathF.Sqrt(dx * dx + dy * dy);
                        if (dist > maxDist) continue;

                        float cellAngle = MathF.Atan2(dy, dx) * (180f / MathF.PI);
                        if (cellAngle < 0) cellAngle += 360f;

                        float diff = MathF.Abs(targetAngle - cellAngle);
                        if (diff > 180f) diff = 360f - diff;

                        if (diff <= fovDegrees / 2f)
                        {
                            float distWeight = 1f - (dist / maxDist);
                            float angleWeight = 1f - (diff / (fovDegrees / 2f));
                            float finalWeight = MathF.Pow(distWeight * angleWeight, steepness);

                            offsets.Add((dx, dy, finalWeight));
                        }
                    }
                }

                offsets = offsets.OrderByDescending(v => v.weight).ToList();
                float maxWeight = offsets.Sum(o => o.weight);
                visionLUT[facing] = (offsets, maxWeight);
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

            var payload = new
            {
                graph = graphID,
                nodes = nodes,
                edges = edges
            };

            string json = JsonSerializer.Serialize(payload,
                new JsonSerializerOptions
                {
                    WriteIndented = false,
                    IncludeFields = true
                }
            );

            foreach (var client in NEMO.clients.ToList())
            {
                client.Send(json);
            }
        }
    }
}
