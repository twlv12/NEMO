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
            if (activeFoods.Count < Config.worldWidth * Config.worldHeight * Config.foodWorldCoverage)
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
                }
            }
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

                // Scale physical presence by age (fully grown at 50 ticks)
                float maturation = Math.Min(1f, c.age / 50f);

                if (rand.NextDouble() < c.intentAttack)
                {
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

                            float rawDamage = 50f * c.GetPheno(PType.Lethality) * maturation;
                            rawDamage *= (1f - (c.GetPheno(PType.ScavengerTolerance) * 0.8f));

                            float targetArmor = target.GetPheno(PType.ArmorDensity);
                            float finalDamage = rawDamage * (1f - targetArmor);

                            target.energy -= finalDamage;
                            c.energy += finalDamage * c.GetPheno(PType.Vampirism);
                            c.energy -= finalDamage * target.GetPheno(PType.SpikeCoating);

                            if (target.energy <= 0)
                            {
                                target.isDead = true;
                                grid[targetX, targetY].occupant = null;
                                grid[targetX, targetY].foodItem = new FoodItem(targetX, targetY, true)
                                {
                                    toxicity = target.GetPheno(PType.ToxicCorpse)
                                };
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
                                float drain = 2f * parasiteTrait;
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
                        float rawNutrition = currentCell.foodItem.nutrition;
                        bool isMeat = currentCell.foodItem.isMeat;
                        float bias = c.GetPheno(PType.CarnivoryBias);
                        float efficiency = isMeat ? bias : (1f - bias);

                        if (isMeat) efficiency *= c.GetPheno(PType.ScavengerTolerance);

                        efficiency *= (1f - c.GetPheno(PType.Vampirism) * 0.8f);

                        float bonusMultiplier = 1f + Math.Max(0f, c.intentConsume);
                        c.energy += rawNutrition * efficiency * bonusMultiplier;

                        float poisonTaken = currentCell.foodItem.toxicity * Config.baseNutrition;
                        poisonTaken *= (1f - c.GetPheno(PType.ScavengerTolerance));
                        c.energy -= Math.Max(0f, poisonTaken);

                        activeFoods.Remove(currentCell.foodItem);
                        currentCell.foodItem = null;
                    }
                }

                c.energy = Math.Clamp(c.energy, 0f, 3f*c.startingEnergy);

                if (c.energy <= 0)
                {
                    c.isDead = true;
                    grid[c.x, c.y].occupant = null;

                    FoodItem? existingItem = grid[c.x, c.y].foodItem;
                    if (existingItem != null)
                    {
                        activeFoods.Remove(existingItem); // Warning gone!
                    }

                    var meat = new FoodItem(c.x, c.y, true)
                    {
                        toxicity = c.GetPheno(PType.ToxicCorpse),
                        nutrition = Config.baseNutrition * Config.meatNutritionMultiplier * (1f - c.GetPheno(PType.ToxicCorpse) * 0.5f)
                    };
                    grid[c.x, c.y].foodItem = meat;
                    activeFoods.Add(meat);
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
                        float investment = c.GetPheno(PType.OffspringInvestment);
                        float childEnergy = c.energy * investment;
                        c.energy -= childEnergy;

                        Genome childGenome = GeneTools.MutateGenome(c.genome.Clone());
                        Creature child = new Creature(spawnX, spawnY, childGenome, this);
                        child.energy = childEnergy;

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
            var payload = new
            {
                type = "petri",
                width = this.width,
                height = this.height,
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
            if (isMeat)
            {
                nutrition = (int)(Config.baseNutrition * Config.meatNutritionMultiplier);
            }
        }
    }

    public struct SignalData
    {
        public float intensity;
        public float decayRate;
    }
}
