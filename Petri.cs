using System.Text.Json;

namespace NEMO
{
    public class World
    {
        public Cell[,] grid;
        public List<Creature> creatures;

        public int width = Config.worldWidth;
        public int height = Config.worldHeight;
        public static Random rand = new Random();

        #region Telemetry
        public long totalTicks = 0;
        public Genome? bestGenome = null;
        public int highestGeneration = 0;

        public float emaEnergyIn = 0f;
        public float emaEnergyOut = 0f;
        public float emaBirths = 0f;
        public float emaDeaths = 0f;
        public float emaLifespan = 0f;

        public float tickEnergyIn = 0f;
        public int tickBirths = 0;
        public int tickDeaths = 0;

        public float emaPlantsEaten = 0f;
        public float emaMeatsEaten = 0f;
        public float emaAttacks = 0f;
        public int tickPlantsEaten = 0;
        public int tickMeatsEaten = 0;
        public int tickAttacks = 0;

        public float CalculateTotalEnergy()
        {
            float total = 0;
            foreach (var c in creatures) total += c.energy;
            foreach (var f in activeFoods) total += f.nutrition;
            return total;
        }
        #endregion

        #region Hashes & Caches
        public HashSet<Cell> activeSignalCells = new HashSet<Cell>();
        public List<FoodItem> activeFoods = new List<FoodItem>();
        public List<ExportBlock> staticBlocks = new List<ExportBlock>(); // Cache blocks!

        public struct ExportFood { public int x { get; set; } public int y { get; set; } public bool meat { get; set; } }
        public struct ExportBlock { public int x { get; set; } public int y { get; set; } }
        public struct ExportCreature { public string id { get; set; } public float x { get; set; } public float y { get; set; } public int dir { get; set; } public byte r { get; set; } public byte g { get; set; } public byte b { get; set; } }
        #endregion

        public void Update()
        {
            #region Stat Resets
            totalTicks++;
            tickEnergyIn = 0f;
            tickBirths = 0;
            tickDeaths = 0;

            emaPlantsEaten = 0f;
            emaMeatsEaten = 0f;
            emaAttacks = 0f;
            tickPlantsEaten = 0;
            tickMeatsEaten = 0;
            tickAttacks = 0;
            #endregion

            float preUpdateEnergy = CalculateTotalEnergy();

            List<FoodItem> rottedMeat = new List<FoodItem>();
            foreach (var f in activeFoods)
            {
                if (f.isMeat)
                {
                    f.nutrition -= 0.5f;
                    if (f.nutrition <= 0) rottedMeat.Add(f);
                }
            }
            foreach (var r in rottedMeat)
            {
                grid[r.x, r.y].foodItem = null;
                activeFoods.Remove(r);
            }

            int currentPlants = activeFoods.Count(f => !f.isMeat);
            if (currentPlants < Config.worldWidth * Config.worldHeight * Config.foodWorldCoverage)
            {
                for (int i = 0; i < Config.plantGrowthRate; i++)
                {
                    int fx = World.rand.Next(width);
                    int fy = World.rand.Next(height);
                    if (grid[fx, fy].foodItem == null && grid[fx, fy].occupant == null && !grid[fx, fy].isBlock)
                    {
                        var plant = new FoodItem(fx, fy, false);
                        grid[fx, fy].foodItem = plant;
                        activeFoods.Add(plant);

                        tickEnergyIn += Config.baseNutrition;
                    }
                }
            }

            DecaySignals();
            foreach (var creature in creatures)
            {
                if (creature.isDead) continue;
                creature.Update();
            }
            ResolveIntents();

            creatures.RemoveAll(c => c.isDead);
            if (creatures.Count < Config.creatureCount && Config.maintainPopulation)
            {
                int x = rand.Next(0, width);
                int y = rand.Next(0, height);

                if (!grid[x, y].isBlock && grid[x, y].occupant == null)
                {
                    Creature c = new Creature(x, y, GeneTools.GenerateGenome(), this);
                    creatures.Add(c);
                    grid[x, y].occupant = c;

                    tickEnergyIn += c.startingEnergy * 0.25f;
                }
            }

            float postUpdateEnergy = CalculateTotalEnergy();
            float tickEnergyOut = (preUpdateEnergy + tickEnergyIn) - postUpdateEnergy;

            #region Stat EMA Calculations
            float alpha = 0.01f;
            emaEnergyIn = (emaEnergyIn * (1f - alpha)) + (tickEnergyIn * alpha);
            emaEnergyOut = (emaEnergyOut * (1f - alpha)) + (tickEnergyOut * alpha);
            emaBirths = (emaBirths * (1f - alpha)) + (tickBirths * alpha);
            emaDeaths = (emaDeaths * (1f - alpha)) + (tickDeaths * alpha);
            emaPlantsEaten = (emaPlantsEaten * (1f - alpha)) + (tickPlantsEaten * alpha);
            emaMeatsEaten = (emaMeatsEaten * (1f - alpha)) + (tickMeatsEaten * alpha);
            emaAttacks = (emaAttacks * (1f - alpha)) + (tickAttacks * alpha);
            #endregion
        }

