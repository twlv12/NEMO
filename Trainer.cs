using Microsoft.CodeAnalysis.Scripting;
using System.Collections.Concurrent;
using System.Text.Json;

namespace NEMO
{
    public class TVars
    {
        public TCreature c;
        public TWorld world;

        public int x => c.x;
        public int y => c.y;
        public float energy => c.energy;
        public int kills => c.kills;
        public int age => c.age;

        public int width =>  world.width;
        public int height => world.height;
    }

    public class TWorld : IWorld
    {
        public TCell[] grid;
        public ICell[] iGrid => grid;
        public List<TCreature> activeCreatures = new List<TCreature>();
        public List<TCreature> baitCreatures = new List<TCreature>();
        public List<TCreature> deadActiveCreatures = new List<TCreature>();

        public int width { get; set; } = Config.TworldWidth;
        public int height { get; set; } = Config.TworldHeight;
        public static Random rand = new Random();
        public ScriptRunner<bool>? customCondition = null;

        public float weightKills = 1.0f;
        public float weightEfficiency = 0.0f;
        public float weightSurvival = 0.0f;
        public float weightCEQ = 0.0f;
        public int roundTicks = 0;
        public float lastGenAvgKills = 0f;
        public int lastGenMaxKills = 0;
        public float lastGenAvgScore = 0f;
        public float lastGenMaxScore = 0f;
        public float lastGenAvgEfficiency = 0f;
        public float lastGenAvgSurvival = 0f;
        public float lastGenAvgCEQ = 0f;
        public float lastGenMaxCEQ = 0f;

        long IWorld.totalTicks => roundTicks;
        public int currentGeneration = 1;

        public TWorld(int width, int height)
        {
            NEMO.Log("[TWORLD] Initializing Trainer World...", "#aaa", ConsoleColor.DarkGray);
            this.width = width;
            this.height = height;

            grid = new TCell[width * height];
            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                    grid[x + (y * width)] = new TCell(x, y);
        }

        public void Update()
        {
            roundTicks++;

            var allCreatures = activeCreatures.Concat(baitCreatures).ToList();

            Parallel.ForEach(Partitioner.Create(0, allCreatures.Count), range => {
                for (int i = range.Item1; i < range.Item2; i++)
                {
                    if (!allCreatures[i].isDead) allCreatures[i].Update();
                }
            });

            ResolveIntents(allCreatures);
            foreach (var c in allCreatures) EvaluateCEQ(c);

            foreach (var ac in activeCreatures.Where(c => c.isDead))
            {
                grid[ac.x + (ac.y * width)].occupant = null;
                deadActiveCreatures.Add(ac);
            }
            foreach (var bc in baitCreatures.Where(c => c.isDead))
            {
                grid[bc.x + (bc.y * width)].occupant = null;
            }

            activeCreatures.RemoveAll(c => c.isDead);
            baitCreatures.RemoveAll(c => c.isDead);

            while (baitCreatures.Count < Config.TnumBaitCreatures)
            {
                int x = rand.Next(0, width);
                int y = rand.Next(0, height);

                if (grid[x + (y * width)].occupant == null)
                {
                    TCreature newBait = new TCreature(x, y, null, this);
                    baitCreatures.Add(newBait);
                    grid[x + (y * width)].occupant = newBait;
                }
            }

            if ((baitCreatures.Count == 0 && Config.TnumBaitCreatures != 0) || activeCreatures.Count == 0 || roundTicks > Config.TmaxTime)
                EndRoundAndBreed();
        }

        private void EndRoundAndBreed()
        {
            NEMO.Log($"[TWORLD] Round {currentGeneration} finished in {roundTicks} ticks.", "#ff99cc", ConsoleColor.Magenta);

            var allCandidates = activeCreatures.Concat(deadActiveCreatures).ToList();
            float maxKillsInGen = allCandidates.Count > 0 ? Math.Max(1f, allCandidates.Max(c => c.kills)) : 1f;
            float totalWeightSum = weightKills + weightEfficiency + weightSurvival + weightCEQ;
            float weightDivisor = Math.Max(1.0f, totalWeightSum);

            foreach (var c in allCandidates)
            {
                float normalizedKills = Math.Clamp(c.kills / maxKillsInGen, 0f, 1f);
                float normalizedEfficiency = c.foodItemsEaten > 0 ? Math.Clamp(c.totalPathEfficiencySum / c.foodItemsEaten, 0f, 1f) : Math.Clamp(c.successfulActions / Math.Max(1f, c.totalActionAttempts), 0f, 1f);
                float normalizedSurvival = Math.Clamp(c.age / (float)Config.TmaxTime, 0f, 1f);
                float normalizedCEQ = Math.Clamp(c.ceqScore, 0f, 1f);

                float rawScore = (normalizedKills * weightKills) +
                                 (normalizedEfficiency * weightEfficiency) +
                                 (normalizedSurvival * weightSurvival) +
                                 (normalizedCEQ * weightCEQ);

                c.trainerScore = rawScore / weightDivisor;

                if (customCondition != null)
                {
                    try
                    {
                        bool passed = customCondition(new TVars { c = c, world = this }).Result;
                        if (!passed)
                            c.trainerScore = 0f;
                        else if (c.trainerScore == 0f)
                            c.trainerScore = 1f;
                    }
                    catch
                    {
                        c.trainerScore = 0f;
                    }
                }
            }

            lastGenMaxKills = allCandidates.Count > 0 ? allCandidates.Max(c => c.kills) : 0;
            lastGenAvgKills = allCandidates.Count > 0 ? (float)allCandidates.Average(c => c.kills) : 0f;
            lastGenMaxScore = allCandidates.Count > 0 ? allCandidates.Max(c => c.trainerScore) : 0f;
            lastGenAvgScore = allCandidates.Count > 0 ? (float)allCandidates.Average(c => c.trainerScore) : 0f;
            lastGenAvgEfficiency = allCandidates.Count > 0 ? (float)allCandidates.Average(c => c.foodItemsEaten > 0 ? (c.totalPathEfficiencySum / c.foodItemsEaten) : 0f) : 0f;
            lastGenAvgSurvival = allCandidates.Count > 0 ? (float)allCandidates.Average(c => c.age) : 0f;
            lastGenMaxCEQ = allCandidates.Count > 0 ? allCandidates.Max(c => c.ceqScore) : 0f;
            lastGenAvgCEQ = allCandidates.Count > 0 ? (float)allCandidates.Average(c => c.ceqScore) : 0f;

            int totalActive = Config.TnumActiveCreatures;
            int numElites = Math.Max(Config.TnumToSelect, (int)(totalActive * 0.05f));

            var champions = allCandidates.OrderByDescending(c => c.trainerScore).Take(numElites).ToList();
            List<Genome> nextGenPool = new List<Genome>();

            foreach (var champ in champions)
            {
                nextGenPool.Add(champ.genome.Clone());
            }

            int remainingToFill = totalActive - nextGenPool.Count;
            for (int i = 0; i < remainingToFill; i++)
            {
                if (champions.Count > 0)
                {
                    var randomChamp = champions[TWorld.rand.Next(champions.Count)];
                    nextGenPool.Add(GeneTools.MutateGenome(randomChamp.genome.Clone()));
                }
                else 
                    nextGenPool.Add(GeneTools.GenerateGenome());
            }

            roundTicks = 0;
            currentGeneration++;
            activeCreatures.Clear();
            deadActiveCreatures.Clear();
            baitCreatures.Clear();

            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                    grid[x + (y * width)].occupant = null;

            ScatterActive(totalActive, nextGenPool);
            ScatterBait(Config.TnumBaitCreatures);
        }