        public void DecaySignals()
        {
            List<Cell> cellsToClear = new List<Cell>();
            foreach (var cell in activeSignalCells)
            {
                bool hasActiveSignal = false;
                for (int c = 0; c < 16; c++)
                {
                    if (cell.signals[c].intensity > 0.01f)
                    {
                        cell.signals[c].intensity *= cell.signals[c].decayRate;
                        hasActiveSignal = true;
                    }
                    else
                    {
                        cell.signals[c] = new SignalData();
                    }
                }
                if (!hasActiveSignal) cellsToClear.Add(cell);
            }
            foreach (var cell in cellsToClear) activeSignalCells.Remove(cell);
        }

        private void ResolveIntents()
        {
            List<Creature> movingCreatures = new List<Creature>();
            List<Creature> newborns = new List<Creature>(); // FIX: The nursery list!

            foreach (var c in creatures)
            {
                if (c.isDead) continue;
                c.age++; // Tick the age!

                c.lastX = c.x;
                c.lastY = c.y;
                c.lastFacing = c.facingDirection;

                if (Math.Abs(c.intentRotate) > 0.01f && rand.NextDouble() < Math.Abs(c.intentRotate))
                {
                    if (c.intentRotate > 0) c.facingDirection = (c.facingDirection + 1) % 8;
                    else c.facingDirection = (c.facingDirection + 7) % 8;
                    float rotCost = Config.movementCost * 0.25f * (1f / c.GetPheno(PType.RotationalAgility));
                    c.energy -= rotCost;
                }

                var relVec = DirectionToVector[c.facingDirection];
                float dxFloat = (relVec.dx * c.intentMove) + c.intentMoveX;
                float dyFloat = (relVec.dy * c.intentMove) + c.intentMoveY;
                float conviction = MathF.Max(MathF.Abs(dxFloat), MathF.Abs(dyFloat));

                if (conviction > 0.01f && rand.NextDouble() < conviction)
                {
                    c.intentMoveX = Math.Sign(dxFloat);
                    c.intentMoveY = Math.Sign(dyFloat);
                    c.intentMove = conviction;
                    movingCreatures.Add(c);
                }
            }

            movingCreatures = movingCreatures.OrderByDescending(c => c.intentMove * c.GetPheno(PType.BodyMass)).ToList();

            foreach (var c in movingCreatures)
            {
                int targetX = c.x + (int)c.intentMoveX;
                int targetY = c.y + (int)c.intentMoveY;

                if (!IsCellObstructed(targetX, targetY))
                {
                    c.energy -= Config.movementCost *
                        c.GetPheno(PType.BodyMass) *
                        c.GetPheno(PType.FastTwitchMuscle) *
                        (1f + c.GetPheno(PType.ArmorDensity)) *
                        (1f + c.GetPheno(PType.RotationalAgility) * 0.2f);

                    grid[c.x, c.y].occupant = null;
                    c.x = targetX;
                    c.y = targetY;
                    grid[targetX, targetY].occupant = c;
                }

                else if (targetX < 0 || targetX >= width || targetY < 0 || targetY >= height)
                {
                    c.facingDirection = (c.facingDirection + 4) % 8;
                    c.intentMoveX = 0;
                    c.intentMoveY = 0;
                    c.intentMove = 0;
                    //c.energy -= 300f;
                }
            }

            foreach (var c in creatures)
            {
                if (c.isDead) continue;

                float maturation = Math.Min(1f, c.age / 50f);

                if (rand.NextDouble() < c.intentAttack)
                {
                    tickAttacks++;

                    c.energy -= Config.attackCost * c.GetPheno(PType.MetabolicRate) * c.GetPheno(PType.Lethality);
                    var vec = DirectionToVector[c.facingDirection];
                    int targetX = c.x + vec.dx;
                    int targetY = c.y + vec.dy;

                    if (targetX >= 0 && targetX < width && targetY >= 0 && targetY < height)
                    {
                        Creature target = grid[targetX, targetY].occupant;
                        if (target != null && !target.isDead)
                        {
                            float rDiff = MathF.Abs(c.colorR - target.colorR);
                            float gDiff = MathF.Abs(c.colorG - target.colorG);
                            float bDiff = MathF.Abs(c.colorB - target.colorB);
                            float kinship = 1f - ((rDiff + gDiff + bDiff) / 765f);

                            if (rand.NextDouble() < (c.GetPheno(PType.SocialCohesion) * kinship)) continue;

                            float rawDamage = Config.baseAttackDmg * c.GetPheno(PType.Lethality) * maturation;
                            rawDamage *= (1f - (c.GetPheno(PType.ScavengerTolerance) * 0.8f));

                            float targetArmor = target.GetPheno(PType.ArmorDensity);
                            float finalDamage = rawDamage * (1f - targetArmor);

                            float actualDamage = Math.Min(finalDamage, Math.Max(0, target.energy));

                            target.energy -= finalDamage;
                            c.energy += finalDamage * c.GetPheno(PType.Vampirism);
                            c.energy -= finalDamage * target.GetPheno(PType.SpikeCoating);

                            if (target.energy <= 0)
                            {
                                tickDeaths++;
                                emaLifespan = (emaLifespan * 0.999f) + (target.age * 0.001f);

                                target.isDead = true;
                                grid[targetX, targetY].occupant = null;

                                FoodItem? existingItem = grid[targetX, targetY].foodItem;
                                if (existingItem != null) activeFoods.Remove(existingItem);

                                float targetMaturation = Math.Min(1f, target.age / 50f);
                                float corpseCalories = (target.startingEnergy * Config.meatEntropyMulti) * targetMaturation;

                                var meat = new FoodItem(targetX, targetY, true)
                                {
                                    toxicity = target.GetPheno(PType.ToxicCorpse),
                                    nutrition = corpseCalories * (1f - target.GetPheno(PType.ToxicCorpse) * 0.5f)
                                };

                                grid[targetX, targetY].foodItem = meat;
                                activeFoods.Add(meat);
                                tickEnergyIn += meat.nutrition;
                            }
                        }
                    }
                }

                float parasiteTrait = c.GetPheno(PType.Parasitism);
                if (parasiteTrait > 0.05f)
                {
                    c.energy -= Config.costOfLiving * parasiteTrait * 0.5f;

                    for (int i = 0; i < 8; i++)
                    {
                        var vec = DirectionToVector[i];
                        int cx = c.x + vec.dx;
                        int cy = c.y + vec.dy;
                        if (cx >= 0 && cx < width && cy >= 0 && cy < height)
                        {
                            Creature victim = grid[cx, cy].occupant;
                            if (victim != null && victim != c && !victim.isDead)
                            {
                                float drain = (Config.costOfLiving * 4f) * parasiteTrait;
                                drain = Math.Min(drain, Math.Max(0, victim.energy));

                                victim.energy -= drain;
                                c.energy += drain;
                            }
                        }
                    }
                }

                bool isResting = (c.x == c.lastX && c.y == c.lastY);
                float restFactor = isResting ? (1f / c.GetPheno(PType.RestingEfficiency)) : 1f;

                float tickCost = Config.costOfLiving
                                 * c.GetPheno(PType.MetabolicRate)
                                 * (c.GetPheno(PType.BodyMass) * maturation)
                                 * restFactor
                                 * c.GetPheno(PType.BrainSize)
                                 * (1f + c.GetPheno(PType.VisionAcuity) * 0.1f)
                                 * (1f + c.GetPheno(PType.SpikeCoating) * 0.2f)
                                 * (1f + c.GetPheno(PType.Camouflage) * 0.2f);

                c.energy -= tickCost;

                var currentCell = grid[c.x, c.y];
                if (currentCell.foodItem != null)
                {
                    FoodItem? meal = currentCell.foodItem;
                    if (meal != null)
                    {
                        float rawNutrition = meal.nutrition;
                        bool isMeat = meal.isMeat;
                        float bias = c.GetPheno(PType.CarnivoryBias);

                        float efficiency = isMeat ? bias : (1f - bias);
                        if (isMeat) efficiency *= c.GetPheno(PType.ScavengerTolerance);
                        efficiency *= (1f - c.GetPheno(PType.Vampirism) * 0.8f);
                        float digestionEff = Math.Clamp(c.intentConsume, 0f, 1f);

                        c.energy += rawNutrition * efficiency * digestionEff;

                        float poisonTaken = meal.toxicity * Config.baseNutrition;
                        poisonTaken *= (1f - c.GetPheno(PType.ScavengerTolerance));
                        c.energy -= Math.Max(0f, poisonTaken);

                        if (meal.isMeat) tickMeatsEaten++;
                        else tickPlantsEaten++;

                        activeFoods.Remove(meal);
                        currentCell.foodItem = null;
                    }
                }

                c.energy = Math.Clamp(c.energy, 0f, 3f*c.startingEnergy);

                if (c.energy <= 0)
                {
                    tickDeaths++;
                    emaLifespan = (emaLifespan * 0.999f) + (c.age * 0.001f);

                    c.isDead = true;
                    grid[c.x, c.y].occupant = null;

                    FoodItem? existingItem = grid[c.x, c.y].foodItem;
                    if (existingItem != null)
                    {
                        activeFoods.Remove(existingItem);
                    }

                    float corpseCalories = c.startingEnergy * Config.meatEntropyMulti * maturation;

                    var meat = new FoodItem(c.x, c.y, true)
                    {
                        toxicity = c.GetPheno(PType.ToxicCorpse),
                        nutrition = corpseCalories * (1f - c.GetPheno(PType.ToxicCorpse) * 0.5f)
                    };

                    grid[c.x, c.y].foodItem = meat;
                    activeFoods.Add(meat);

                    tickEnergyIn += meat.nutrition;
                    continue;
                }

                float reqEnergy = c.startingEnergy * c.GetPheno(PType.ReproductionThreshold);
                if (c.energy >= reqEnergy)
                {
                    bool placed = false;
                    int spawnX = c.x, spawnY = c.y;

                    var dirs = Enumerable.Range(0, 8).OrderBy(x => rand.Next()).ToList();
                    foreach (int i in dirs)
                    {
                        var vec = DirectionToVector[i];
                        if (!IsCellObstructed(c.x + vec.dx, c.y + vec.dy))
                        {
                            spawnX = c.x + vec.dx;
                            spawnY = c.y + vec.dy;
                            placed = true;
                            break;
                        }
                    }

                    if (placed)
                    {
                        tickBirths++;

                        if (c.generation >= highestGeneration)
                        {
                            highestGeneration = c.generation;
                            bestGenome = c.genome.Clone();
                        }

                        float investment = c.GetPheno(PType.OffspringInvestment);
                        float childEnergy = c.energy * investment;
                        c.energy -= childEnergy;

                        Genome childGenome = GeneTools.MutateGenome(c.genome.Clone());
                        Creature child = new Creature(spawnX, spawnY, childGenome, this);
                        child.energy = childEnergy;

                        child.generation = c.generation + 1;

                        grid[spawnX, spawnY].occupant = child;
                        newborns.Add(child); // FIX: Safe nursery insertion
                    }
                }

                c.ResetIntents();
            }

            creatures.AddRange(newborns); 
        }

        public bool IsCellObstructed(int x, int y)
        {
            if (x < 0 || x >= width || y < 0 || y >= height) return true;
            return grid[x, y].isBlock || grid[x, y].occupant != null;
        }

        public string GetStateJson()
        {
            float avgBurnPerCreature = emaEnergyOut / Math.Max(1, creatures.Count);
            float mathLifespan = Config.baseStartingEnergy / Math.Max(0.001f, avgBurnPerCreature);

            float avgAge = 0, avgGen = 0, avgEnergy = 0, avgMeatBias = 0, avgArmor = 0, avgLethality = 0, avgGenes = 0;
            float plantEnergy = 0, meatEnergy = 0;
            int herbivores = 0, hunters = 0, scavengers = 0, parasites = 0;
            int maxGen = 0;

            if (creatures.Count > 0)
            {
                foreach (var c in creatures)
                {
                    avgAge += c.age;
                    avgGen += c.generation;
                    if (c.generation > maxGen) maxGen = c.generation;

                    avgEnergy += c.energy;
                    avgMeatBias += c.GetPheno(PType.CarnivoryBias);
                    avgArmor += c.GetPheno(PType.ArmorDensity);
                    avgLethality += c.GetPheno(PType.Lethality);
                    avgGenes += c.genome.genes.Count;

                    if (c.GetPheno(PType.CarnivoryBias) > 0.5f)
                    {
                        if (c.GetPheno(PType.ScavengerTolerance) > 0.5f) scavengers++;
                        else hunters++;
                    }
                    else herbivores++;

                    if (c.GetPheno(PType.Parasitism) > 0.1f) parasites++;
                }

                float count = creatures.Count;
                avgAge /= count;
                avgGen /= count;
                avgEnergy /= count;
                avgMeatBias /= count;
                avgArmor /= count;
                avgLethality /= count;
                avgGenes /= count;
            }

            foreach (var f in activeFoods)
            {
                if (f.isMeat) meatEnergy += f.nutrition;
                else plantEnergy += f.nutrition;
            }

            var payload = new
            {
                type = "petri",
                width = this.width,
                height = this.height,

                stats = new
                {
                    ticks = totalTicks,
                    tps = NEMO.currentTPS,            
                    pop = creatures.Count,
                    extinctions = NEMO.extinctionCount,
                    savedGenomesTotal = NEMO.savedGenomesTotal,
                    savedGenomesSession = NEMO.savedGenomesSession,
                    plants = activeFoods.Count(f => !f.isMeat),
                    meat = activeFoods.Count(f => f.isMeat),

                    eIn = emaEnergyIn,
                    eOut = emaEnergyOut,
                    totalCreatureE = avgEnergy * creatures.Count,
                    totalPlantE = plantEnergy,
                    totalMeatE = meatEnergy,

                    births = emaBirths,
                    deaths = emaDeaths,
                    lifeMeas = emaLifespan,
                    lifeMath = mathLifespan,

                    plantsEaten = emaPlantsEaten,    
                    meatsEaten = emaMeatsEaten,      
                    attacks = emaAttacks,            

                    avgAge = avgAge,
                    avgGen = avgGen,
                    maxGen = maxGen,

                    simLoad = NEMO.emaSimTime,        
                    uiLoad = NEMO.emaUiTime,

                    herbivores = herbivores,
                    hunters = hunters,
                    scavengers = scavengers,
                    parasites = parasites,
                    avgCarnivory = avgMeatBias,
                    avgArmor = avgArmor,
                    avgLethality = avgLethality,
                    avgGenes = avgGenes
                },

                blocks = this.staticBlocks,
                foods = this.activeFoods.Select(f => new ExportFood { x = f.x, y = f.y, meat = f.isMeat }).ToList(),
                creatures = this.creatures.Select(c => new ExportCreature
                {
                    id = c.ID.ToString(),
                    x = c.x,
                    y = c.y,
                    dir = c.facingDirection,
                    r = c.colorR,
                    g = c.colorG,
                    b = c.colorB
                }).ToList()
            };
            return JsonSerializer.Serialize(payload);
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

        public World(int width, int height, List<Genome> genomePool)
        {
            this.width = width;
            this.height = height;

            grid = new Cell[width, height];
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    grid[x, y] = new Cell(x, y);
                }
            }