        private void ResolveIntents(List<TCreature> allCreatures)
        {
            foreach (var c in allCreatures)
            {
                if (c.isDead) continue;
                c.age++;

                if (c.brain == null)
                {
                    if (rand.NextDouble() < 0.1) c.intentRotate = rand.NextDouble() > 0.5 ? 1f : -1f;
                    if (rand.NextDouble() < 0.2) c.intentMove = 1f;
                }

                if (Math.Abs(c.intentRotate) > 0.01f && rand.NextDouble() < Math.Abs(c.intentRotate))
                {
                    if (c.intentRotate > 0) c.facingDirection = (c.facingDirection + 1) % 8;
                    else c.facingDirection = (c.facingDirection + 7) % 8;

                    float rotCost = NEMO.disableEnergyDrain ? 0f :
                    Config.TmovementCost * 0.25f * c.GetPheno(PType.BodyMass) * (1f / Math.Max(0.01f, c.GetPheno(PType.RotationalAgility))) * Math.Abs(c.intentRotate) * Config.TrotationMulti;

                    c.energy -= rotCost;
                }

                var relVec = DirectionToVector[c.facingDirection];
                float dxFloat = (relVec.dx * c.intentMove) + c.intentMoveX;
                float dyFloat = (relVec.dy * c.intentMove) + c.intentMoveY;
                float conviction = MathF.Max(MathF.Abs(dxFloat), MathF.Abs(dyFloat));

                if (conviction > 0.01f && rand.NextDouble() < conviction)
                {
                    int targetX = c.x + Math.Sign(dxFloat);
                    int targetY = c.y + Math.Sign(dyFloat);
                    bool outOfBounds = targetX < 0 || targetX >= width || targetY < 0 || targetY >= height;

                    if (!outOfBounds)
                    {
                        TCreature targetOccupant = grid[targetX + (targetY * width)].occupant;

                        if (targetOccupant == null)
                        {
                            grid[c.x + (c.y * width)].occupant = null;
                            c.x = targetX;
                            c.y = targetY;
                            grid[targetX + (targetY * width)].occupant = c;

                            if (!NEMO.disableEnergyDrain)
                            {
                                float massMoveFactor = MathF.Pow(Math.Max(0.1f, c.GetPheno(PType.BodyMass)), 0.5f);

                                float movementPenalty = Config.TmovementCost *
                                    conviction *
                                    massMoveFactor *
                                    c.GetPheno(PType.FastTwitchMuscle) *
                                    Math.Max(0.5f, c.GetPheno(PType.RestingEfficiency)) *
                                    (1f + c.GetPheno(PType.RotationalAgility) * 0.2f);

                                c.energy -= movementPenalty;
                            }
                        }
                        else if (!c.isBait && targetOccupant.isBait && !targetOccupant.isDead)
                        {
                            if (Config.TinstantKills)
                            {
                                targetOccupant.isDead = true;
                                c.kills++;
                                c.successfulActions++;

                                baitCreatures.Remove(targetOccupant);
                                grid[c.x + (c.y * width)].occupant = null;
                                c.x = targetX;
                                c.y = targetY;
                                grid[targetX + (targetY * width)].occupant = c;
                            }
                        }
                        else
                        {
                            int dx = Math.Sign(dxFloat);
                            int dy = Math.Sign(dyFloat);
                            for (int d = 0; d < 8; d++)
                            {
                                if (DirectionToVector[d].dx == -dx && DirectionToVector[d].dy == -dy)
                                {
                                    c.facingDirection = d; break;
                                }
                            }
                        }
                    }
                    else
                    {
                        if (!c.isBait && Config.TwallDeath)
                        {
                            c.isDead = true;
                            grid[c.x + (c.y * width)].occupant = null;
                            break;
                        }

                        int dx = Math.Sign(dxFloat);
                        int dy = Math.Sign(dyFloat);
                        if (c.x + dx < 0 || c.x + dx >= width) dx = -dx;
                        if (c.y + dy < 0 || c.y + dy >= height) dy = -dy;

                        for (int d = 0; d < 8; d++)
                        {
                            if (DirectionToVector[d].dx == dx && DirectionToVector[d].dy == dy)
                            {
                                c.facingDirection = d; break;
                            }
                        }
                    }
                }

                if (c.intentAttack > 0.1f && c.brain != null)
                {
                    c.totalActionAttempts++;

                    float attackCost = Config.attackCost * c.GetPheno(PType.MetabolicRate) * c.GetPheno(PType.Lethality);
                    c.energy -= NEMO.disableEnergyDrain ? 0f : attackCost;

                    var vec = DirectionToVector[c.facingDirection];
                    int targetX = c.x + vec.dx;
                    int targetY = c.y + vec.dy;

                    if (targetX >= 0 && targetX < width && targetY >= 0 && targetY < height)
                    {
                        TCreature target = grid[targetX + (targetY * width)].occupant;
                        if (target != null && !target.isDead)
                        {
                            c.successfulActions++;

                            float damage = Config.baseAttackDmg * c.GetPheno(PType.Lethality);
                            target.energy -= damage;
                            c.damageDealt += damage;
                            c.energy += damage;

                            if (target.energy <= 0)
                            {
                                target.isDead = true;
                                c.kills++;
                                grid[targetX + (targetY * width)].occupant = null;
                            }
                        }
                    }
                }

                if (!NEMO.disableEnergyDrain) c.energy -= c.GetBaseTickCost();
                if (c.energy <= 0)
                {
                    c.isDead = true;
                    grid[c.x + (c.y * width)].occupant = null;
                }
                c.energy = Math.Clamp(c.energy, 0f, c.startingEnergy);

                c.ResetIntents();
            }
        }

        public void ScatterActive(int numActive, List<Genome> genomePool)
        {
            NEMO.Log($"[TWORLD] Spawning {numActive} active creatures...", "#aaa", ConsoleColor.DarkGray);

            activeCreatures = new List<TCreature>();
            int genomeIndex = 0;

            while (activeCreatures.Count < numActive)
            {
                int x = rand.Next(0, width);
                int y = rand.Next(0, height);

                if (grid[x + (y * width)].occupant == null)
                {
                    Genome gen;
                    if (genomePool.Count > 0)
                    {
                        gen = genomePool[genomeIndex % genomePool.Count];
                        genomeIndex++;
                    }
                    else
                    {
                        gen = GeneTools.GenerateGenome();
                    }

                    TCreature c = new TCreature(x, y, gen, this);
                    activeCreatures.Add(c);
                    grid[x + (y * width)].occupant = c;
                }
            }
        }
        public void ScatterBait(int numBait)
        {
            NEMO.Log($"[TWORLD] Spawning {numBait} bait...", "#aaa", ConsoleColor.DarkGray);

            baitCreatures = new List<TCreature>();
            while (baitCreatures.Count < numBait)
            {
                int x = rand.Next(0, width);
                int y = rand.Next(0, height);

                if (grid[x + (y * width)].occupant == null)
                {
                    TCreature c = new TCreature(x, y, null, this);

                    baitCreatures.Add(c);
                    grid[x + (y * width)].occupant = c;
                }
            }
        }

        public void EvaluateCEQ(TCreature c)
        {
            float causality = c.foodItemsEaten > 0 ? (c.totalPathEfficiencySum / c.foodItemsEaten) : 0f;
            float precision = c.totalActionAttempts > 0 ? (c.successfulActions / c.totalActionAttempts) : 0f;
            float entropy = c.reactivitySamples > 0 ? (c.conditionalReactivityScore / c.reactivitySamples) : 0f;

            float ceqScore = (causality * 0.35f) + (precision * 0.25f) + (entropy * 0.40f);
            c.ceqScore = ceqScore;
        }