            creatures = new List<Creature>();

            while (creatures.Count < Config.creatureCount)
            {
                int x = rand.Next(0, width);
                int y = rand.Next(0, height);

                if (!grid[x, y].isBlock && grid[x, y].occupant == null)
                {
                    Genome gen = genomePool.Count > 0 ? genomePool[rand.Next(genomePool.Count)] : GeneTools.GenerateGenome();
                    Creature c = new Creature(x, y, gen, this);
                    creatures.Add(c);
                    grid[x, y].occupant = c;
                }
            }

            int initialFoodTarget = (int)(width * height * Config.foodWorldCoverage);
            for (int i = 0; i < initialFoodTarget; i++)
            {
                int fx = rand.Next(0, width);
                int fy = rand.Next(0, height);
                if (!grid[fx, fy].isBlock && grid[fx, fy].occupant == null && grid[fx, fy].foodItem == null)
                {
                    var plant = new FoodItem(fx, fy, false);
                    grid[fx, fy].foodItem = plant;
                    activeFoods.Add(plant);
                }
            }
        }
    }

    public class Creature
    {
        public readonly Guid ID = Guid.NewGuid();

        public Genome genome;
        public Brain brain;
        public World world;

        #region Initializations
        public int x;
        public int y;
        public int lastX;
        public int lastY;

        //0 north, 1 northeast, 2 east, 3 southeast, 4 south, 5 southwest, 6 west, 7 northwest
        public int facingDirection;
        public int lastFacing;

        public int age = 0;
        public int generation = 0;
        public float energy = Config.baseStartingEnergy;
        public bool isDead = false;

        public float intentMove = 0f;
        public float intentMoveX = 0f;
        public float intentMoveY = 0f;
        public float intentRotate = 0f;
        public float intentConsume = 0f;
        public float intentAttack = 0f;

        public int genomeHash;
        public byte colorR;
        public byte colorG;
        public byte colorB;

        public float startingEnergy;
        #endregion

        public void Update()
        {
            this.brain.UpdateAllNeurons();
        }

        public void ResetIntents()
        {
            intentMove = 0f;
            intentMoveX = 0f;
            intentMoveY = 0f;
            intentRotate = 0f;
            intentConsume = 0f;
            intentAttack = 0f;
        }

        public float GetPheno(PType type) => genome.phenotypes[type].value;

        public Creature(int x, int y, Genome genome, World world)
        {
            this.x = x;
            this.y = y;
            this.lastX = x;
            this.lastY = y;
            this.facingDirection = World.rand.Next(0, 8);
            this.lastFacing = this.facingDirection;
            this.world = world;

            this.genome = genome;
            this.genomeHash = genome.GenerateExactHash();
            var color = genome.GenerateColor();
            this.colorR = color.r;
            this.colorG = color.g;
            this.colorB = color.b;

            this.brain = NeuralTools.GenomeToBrain(genome);
            foreach (Neuron n in brain.neurons){
                n.host = this;
            }

            this.startingEnergy = Config.baseStartingEnergy * GetPheno(PType.BodyMass);
            this.energy = this.startingEnergy * 0.25f;
        }
    }

    public class Cell
    {
        public int x;
        public int y;

        public bool isBlock = false;
        public FoodItem? foodItem = null;
        public Creature? occupant = null;

        public SignalData[] signals = new SignalData[16];

        public Cell(int x, int y)
        {
            this.x = x;
            this.y = y;
        }
    }

    public class FoodItem
    {
        public int x;
        public int y;
        public float nutrition;
        public bool isMeat;
        public float toxicity = 0f;

        public FoodItem(int x, int y, bool isMeat)
        {
            this.x = x;
            this.y = y;
            this.isMeat = isMeat;
            this.nutrition = Config.baseNutrition;
        }
    }

    public struct SignalData
    {
        public float intensity;
        public float decayRate;
    }
}