        public string GetStateJson(TCreature[] creaturesSnap)
        {
            var exportCreatures = new List<World.ExportCreature>(creaturesSnap.Length);
            float[] angleMap = new float[] { -90f, -45f, -20f, 0f, 20f, 45f, 90f, 180f };

            foreach (var c in creaturesSnap)
            {
                var creatureCones = new List<World.ExportCone>();

                if (c.brain != null)
                {
                    for (int n = 0; n < c.brain.neurons.Count; n++)
                    {
                        var vNeuron = c.brain.neurons[n];
                        if (NeuronDicts.VisionNeurons.Contains(vNeuron.func) && vNeuron.dataFields != null && vNeuron.dataFields.Length >= 3)
                        {
                            int fovMode = vNeuron.dataFields[1].intVal;
                            float cFov = fovMode switch { 0 => 5f, 1 => 45f, 2 => 90f, 3 => 180f, 4 => 270f, _ => 45f };

                            float acuity = c.genome != null ? c.GetPheno(PType.VisionAcuity) : 0f;
                            float cRange = vNeuron.dataFields[2].intVal * (1f + acuity);
                            float cOffset = angleMap[Math.Clamp(vNeuron.dataFields[0].intVal, 0, 7)];

                            int steepnessVal = 0;
                            if (vNeuron.func == NFunc.VisionGenSim && vNeuron.dataFields.Length > 5) steepnessVal = vNeuron.dataFields[5].intVal;
                            else if (vNeuron.dataFields.Length > 4) steepnessVal = vNeuron.dataFields[4].intVal;

                            creatureCones.Add(new World.ExportCone { range = cRange, fov = cFov, offset = cOffset, steepness = steepnessVal });
                        }
                    }
                }

                exportCreatures.Add(new World.ExportCreature
                {
                    id = c.ID.ToString(),
                    x = c.x,
                    y = c.y,
                    dir = c.facingDirection,
                    r = c.colorR,
                    g = c.colorG,
                    b = c.colorB,
                    energy = c.startingEnergy > 0 ? Math.Clamp(c.energy / c.startingEnergy, 0f, 1f) : 1f,
                    slot = "",
                    cones = creatureCones,
                    parentId = "",
                    diet = 1,
                    lineage = 0,
                    mass = c.genome != null ? c.GetPheno(PType.BodyMass) : 1f,
                    sr = c.survivalRatio
                });
            }

            object? trackedInfoObj = null;
            if (!string.IsNullOrEmpty(NEMO.trackedCreatureId))
            {
                var tc = creaturesSnap.FirstOrDefault(c => c.ID.ToString() == NEMO.trackedCreatureId);
                if (tc != null)
                {
                    int sensors = tc.brain?.neurons.Count(n => n.type == NType.Sensor) ?? 0;
                    int maths = tc.brain?.neurons.Count(n => n.type == NType.Math) ?? 0;
                    int actions = tc.brain?.neurons.Count(n => n.type == NType.Action) ?? 0;
                    int activeConnections = tc.brain?.connections.Count(c => c.src != null && c.tgt != null) ?? 0;

                    World.defaultGenomeRef ??= new Genome(new List<Gene>());
                    if (World.defaultGenomeRef.phenotypes == null || World.defaultGenomeRef.phenotypes.Count == 0)
                        World.defaultGenomeRef.InitializeDefaultPhenotypes();

                    var topPhenos = tc.genome != null ? tc.genome.phenotypes
                        .Select(kvp => {
                            float def = World.defaultGenomeRef.phenotypes.ContainsKey(kvp.Key)
                                ? World.defaultGenomeRef.phenotypes[kvp.Key].value
                                : 0.5f;
                            float cur = kvp.Value.value;
                            float diff = cur - def;
                            return new { name = kvp.Key.ToString(), val = cur, diff = diff };
                        })
                        .OrderByDescending(x => Math.Abs(x.diff))
                        .Take(4)
                        .Cast<object>()
                        .ToList() : new List<object>();

                    trackedInfoObj = new
                    {
                        id = tc.ID.ToString(),
                        age = tc.age,
                        gen = currentGeneration,
                        survivalRatio = tc.survivalRatio,
                        ceq = tc.ceqScore,
                        bodyMass = tc.genome != null ? tc.GetPheno(PType.BodyMass) : 1f,
                        lineage = 0f,
                        energy = tc.energy,
                        kills = tc.kills,
                        damageDealt = tc.damageDealt,
                        meatsEaten = tc.meatsEaten,
                        plantsEaten = tc.plantsEaten,
                        energyPct = tc.startingEnergy > 0 ? Math.Clamp(tc.energy / tc.startingEnergy, 0f, 1f) : 1f,
                        action = tc.currentAction,
                        diet = tc.isBait ? "Bait" : "Hunter",
                        sensors = sensors,
                        maths = maths,
                        actions = actions,
                        totalGenes = tc.genome?.genes.Count ?? 0,
                        activeConnections = activeConnections,
                        phenos = topPhenos
                    };
                }
            }

            var payload = new
            {
                type = "petri",
                width = this.width,
                height = this.height,
                fertMap = "", 
                stats = new
                {
                    ticks = roundTicks,            
                    maxGen = currentGeneration,    
                    hunters = activeCreatures.Count,
                    herbivores = baitCreatures.Count,
                    tps = NEMO.currentTPS,
                    pop = creaturesSnap.Length,
                    extinctions = 0,
                    highestSurvivalRatio = 0f,
                    highestCEQ = 0f,
                    plants = 0,
                    meat = 0,
                    eIn = 0f,
                    eOut = 0f,
                    totalCreatureE = 0f,
                    totalPlantE = 0f,
                    totalMeatE = 0f,
                    births = 0f,
                    deaths = 0f,
                    lifeMeas = 0f,
                    lifeMath = 0f,
                    plantsEaten = 0f,
                    meatsEaten = 0f,
                    attacks = 0f,
                    killRate = 0f,
                    avgAge = 0f,
                    avgGen = 0f,
                    simLoad = NEMO.emaSimTime,
                    uiLoad = NEMO.emaUiTime,
                    omnivores = 0,
                    scavengers = 0,
                    parasites = 0,
                    avgCarnivory = 0f,
                    avgGenes = 0f,
                    massP10 = 0f,
                    massP50 = 0f,
                    massP90 = 0f,
                    govCap = 0f,
                    govCurE = 0f,
                    govBaseE = 0f,
                    govActLife = 0f,
                    govMathLife = 0f,
                    govBlend = 0f,
                    govMom = 0f,
                    govWastePen = 0f,
                    govDiet = 0f,
                    lastGenAvgKills = lastGenAvgKills,
                    lastGenMaxKills = lastGenMaxKills,
                    lastGenAvgScore = lastGenAvgScore,
                    lastGenMaxScore = lastGenMaxScore,
                    lastGenAvgEfficiency = lastGenAvgEfficiency,
                    lastGenAvgSurvival = lastGenAvgSurvival,
                    lastGenAvgCEQ = lastGenAvgCEQ,
                    lastGenMaxCEQ = lastGenMaxCEQ,
                },
                trackedInfo = trackedInfoObj,
                blocks = new List<World.ExportBlock>(),
                foods = new List<World.ExportFood>(),
                creatures = exportCreatures
            };

            return JsonSerializer.Serialize(payload);
        }

        public bool IsCellObstructed(int x, int y)
        {
            if (x < 0 || x >= width || y < 0 || y >= height) return true;
            return grid[x + (y * width)].occupant != null;
        }
        public static (int dx, int dy)[] DirectionToVector = new (int, int)[] {
            ( 0, -1), // 0 north
            ( 1, -1), // 1 northeast
            ( 1,  0), // 2 east
            ( 1,  1), // 3 southeast
            ( 0,  1), // 4 south
            (-1,  1), // 5 southwest
            (-1,  0), // 6 west
            (-1, -1)  // 7 northwest
        };
    }

    public class TCreature : ICreature
    {
        public Genome? genome;
        public Brain? brain;
        public TWorld world;
        public bool isBait;
        public Guid ID = Guid.NewGuid();

        #region Initializations
        IWorld ICreature.world => world;
        float[] ICreature.phenoCache => phenoCache;

        public int x { get; set; }
        public int y { get; set; }
        public int lastX { get; set; }
        public int lastY { get; set; }
        public int facingDirection { get; set; }
        public int lastFacing { get; set; }
        public float startingEnergy { get; set; }
        public float energy { get; set; } = Config.TbaseStartingEnergy;
        public bool isDead { get; set; } = false;

        public float intentMove { get; set; } = 0f;
        public float intentMoveX { get; set; } = 0f;
        public float intentMoveY { get; set; } = 0f;
        public float intentRotate { get; set; } = 0f;
        public float intentConsume { get; set; } = 0f;
        public float intentAttack { get; set; } = 0f;
        public float intentSignalIntensity { get; set; } = 0f;
        public int intentSignalChannel { get; set; } = -1;
        public float intentSignalDecay { get; set; } = 0f;

        float[] phenoCache { get; }
        public int genomeHash { get; set; }
        public byte colorR { get; set; }
        public byte colorG { get; set; }
        public byte colorB { get; set; }
        public int age { get; set; } = 0;

        public string currentAction = "Idle";
        public int meatsEaten = 0;
        public int plantsEaten = 0;
        public float damageDealt = 0f;
        public int kills = 0;
        public int ticksSinceLastMove = 1000;
        public int lastFoodX = -1;
        public int lastFoodY = -1;
        public float totalPathEfficiencySum = 0f;
        public int foodItemsEaten = 0;
        public float totalActionAttempts = 0f;
        public float successfulActions = 0f;
        public float lastSensorSum = 0f;
        public float lastMotorSum = 0f;
        public float conditionalReactivityScore = 0f;
        public int reactivitySamples = 0;
        public float trainerScore = 0f;
        public float ceqScore = 0f;
        public float survivalRatio = 0f;
        #endregion

        public TCreature(int x, int y, Genome? gen, TWorld world)
        {
            this.x = x;
            this.y = y;
            this.lastX = x;
            this.lastY = y;
            this.facingDirection = TWorld.rand.Next(0, 8);
            this.lastFacing = this.facingDirection;
            this.world = world;

            int pTypeCount = Enum.GetValues(typeof(PType)).Length;
            this.phenoCache = new float[pTypeCount];

            if (gen != null)
            {
                genome = gen;
                genomeHash = gen.GenerateExactHash();
                isBait = false;

                foreach (var kvp in genome.phenotypes)
                    phenoCache[(int)kvp.Key] = kvp.Value.value;

                startingEnergy = Config.TbaseStartingEnergy * GetPheno(PType.BodyMass);
                energy = startingEnergy; 

                var color = genome.GenerateColor();
                colorR = color.r;
                colorG = color.g;
                colorB = color.b;

                brain = NeuralTools.GenomeToBrain(genome);

                foreach (Neuron n in brain.neurons)
                {
                    n.host = this;
                }
            }
            else
            {
                genome = null;
                brain = null;
                isBait = true;

                colorR = 100; 
                colorG = 100;
                colorB = 100;

                if (World.defaultGenomeRef == null)
                {
                    World.defaultGenomeRef = new Genome(new List<Gene>());
                    World.defaultGenomeRef.InitializeDefaultPhenotypes();
                }
                foreach (var kvp in World.defaultGenomeRef.phenotypes)
                {
                    phenoCache[(int)kvp.Key] = kvp.Value.value;
                }

                startingEnergy = Config.TbaseStartingEnergy;
                energy = startingEnergy * Config.deathEnergy * 1.5f;
            }
        }

        public void Update()
        {
            if (isBait || brain == null) return;

            brain.UpdateAllNeurons();

            if (age % 10 == 0)
            {
                float currentSensorSum = 0f;
                float currentMotorSum = 0f;

                for (int i = 0; i < brain.neurons.Count; i++)
                {
                    Neuron n = brain.neurons[i];
                    if (n.type == NType.Sensor) currentSensorSum += n.value;
                    else if (n.type == NType.Action) currentMotorSum += n.value;
                }

                float sensorDelta = MathF.Abs(currentSensorSum - lastSensorSum);
                float motorDelta = MathF.Abs(currentMotorSum - lastMotorSum);

                if (sensorDelta > 0.1f)
                {
                    reactivitySamples++;
                    conditionalReactivityScore += Math.Clamp(motorDelta, 0f, 1f);
                }

                lastSensorSum = currentSensorSum;
                lastMotorSum = currentMotorSum;
            }
        }

        public void ResetIntents()
        {
            intentMove = 0f;
            intentMoveX = 0f;
            intentMoveY = 0f;
            intentRotate = 0f;
            intentConsume = 0f;
            intentAttack = 0f;
            intentSignalIntensity = 0f;
            intentSignalChannel = -1;
            intentSignalDecay = 0f;
        }

        public float GetPheno(PType type) => phenoCache[(int)type];

        public float GetBaseTickCost()
        {
            float massFactor = MathF.Pow(Math.Max(0.1f, GetPheno(PType.BodyMass)), 0.75f);

            return Config.TcostOfLiving
                 * GetPheno(PType.MetabolicRate)
                 * massFactor
                 * GetPheno(PType.BrainSize)
                 * (1f + GetPheno(PType.VisionAcuity) * 0.1f)
                 * (1f + GetPheno(PType.SpikeCoating) * 0.2f)
                 * (1f + GetPheno(PType.Camouflage) * 0.2f);
        }
    }

    public class TCell : ICell
    {
        public int x;
        public int y;

        public bool isBlock => false;
        public object? foodItem => null;
        public SignalData[] signals => new SignalData[16];

        public TCreature? occupant = null;

        ICreature? ICell.occupant => occupant;

        public TCell(int x, int y)
        {
            this.x = x;
            this.y = y;
        }
    }

    public class TFoodItem
    {
        public int x;
        public int y;
        public float nutrition;
        public bool isMeat;

        public TFoodItem(int x, int y, bool isMeat)
        {
            this.x = x;
            this.y = y;
            this.isMeat = isMeat;
            this.nutrition = Config.TbaseNutrition;
        }
    }
}