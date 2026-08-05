using System.Collections.Concurrent;
using System.Text.Json;

namespace NEMO
{
    public class World : IWorld
    {
        public Cell[] grid;
        public ICell[] iGrid => grid;
        public float[] fertilityMap;
        public List<Creature> creatures;
        public ConcurrentQueue<Creature> pendingNewborns = new();
        public readonly string runID = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        public static bool isRecording = false;

        public int width { get; set; } = Config.worldWidth;
        public int height { get; set; } = Config.worldHeight;
        public long totalTicks { get; set; } = 0;
        public static Random rand = new Random();
        public float fertOffsetX;
        public float fertOffsetY;
        private int fertUpdateCol = 0;

        #region Telemetry
        public Genome? bestGenome = null;
        public int highestGeneration = 0;
        public float highestSurvivalRatio = 0f;
        public float highestCEQ = 0f;

        public float emaEnergyIn = 0f;
        public float emaEnergyOut = 0f;
        public float emaBirths = 0f;
        public float emaDeaths = 0f;
        public float emaLifespan = 0f;

        public float emaEnergyWasted = 0f;
        public float tickEnergyWasted = 0f;
        public float tickEnergyIn = 0f;
        public int tickBirths = 0;
        public int tickDeaths = 0;

        public float emaPlantsEaten = 0f;
        public float emaMeatsEaten = 0f;
        public float emaAttacks = 0f;
        public float emaKills = 0f;
        public int tickPlantsEaten = 0;
        public int tickMeatsEaten = 0;
        public int tickAttacks = 0;
        public int tickKills = 0;

        public float govDynamicCapacity = 0f;
        public float govCurrentEnergy = 0f;
        public float govBaselineEnergy = 0f;
        public float govActiveLifespan = 0f;
        public float govMathLifespan = 0f;
        public float govBlendFactor = 0f;
        public float govMomentum = 1f;
        public float govWastePenalty = 0f;
        public float govDietFactor = 1f;

        public float CalculateTotalEnergy()
        {
            float total = 0;
            foreach (var c in creatures) total += c.energy;
            lock (activeFoods)
            {
                foreach (var f in activeFoods) total += f.nutrition;
            }
            return total;
        }
        #endregion

        #region Hashes & Caches & Buffers
        public static Genome? defaultGenomeRef = null;
        public HashSet<Cell> activeSignalCells = new HashSet<Cell>();
        public List<FoodItem> activeFoods = new List<FoodItem>();
        public List<ExportBlock> staticBlocks = new List<ExportBlock>();

        private List<Creature> movingCreaturesBuf = new List<Creature>(2000);
        private List<Creature> newbornsBuf = new List<Creature>(500);
        private List<FoodItem> rottedMeatBuf = new List<FoodItem>(500);
        private List<Cell> cellsToClearBuf = new List<Cell>(500);

        public struct ExportFood { public int x { get; set; } public int y { get; set; } public bool meat { get; set; } }
        public struct ExportBlock { public int x { get; set; } public int y { get; set; } }
        public struct ExportCone { public float range { get; set; } public float fov { get; set; } public float offset { get; set; } public int steepness { get; set; } }
        public struct ExportCreature
        {
            public string id { get; set; }
            public float x { get; set; }
            public float y { get; set; }
            public int dir { get; set; }
            public byte r { get; set; }
            public byte g { get; set; }
            public byte b { get; set; }
            public float energy { get; set; }
            public string slot { get; set; }
            public string parentId { get; set; }
            public List<ExportCone> cones { get; set; }
            public int diet { get; set; }
            public float lineage { get; set; }
            public float mass { get; set; }
            public float sr { get; set; }
        }

        private static readonly Comparison<Creature> CreatureMoveComparer = (a, b) =>
        {
            float valA = a.intentMove * a.phenoCache[(int)PType.BodyMass];
            float valB = b.intentMove * b.phenoCache[(int)PType.BodyMass];
            return valB.CompareTo(valA);
        };
        #endregion

        public void Update()
        {
            #region Stat Resets
            totalTicks++;
            tickEnergyIn = 0f;
            tickBirths = 0;
            tickDeaths = 0;
            tickPlantsEaten = 0;
            tickMeatsEaten = 0;
            tickAttacks = 0;
            tickKills = 0;
            #endregion

            while (pendingNewborns.TryDequeue(out var pending))
                creatures.Add(pending);

            float preUpdateEnergy = CalculateTotalEnergy();

            rottedMeatBuf.Clear();
            foreach (var f in activeFoods)
            {
                if (f.isMeat)
                {
                    f.nutrition -= NEMO.disableEnergyDrain ? 0f : Config.meatDecayRate;
                    if (f.nutrition <= 0) rottedMeatBuf.Add(f);
                }
                else
                {
                    if (fertilityMap[f.x + (f.y * width)] < Config.plantCutoff)
                    {
                        f.nutrition -= NEMO.disableEnergyDrain ? 0f : (Config.meatDecayRate * 0.15f);
                        if (f.nutrition <= 0) rottedMeatBuf.Add(f);
                    }
                }
            }
            foreach (var r in rottedMeatBuf)
            {
                grid[r.x + (r.y * width)].foodItem = null;
                activeFoods.Remove(r);
            }

            int currentPlants = activeFoods.Count(f => !f.isMeat);

            if (fertUpdateCol == 0)
            {
                fertOffsetX += 0.05f * Config.migrationSpeed;
                fertOffsetY += 0.05f * Config.migrationSpeed;
            }
            for (int y = 0; y < height; y++)
            {
                if (!grid[fertUpdateCol + (y * width)].isOasis)
                {
                    float fnx = (fertUpdateCol * Config.plantFrequency) / 60f + fertOffsetX;
                    float fny = (y * Config.plantFrequency) / 60f + fertOffsetY;
                    this.fertilityMap[fertUpdateCol + (y * width)] = MathfPerlin(fnx, fny);
                }
            }
            fertUpdateCol = (fertUpdateCol + 1) % width;

            DecaySignals();

            System.Threading.Tasks.Parallel.ForEach(Partitioner.Create(0, creatures.Count), range =>
            {
                for (int i = range.Item1; i < range.Item2; i++)
                {
                    if (!creatures[i].isDead) creatures[i].Update();
                }
            });

            ResolveIntents();

            creatures.RemoveAll(c => c.isDead);

            if (!NEMO.disableGovernor && creatures.Count < Config.creatureCount && Config.maintainPopulation)
            {
                int x = rand.Next(0, width);
                int y = rand.Next(0, height);

                if (!grid[x + (y * width)].isBlock && grid[x + (y * width)].occupant == null)
                {
                    Creature c = new Creature(x, y, GeneTools.GenerateGenome(), this);
                    creatures.Add(c);
                    grid[x + (y * width)].occupant = c;

                    tickEnergyIn += c.startingEnergy * 0.25f;
                }
            }

            #region Governor Calculations
            float currentSystemEnergy = CalculateTotalEnergy();
            float tickEnergyOut = (preUpdateEnergy + tickEnergyIn) - currentSystemEnergy;

            float sumCarnivory = 0f;
            long sumGen = 0;
            for (int i = 0; i < creatures.Count; i++)
            {
                sumCarnivory += creatures[i].GetPheno(PType.CarnivoryBias);
                sumGen += creatures[i].generation;
            }

            float avgCarnivory = creatures.Count > 0 ? sumCarnivory / creatures.Count : 0.5f;
            float avgGen = creatures.Count > 0 ? (float)sumGen / creatures.Count : 0f;

            float avgBurnPerTick = emaEnergyOut / Math.Max(1f, creatures.Count);
            float mathLifespan = Math.Min(5000f, Config.baseStartingEnergy / Math.Max(0.001f, avgBurnPerTick));

            float blendFactor = Math.Clamp(avgGen / 25.0f, 0f, 1f);
            float activeLifespan = (mathLifespan * (1f - blendFactor)) + (Math.Max(50f, emaLifespan) * blendFactor);

            float wasteRatio = Math.Min(1.0f, emaEnergyWasted / Math.Max(0.001f, emaEnergyOut));
            float wastePenalty = wasteRatio * Config.wastePenaltyMultiplier * blendFactor;

            float dietFactor = 1.0f - (avgCarnivory * 0.8f);

            float baselineEnergy = Config.creatureCount * Config.baseStartingEnergy * Config.globalEnergyMultiplier;
            float dynamicCapacity = baselineEnergy * Math.Max(0.5f, 1.0f - wastePenalty) * dietFactor;

            this.govDynamicCapacity = dynamicCapacity;
            this.govCurrentEnergy = currentSystemEnergy;
            this.govBaselineEnergy = baselineEnergy;
            this.govActiveLifespan = activeLifespan;
            this.govMathLifespan = mathLifespan;
            this.govBlendFactor = blendFactor;
            this.govWastePenalty = wastePenalty;
            this.govDietFactor = dietFactor;
            #endregion

            if (!NEMO.disableGovernor)
            {
                int maxIntervention = Math.Max(1, (int)(Config.creatureCount * Config.governorStrength * 0.05));

                if (currentSystemEnergy < dynamicCapacity)
                {
                    float deficit = dynamicCapacity - currentSystemEnergy;
                    int plantsToSpawn = (int)(deficit / Config.baseNutrition);

                    plantsToSpawn = Math.Min(plantsToSpawn, maxIntervention);

                    for (int i = 0; i < plantsToSpawn; i++)
                    {
                        int fx = World.rand.Next(width);
                        int fy = World.rand.Next(height);

                        if (!grid[fx + (fy * width)].isBlock && grid[fx + (fy * width)].occupant == null && grid[fx + (fy * width)].foodItem == null)
                        {
                            bool isFertileZone = fertilityMap[fx + (fy * width)] > Config.plantCutoff;
                            bool isWildSprout = World.rand.NextDouble() < Config.lingeringPlants;

                            if (isFertileZone || isWildSprout)
                            {
                                var plant = new FoodItem(fx, fy, false);
                                grid[fx + (fy * width)].foodItem = plant;
                                activeFoods.Add(plant);
                                tickEnergyIn += Config.baseNutrition;
                            }
                        }
                    }
                }
                else if (currentSystemEnergy > dynamicCapacity)
                {
                    float excess = currentSystemEnergy - dynamicCapacity;
                    int plantsToWilt = (int)(excess / Config.baseNutrition);

                    plantsToWilt = Math.Min(plantsToWilt, maxIntervention);

                    int wilted = 0;
                    for (int i = activeFoods.Count - 1; i >= 0 && wilted < plantsToWilt; i--)
                    {
                        if (!activeFoods[i].isMeat)
                        {
                            grid[activeFoods[i].x + (activeFoods[i].y * width)].foodItem = null;
                            activeFoods.RemoveAt(i);
                            wilted++;
                        }
                    }
                }
            }

            #region Stat EMA Calculations
            float alpha = 0.01f;
            emaEnergyIn = (emaEnergyIn * (1f - alpha)) + (tickEnergyIn * alpha);
            emaEnergyOut = (emaEnergyOut * (1f - alpha)) + (tickEnergyOut * alpha);
            emaEnergyWasted = (emaEnergyWasted * (1f - alpha)) + (tickEnergyWasted * alpha);
            emaBirths = (emaBirths * (1f - alpha)) + (tickBirths * alpha);
            emaDeaths = (emaDeaths * (1f - alpha)) + (tickDeaths * alpha);
            emaPlantsEaten = (emaPlantsEaten * (1f - alpha)) + (tickPlantsEaten * alpha);
            emaMeatsEaten = (emaMeatsEaten * (1f - alpha)) + (tickMeatsEaten * alpha);
            emaAttacks = (emaAttacks * (1f - alpha)) + (tickAttacks * alpha);
            emaKills = (emaKills * (1f - alpha)) + (tickKills * alpha);
            #endregion
        }

        public void DecaySignals()
        {
            cellsToClearBuf.Clear();
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
                        cell.signals[c] = default;
                    }
                }
                if (!hasActiveSignal) cellsToClearBuf.Add(cell);
            }
            foreach (var cell in cellsToClearBuf) activeSignalCells.Remove(cell);
        }

        private void ResolveIntents()   
        {
            movingCreaturesBuf.Clear();
            newbornsBuf.Clear();

            for (int i = 0; i < creatures.Count; i++)
            {
                var c = creatures[i];
                if (c.isDead) continue;
                c.age++;
                EvaluateSR(c);

                c.lastX = c.x;
                c.lastY = c.y;
                c.lastFacing = c.facingDirection;
                c.ticksSinceLastMove++;
                if (c.gestationTimer > 0) c.gestationTimer--;

                if (Math.Abs(c.intentRotate) > 0.01f && rand.NextDouble() < Math.Abs(c.intentRotate))
                {
                    if (c.intentRotate > 0) c.facingDirection = (c.facingDirection + 1) % 8;
                    else c.facingDirection = (c.facingDirection + 7) % 8;

                    float rotCost = NEMO.disableEnergyDrain ? 0f :
                        Config.movementCost * 0.25f * c.GetPheno(PType.BodyMass) * (1f / c.GetPheno(PType.RotationalAgility));

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
                    movingCreaturesBuf.Add(c);
                }
            }

            movingCreaturesBuf.Sort(CreatureMoveComparer);

            foreach (var c in movingCreaturesBuf)
            {
                int targetX = c.x + (int)c.intentMoveX;
                int targetY = c.y + (int)c.intentMoveY;

                bool outOfBounds = targetX < 0 || targetX >= width || targetY < 0 || targetY >= height;
                bool hitBlock = !outOfBounds && grid[targetX + (targetY * width)].isBlock;

                if (!outOfBounds && !hitBlock)
                {
                    Creature targetOccupant = grid[targetX + (targetY * width)].occupant;

                    if (targetOccupant == null)
                    {
                        if (!NEMO.disableEnergyDrain)
                        {
                            float massMoveFactor = MathF.Pow(Math.Max(0.1f, c.GetPheno(PType.BodyMass)), 0.5f);

                            c.energy -= Config.movementCost *
                                massMoveFactor *
                                c.GetPheno(PType.FastTwitchMuscle) *
                                (1f + c.GetPheno(PType.ArmorDensity)) *
                                (1f + c.GetPheno(PType.RotationalAgility) * 0.2f)
                                * Math.Max(0.5f, c.GetPheno(PType.RestingEfficiency));
                        }

                        grid[c.x + (c.y * width)].occupant = null;
                        c.x = targetX;
                        c.y = targetY;
                        grid[targetX + (targetY * width)].occupant = c;
                    }
                    else if (targetOccupant != c)
                    {
                        float myMass = c.GetPheno(PType.BodyMass);
                        float theirMass = targetOccupant.GetPheno(PType.BodyMass);

                        if (myMass > theirMass * 1.25f && c.intentMove > 0.5f)
                        {
                            if (!NEMO.disableEnergyDrain)
                            {
                                c.energy -= Config.movementCost * myMass * 1.5f;

                                float trampleDmg = Config.wallCollisionDmg * c.intentMove * (myMass / theirMass);
                                targetOccupant.energy -= trampleDmg * (1f - targetOccupant.GetPheno(PType.ArmorDensity));
                            }

                            grid[c.x + (c.y * width)].occupant = targetOccupant;
                            targetOccupant.x = c.x;
                            targetOccupant.y = c.y;
                            targetOccupant.lastX = c.x; 
                            targetOccupant.lastY = c.y;

                            c.x = targetX;
                            c.y = targetY;
                            grid[targetX + (targetY * width)].occupant = c;
                        }
                        else
                        {
                            c.facingDirection = (c.facingDirection + 4) % 8;

                            float kineticDamage = Config.wallCollisionDmg * c.intentMove * c.GetPheno(PType.BodyMass);
                            if (!NEMO.disableEnergyDrain)
                            {
                                c.energy -= kineticDamage;
                                tickEnergyWasted += kineticDamage;
                            }

                            c.intentMoveX = 0;
                            c.intentMoveY = 0;
                            c.intentMove = 0;
                        }
                    }
                }
                else if (outOfBounds || hitBlock)
                {
                    int dx = (int)c.intentMoveX;
                    int dy = (int)c.intentMoveY;

                    bool blockedX = c.x + dx < 0 || c.x + dx >= width || grid[(c.x + dx) + (c.y * width)].isBlock;
                    bool blockedY = c.y + dy < 0 || c.y + dy >= height || grid[c.x + ((c.y + dy) * width)].isBlock;

                    if (blockedX) dx = -dx;
                    if (blockedY) dy = -dy;

                    for (int d = 0; d < 8; d++)
                    {
                        if (DirectionToVector[d].dx == dx && DirectionToVector[d].dy == dy)
                        {
                            c.facingDirection = d;
                            break;
                        }
                    }

                    float kineticDamage = Config.wallCollisionDmg * c.intentMove * c.GetPheno(PType.BodyMass);
                    if (!NEMO.disableEnergyDrain)
                    {
                        c.energy -= kineticDamage;
                        tickEnergyWasted += kineticDamage;
                    }

                    c.intentMoveX = 0;
                    c.intentMoveY = 0;
                    c.intentMove = 0;
                }
                else
                {
                    c.intentMoveX = 0;
                    c.intentMoveY = 0;
                    c.intentMove = 0;
                }
            }

            foreach (var c in creatures)
            {
                if (c.isDead) continue;

                float maturation = Math.Min(1f, c.age / 50f);

                if (rand.NextDouble() < c.intentAttack)
                {
                    tickAttacks++;
                    c.totalActionAttempts++;

                    c.energy -= NEMO.disableEnergyDrain ? 0f
                        : Config.attackCost * c.GetPheno(PType.MetabolicRate) * c.GetPheno(PType.Lethality);
                    var vec = DirectionToVector[c.facingDirection];
                    int targetX = c.x + vec.dx;
                    int targetY = c.y + vec.dy;

                    if (targetX >= 0 && targetX < width && targetY >= 0 && targetY < height)
                    {
                        Creature target = grid[targetX + (targetY * width)].occupant;
                        if (target != null && !target.isDead)
                        {
                            float rDiff = MathF.Abs(c.colorR - target.colorR);
                            float gDiff = MathF.Abs(c.colorG - target.colorG);
                            float bDiff = MathF.Abs(c.colorB - target.colorB);
                            float kinship = 1f - ((rDiff + gDiff + bDiff) / 765f);

                            if (rand.NextDouble() < (c.GetPheno(PType.SocialCohesion) * kinship)) continue;
                            c.successfulActions++;

                            float rawDamage = Config.baseAttackDmg * c.GetPheno(PType.Lethality) * maturation;
                            rawDamage *= (1f - (c.GetPheno(PType.ScavengerTolerance) * 0.8f));

                            float attackerMass = Math.Max(0.1f, c.GetPheno(PType.BodyMass));
                            float targetMass = Math.Max(0.1f, target.GetPheno(PType.BodyMass));

                            float massAdvantage = attackerMass / targetMass;

                            float targetArmor = target.GetPheno(PType.ArmorDensity);
                            float finalDamage = rawDamage * massAdvantage * (1f - targetArmor);

                            float deathThreshold = target.startingEnergy * Config.deathEnergy;
                            float maxDrain = Math.Max(0, target.energy - deathThreshold);
                            float actualDamage = Math.Min(finalDamage, maxDrain);

                            target.energy -= actualDamage;
                            c.damageDealt += actualDamage;

                            float rawEfficiency = MathF.Sqrt(c.GetPheno(PType.CarnivoryBias));
                            float biteEfficiency = rawEfficiency * (0.5f + (c.GetPheno(PType.ScavengerTolerance) * 0.5f));

                            float caloriesAbsorbed = actualDamage * Config.meatEntropyMulti * biteEfficiency;

                            c.energy += caloriesAbsorbed;
                            c.energy -= actualDamage * target.GetPheno(PType.SpikeCoating);

                            tickMeatsEaten++;
                            c.meatsEaten++;

                            if (target.energy <= target.startingEnergy * Config.deathEnergy)
                            {
                                tickDeaths++;
                                tickKills++;
                                c.kills++;
                                emaLifespan = (emaLifespan * 0.999f) + (target.age * 0.001f);

                                target.isDead = true;
                                grid[targetX + (targetY * width)].occupant = null;

                                FoodItem? existingItem = grid[targetX + (targetY * width)].foodItem;
                                if (existingItem != null) activeFoods.Remove(existingItem);

                                float totalCorpseEnergy = Math.Min(Math.Max(0, target.energy), deathThreshold);
                                float corpseCalories = totalCorpseEnergy * Config.meatEntropyMulti;

                                var meat = new FoodItem(targetX, targetY, true)
                                {
                                    toxicity = target.GetPheno(PType.ToxicCorpse),
                                    nutrition = corpseCalories * (1f - target.GetPheno(PType.ToxicCorpse) * 0.5f)
                                };

                                grid[targetX + (targetY * width)].foodItem = meat;
                                activeFoods.Add(meat);
                            }
                        }
                        else
                        {
                            tickEnergyWasted += Config.attackCost * c.GetPheno(PType.MetabolicRate) * c.GetPheno(PType.Lethality);
                        }
                    }
                    else
                    {
                        tickEnergyWasted += Config.attackCost * c.GetPheno(PType.MetabolicRate) * c.GetPheno(PType.Lethality);
                    }
                }

                float parasiteTrait = c.GetPheno(PType.Parasitism);
                if (parasiteTrait > 0.05f)
                {
                    float somaticTax = NEMO.disableEnergyDrain ? 0f : Config.costOfLiving * parasiteTrait * c.GetPheno(PType.BodyMass) * Config.paraSomaticTax;
                    c.energy -= somaticTax;

                    for (int i = 0; i < 8; i++)
                    {
                        var vec = DirectionToVector[i];
                        int cx = c.x + vec.dx;
                        int cy = c.y + vec.dy;
                        if (cx >= 0 && cx < width && cy >= 0 && cy < height)
                        {
                            Creature victim = grid[cx + (cy * width)].occupant;
                            if (victim != null && victim != c && !victim.isDead)
                            {
                                float healthRatio = Math.Clamp(victim.energy / victim.startingEnergy, 0f, 1f);
                                float vulnerability = Math.Max(0.1f, 1f - healthRatio);

                                float hostArmor = victim.GetPheno(PType.ArmorDensity);
                                float hostSpikes = victim.GetPheno(PType.SpikeCoating);

                                float latchCost = (Config.costOfLiving * parasiteTrait) * (0.5f + (hostSpikes * 2f));
                                c.energy -= latchCost;

                                float attemptedDrain = (Config.costOfLiving * Config.paraDrainPower) * parasiteTrait * vulnerability;
                                attemptedDrain *= (1f - hostArmor);

                                float actualDrain = Math.Min(attemptedDrain, Math.Max(0, victim.energy));

                                victim.energy -= actualDrain;
                                float parasiteAbsorbed = actualDrain * Config.paraEntropyMulti;
                                c.energy += parasiteAbsorbed;
                            }
                        }
                    }
                }

                float symbiosisTrait = c.GetPheno(PType.Symbiosis);
                if (symbiosisTrait > 0.05f)
                {
                    c.energy -= Config.costOfLiving * symbiosisTrait * 0.25f;

                    for (int i = 0; i < 8; i++)
                    {
                        var vec = DirectionToVector[i];
                        int cx = c.x + vec.dx;
                        int cy = c.y + vec.dy;
                        if (cx >= 0 && cx < width && cy >= 0 && cy < height)
                        {
                            Creature neighbor = grid[cx + (cy * width)].occupant;
                            if (neighbor != null && neighbor != c && !neighbor.isDead)
                            {
                                float neighborSymbiosis = neighbor.GetPheno(PType.Symbiosis);
                                if (neighborSymbiosis > 0.05f)
                                {
                                    float rDiff = MathF.Abs(c.colorR - neighbor.colorR);
                                    float gDiff = MathF.Abs(c.colorG - neighbor.colorG);
                                    float bDiff = MathF.Abs(c.colorB - neighbor.colorB);
                                    float kinship = 1f - ((rDiff + gDiff + bDiff) / 765f);

                                    if (kinship > Config.selectKinshipThreshold)
                                    {
                                        if (c.energy > neighbor.energy)
                                        {
                                            float difference = c.energy - neighbor.energy;

                                            float transferRate = Math.Min(symbiosisTrait, neighborSymbiosis) * 0.5f;
                                            float amountToShare = difference * transferRate;

                                            c.energy -= amountToShare;
                                            neighbor.energy += amountToShare;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                bool isResting = (c.x == c.lastX && c.y == c.lastY);
                float restFactor = isResting ? (1f / c.GetPheno(PType.RestingEfficiency)) : 1f;

                float tickCost = NEMO.disableEnergyDrain ? 0f : (c.GetBaseTickCost() * maturation * restFactor);

                c.energy -= tickCost;

                float deathE = c.startingEnergy * Config.deathEnergy;
                float reproE = c.startingEnergy * c.GetPheno(PType.ReproductionThreshold);
                float targetMaxE = c.gestationTimer > 0
                    ? reproE * 0.95f
                    : reproE + (c.startingEnergy * 0.2f);

                float energyRange = targetMaxE - deathE;
                float fullness = Math.Clamp((c.energy - deathE) / Math.Max(0.001f, energyRange), 0f, 1f);
                float appetite = 1f - fullness;

                var currentCell = grid[c.x + (c.y * width)];
                if (currentCell.foodItem != null && appetite > 0.05f)
                {
                    FoodItem meal = currentCell.foodItem;

                    float stomachCap = c.GetPheno(PType.BodyMass) * Config.tickStomachCapacity;
                    float attemptedBite = Math.Min(meal.nutrition, stomachCap) * appetite;

                    float rawEfficiency = meal.isMeat ? MathF.Sqrt(c.GetPheno(PType.CarnivoryBias)) : MathF.Sqrt(1f - c.GetPheno(PType.CarnivoryBias));
                    if (meal.isMeat)
                    {
                        rawEfficiency *= (0.5f + (c.GetPheno(PType.ScavengerTolerance) * 0.5f));
                    }

                    float gutDigestionFactor = meal.isMeat ? 1f : Math.Clamp(c.GetPheno(PType.BodyMass) / 1.5f, 0.2f, 1f);
                    float efficiency = rawEfficiency * gutDigestionFactor;

                    c.energy += attemptedBite * efficiency;

                    float poisonTaken = (attemptedBite / Config.baseNutrition) * meal.toxicity * Config.baseNutrition * (1f - c.GetPheno(PType.ScavengerTolerance));
                    c.energy -= Math.Max(0f, poisonTaken);

                    meal.nutrition -= attemptedBite;

                    if (meal.nutrition <= 5f)
                    {
                        if (meal.isMeat) { tickMeatsEaten++; c.meatsEaten++; }
                        else { tickPlantsEaten++; c.plantsEaten++; }

                        activeFoods.Remove(meal);
                        currentCell.foodItem = null;
                    }
                }

                c.intentConsume *= appetite;

                if (c.intentConsume > 0.1f)
                {
                    c.totalActionAttempts++;

                    float consumeCost = Config.costOfLiving * c.intentConsume;
                    c.energy -= NEMO.disableEnergyDrain ? 0f : consumeCost;
                    bool ateSomething = false;

                    float remainingBiteCapacity = c.GetPheno(PType.BodyMass) * Config.tickStomachCapacity * c.intentConsume;

                    for (int dx = -1; dx <= 1 && remainingBiteCapacity > 1f; dx++)
                    {
                        for (int dy = -1; dy <= 1 && remainingBiteCapacity > 1f; dy++)
                        {
                            if (dx == 0 && dy == 0) continue;

                            int cx = c.x + dx;
                            int cy = c.y + dy;

                            if (cx >= 0 && cx < width && cy >= 0 && cy < height)
                            {
                                FoodItem? adjMeal = grid[cx + (cy * width)].foodItem;
                                if (adjMeal != null)
                                {
                                    float bite = Math.Min(adjMeal.nutrition, remainingBiteCapacity);

                                    float rawEfficiency = adjMeal.isMeat ? MathF.Sqrt(c.GetPheno(PType.CarnivoryBias)) : MathF.Sqrt(1f - c.GetPheno(PType.CarnivoryBias));
                                    if (adjMeal.isMeat)
                                    {
                                        rawEfficiency *= (0.5f + (c.GetPheno(PType.ScavengerTolerance) * 0.5f));
                                    }

                                    float gutDigestionFactor = adjMeal.isMeat ? 1.0f : Math.Clamp(c.GetPheno(PType.BodyMass) / 1.5f, 0.2f, 1.2f);
                                    float efficiency = rawEfficiency * gutDigestionFactor;

                                    c.energy += bite * efficiency;

                                    float poisonTaken = (bite / Config.baseNutrition) * adjMeal.toxicity * Config.baseNutrition * (1f - c.GetPheno(PType.ScavengerTolerance));
                                    c.energy -= Math.Max(0f, poisonTaken);

                                    adjMeal.nutrition -= bite;
                                    remainingBiteCapacity -= bite;
                                    ateSomething = true;

                                    if (adjMeal.nutrition <= 5f)
                                    {
                                        if (adjMeal.isMeat) { tickMeatsEaten++; c.meatsEaten++; }
                                        else { tickPlantsEaten++; c.plantsEaten++; }

                                        activeFoods.Remove(adjMeal);
                                        grid[cx + (cy * width)].foodItem = null;
                                    }
                                }
                            }
                        }
                    }

                    if (!ateSomething) tickEnergyWasted += consumeCost;
                    else
                    {
                        if (c.lastFoodX != -1)
                        {
                            float dx = Math.Abs(c.x - c.lastFoodX);
                            float dy = Math.Abs(c.y - c.lastFoodY);
                            float displacement = MathF.Sqrt((dx * dx) + (dy * dy));

                            if (displacement > 1.5f)
                            {
                                float intentionalDistance = Math.Min(displacement, 15f);
                                float normalizedDistance = intentionalDistance / 15f;

                                float weightedEfficiency = normalizedDistance * (displacement / Math.Max(1f, c.ticksSinceLastMove));
                                c.totalPathEfficiencySum += weightedEfficiency;
                            }
                        }

                        c.lastFoodX = c.x;
                        c.lastFoodY = c.y;
                        c.successfulActions++;
                        c.foodItemsEaten++;
                        c.ticksSinceLastMove = 0;
                    }
                }

                c.energy = Math.Clamp(c.energy, 0f, 3f * c.startingEnergy);

                if (c.energy <= c.startingEnergy * Config.deathEnergy)
                {
                    tickDeaths++;
                    emaLifespan = (emaLifespan * 0.999f) + (c.age * 0.001f);

                    c.isDead = true;
                    grid[c.x + (c.y * width)].occupant = null;

                    FoodItem? existingItem = grid[c.x + (c.y * width)].foodItem;
                    if (existingItem != null)
                    {
                        activeFoods.Remove(existingItem);
                    }

                    float deathThreshold = c.startingEnergy * Config.deathEnergy;
                    float totalCorpseEnergy = Math.Min(Math.Max(0, c.energy), deathThreshold);
                    float corpseCalories = totalCorpseEnergy * Config.meatEntropyMulti;

                    var meat = new FoodItem(c.x, c.y, true)
                    {
                        toxicity = c.GetPheno(PType.ToxicCorpse),
                        nutrition = corpseCalories * (1f - c.GetPheno(PType.ToxicCorpse) * 0.5f)
                    };

                    grid[c.x + (c.y * width)].foodItem = meat;
                    activeFoods.Add(meat);

                    continue;
                }

                if (c.intentSignalChannel >= 0 && c.intentSignalIntensity > 0)
                {
                    float volume = c.GetPheno(PType.PheromoneVolume);
                    grid[c.x + (c.y * width)].signals[c.intentSignalChannel].intensity += c.intentSignalIntensity * volume;
                    c.energy -= c.intentSignalIntensity * volume * 0.5f;

                    activeSignalCells.Add(grid[c.x + (c.y * width)]);

                    float mappedDecay = 0.2f + 0.797f * (1f - MathF.Pow(1f - c.intentSignalDecay, 3));
                    mappedDecay *= (1f / c.GetPheno(PType.ChemicalVolatility));

                    grid[c.x + (c.y * width)].signals[c.intentSignalChannel].decayRate = Math.Clamp(mappedDecay, 0.1f, 0.999f);
                }

                float reqEnergy = c.startingEnergy * c.GetPheno(PType.ReproductionThreshold);
                if (c.energy >= reqEnergy && c.genome.genes.Count > 0 && !NEMO.disableGovernor && c.gestationTimer <= 0)
                {
                    bool placed = false;
                    int spawnX = c.x, spawnY = c.y;

                    int startDir = World.rand.Next(8);
                    for (int i = 0; i < 8; i++)
                    {
                        var vec = DirectionToVector[(startDir + i) % 8];
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
                        c.gestationTimer = (int)(Config.maturationTime * c.GetPheno(PType.GestationPeriod) * Config.gestationPeriod);

                        float investment = c.GetPheno(PType.OffspringInvestment);
                        float investedEnergy = c.energy * investment;

                        c.energy -= investedEnergy;

                        Genome childGenome = GeneTools.MutateGenome(c.genome.Clone());
                        Creature child = new Creature(spawnX, spawnY, childGenome, this);

                        child.energy = investedEnergy * Config.birthEfficiency;
                        child.gestationTimer = (int)(Config.maturationTime * child.GetPheno(PType.GestationPeriod) * Config.gestationPeriod);

                        child.generation = c.generation + 1;
                        child.lineageLifespan = (c.lineageLifespan == 0f) ? c.age : (c.lineageLifespan * 0.8f) + (c.age * 0.2f);
                        child.parentID = c.ID.ToString();

                        grid[spawnX + (spawnY * width)].occupant = child;
                        newbornsBuf.Add(child);
                    }
                }

                if (c.intentAttack > 0.1f) c.currentAction = "Attacking";
                else if (c.intentConsume > 0.1f) c.currentAction = "Eating";
                else if (c.intentMove > 0.1f) c.currentAction = "Moving";
                else if (c.intentRotate > 0.1f) c.currentAction = "Turning";
                else c.currentAction = "Idle";

                c.ResetIntents();
            }

            creatures.AddRange(newbornsBuf);
        }

        public float EvaluateSR(Creature c)
        {
            float specificMathLifespan = c.startingEnergy / Math.Max(0.001f, c.GetBaseTickCost());
            float effectiveAge = (c.age * 0.5f) + (c.lineageLifespan * 0.5f);

            float survivalRatio = effectiveAge / Math.Max(1f, specificMathLifespan);
            c.survivalRatio = survivalRatio;

            if (survivalRatio > highestSurvivalRatio)
            {
                highestSurvivalRatio = survivalRatio;
                highestGeneration = c.generation;

                if (bestGenome == null || bestGenome.GenerateExactHash() != c.genomeHash)
                {
                    bestGenome = c.genome.Clone();
                }
            }

            float ceq = c.EvaluateCEQ(survivalRatio);
            if (ceq > highestCEQ)
            {
                highestCEQ = ceq;
            }

            return survivalRatio;
        }

        public bool IsCellObstructed(int x, int y)
        {
            if (x < 0 || x >= width || y < 0 || y >= height) return true;
            return grid[x + (y * width)].isBlock || grid[x + (y * width)].occupant != null;
        }

        public string GetStateJson(Creature[] creaturesSnap, FoodItem[] foodsSnap, ExportBlock[] blocksSnap)
        {
            float avgBurnPerCreature = emaEnergyOut / Math.Max(1f, Config.creatureCount);
            float mathLifespan = Config.baseStartingEnergy / Math.Max(0.001f, avgBurnPerCreature);

            float avgAge = 0, avgGen = 0, avgEnergy = 0, avgMeatBias = 0, avgGenes = 0;
            float plantEnergy = 0, meatEnergy = 0;
            int herbivores = 0, hunters = 0, scavengers = 0, parasites = 0, omnivores = 0;
            int maxGen = 0;

            if (creaturesSnap.Length > 0)
            {
                foreach (var c in creaturesSnap)
                {
                    avgAge += c.age;
                    avgGen += c.generation;
                    if (c.generation > maxGen) maxGen = c.generation;

                    avgEnergy += c.energy;
                    avgMeatBias += c.GetPheno(PType.CarnivoryBias);
                    avgGenes += c.genome.genes.Count;

                    float carnivory = c.GetPheno(PType.CarnivoryBias);
                    float parasitism = c.GetPheno(PType.Parasitism);
                    float scavenger = c.GetPheno(PType.ScavengerTolerance);

                    if (parasitism > 0.2f)
                    {
                        parasites++;
                    }
                    else if (carnivory > 0.65f)
                    {
                        if (scavenger > 0.5f) scavengers++;
                        else hunters++;
                    }
                    else if (carnivory < 0.35f)
                    {
                        herbivores++;
                    }
                    else
                    {
                        omnivores++;
                    }
                }

                float count = creaturesSnap.Length;
                avgAge /= count;
                avgGen /= count;
                avgEnergy /= count;
                avgMeatBias /= count;
                avgGenes /= count;
            }

            foreach (var f in foodsSnap)
            {
                if (f.isMeat) meatEnergy += f.nutrition;
                else plantEnergy += f.nutrition;
            }

            object trackedInfoObj = null;
            if (!string.IsNullOrEmpty(NEMO.trackedCreatureId))
            {
                var tc = creaturesSnap.FirstOrDefault(c => c.ID.ToString() == NEMO.trackedCreatureId);
                if (tc != null)
                {
                    string currentAction = tc.currentAction;

                    float carnivory = tc.GetPheno(PType.CarnivoryBias);
                    float parasitism = tc.GetPheno(PType.Parasitism);
                    float scavenger = tc.GetPheno(PType.ScavengerTolerance);
                    string dietType = "Omnivore";
                    if (parasitism > 0.2f)
                    {
                        dietType = "Parasite";
                        if (carnivory > 0.65f) dietType = "Carnivorous Parasite";
                        else if (carnivory < 0.35f) dietType = "Herbivorous Parasite";
                    }
                    else if (carnivory > 0.65f) dietType = scavenger > 0.5f ? "Scavenger" : "Predator";
                    else if (carnivory < 0.35f) dietType = "Herbivore";

                    int sensors = tc.brain.neurons.Count(n => n.type == NType.Sensor);
                    int maths = tc.brain.neurons.Count(n => n.type == NType.Math);
                    int actions = tc.brain.neurons.Count(n => n.type == NType.Action);
                    int activeConnections = tc.brain.connections.Count(c => c.src != null && c.tgt != null);

                    if (defaultGenomeRef == null)
                    {
                        defaultGenomeRef = new Genome(new List<Gene>());
                        defaultGenomeRef.InitializeDefaultPhenotypes();
                    }

                    var topPhenos = tc.genome.phenotypes
                        .Select(kvp => {
                            float def = defaultGenomeRef.phenotypes[kvp.Key].value;
                            float cur = kvp.Value.value;
                            float diff = cur - def;
                            return new { name = kvp.Key.ToString(), val = cur, diff = diff };
                        })
                        .OrderByDescending(x => Math.Abs(x.diff))
                        .Take(4)
                        .ToList();

                    trackedInfoObj = new
                    {
                        id = tc.ID.ToString(),
                        age = tc.age,
                        gen = tc.generation,
                        survivalRatio = tc.survivalRatio,
                        ceq = tc.ceqScore,
                        bodyMass = tc.GetPheno(PType.BodyMass),
                        lineage = tc.lineageLifespan,
                        energy = tc.energy,
                        kills = tc.kills,
                        damageDealt = tc.damageDealt,
                        meatsEaten = tc.meatsEaten,
                        plantsEaten = tc.plantsEaten,
                        energyPct = Math.Clamp(tc.energy / tc.startingEnergy, 0f, 1f),
                        action = currentAction,
                        diet = dietType,
                        sensors = sensors,
                        maths = maths,
                        actions = actions,
                        totalGenes = tc.genome.genes.Count,
                        activeConnections = activeConnections,
                        phenos = topPhenos
                    };
                }
            }

            int dw = this.width / 2;
            int dh = this.height / 2;
            byte[] fertBytes = new byte[dw * dh];
            for (int y = 0; y < dh; y++)
            {
                for (int x = 0; x < dw; x++)
                {
                    float val = fertilityMap[x * 2 + (y * 2 * width)];
                    fertBytes[y * dw + x] = (byte)(Math.Clamp(val, 0f, 1f) * 255);
                }
            }

            var exportFoods = new List<ExportFood>(foodsSnap.Length);
            for (int i = 0; i < foodsSnap.Length; i++)
            {
                exportFoods.Add(new ExportFood { x = foodsSnap[i].x, y = foodsSnap[i].y, meat = foodsSnap[i].isMeat });
            }

            var masses = creaturesSnap.Select(c => c.GetPheno(PType.BodyMass)).OrderBy(m => m).ToList();
            float p10 = masses.Count > 0 ? masses[(int)(masses.Count * 0.10f)] : 0;
            float p50 = masses.Count > 0 ? masses[(int)(masses.Count * 0.50f)] : 0;
            float p90 = masses.Count > 0 ? masses[(int)(masses.Count * 0.90f)] : 0;

            var exportCreatures = new List<ExportCreature>(creaturesSnap.Length);
            float[] angleMap = new float[] { -90f, -45f, -20f, 0f, 20f, 45f, 90f, 180f };

            for (int i = 0; i < creaturesSnap.Length; i++)
            {
                var c = creaturesSnap[i];
                var creatureCones = new List<ExportCone>();

                for (int n = 0; n < c.brain.neurons.Count; n++)
                {
                    var vNeuron = c.brain.neurons[n];
                    if (NeuronDicts.VisionNeurons.Contains(vNeuron.func))
                    {
                        if (vNeuron.dataFields != null && vNeuron.dataFields.Length >= 3)
                        {
                            int fovMode = vNeuron.dataFields[1].intVal;
                            float cFov = fovMode switch { 0 => 5f, 1 => 45f, 2 => 90f, 3 => 180f, 4 => 270f, _ => 45f };
                            float cRange = vNeuron.dataFields[2].intVal * (1f + c.GetPheno(PType.VisionAcuity));
                            float cOffset = angleMap[Math.Clamp(vNeuron.dataFields[0].intVal, 0, 7)];

                            int steepnessVal = 0;
                            if (vNeuron.func == NFunc.VisionGenSim && vNeuron.dataFields.Length > 5) steepnessVal = vNeuron.dataFields[5].intVal;
                            else if (vNeuron.dataFields.Length > 4) steepnessVal = vNeuron.dataFields[4].intVal;

                            creatureCones.Add(new ExportCone { range = cRange, fov = cFov, offset = cOffset, steepness = steepnessVal });
                        }
                    }
                }

                int dietType = 4;
                float carn = c.GetPheno(PType.CarnivoryBias);
                float para = c.GetPheno(PType.Parasitism);
                float scav = c.GetPheno(PType.ScavengerTolerance);

                if (para > 0.2f) dietType = 3;
                else if (carn > 0.65f) dietType = (scav > 0.5f) ? 2 : 1;
                else if (carn < 0.35f) dietType = 0;

                exportCreatures.Add(new ExportCreature
                {
                    id = c.ID.ToString(),
                    x = c.x,
                    y = c.y,
                    dir = c.facingDirection,
                    r = c.colorR,
                    g = c.colorG,
                    b = c.colorB,
                    energy = Math.Clamp(c.energy / c.startingEnergy, 0f, 1f),
                    slot = c.trackedSlot ?? "",
                    cones = creatureCones,
                    parentId = c.parentID,
                    diet = dietType,
                    lineage = c.lineageLifespan,
                    mass = c.GetPheno(PType.BodyMass),
                    sr = c.survivalRatio
                });
            }

            var payload = new
            {
                type = "petri",
                width = this.width,
                height = this.height,

                fertMap = Convert.ToBase64String(fertBytes),

                stats = new
                {
                    ticks = totalTicks,
                    tps = NEMO.currentTPS,
                    pop = creaturesSnap.Length,
                    extinctions = NEMO.extinctionCount,
                    savedGenomesTotal = NEMO.savedGenomesTotal,
                    savedGenomesSession = NEMO.savedGenomesSession,
                    highestSurvivalRatio = highestSurvivalRatio,
                    highestCEQ = highestCEQ,
                    plants = foodsSnap.Count(f => !f.isMeat),
                    meat = foodsSnap.Count(f => f.isMeat),

                    eIn = emaEnergyIn,
                    eOut = emaEnergyOut,
                    totalCreatureE = avgEnergy * creaturesSnap.Length,
                    totalPlantE = plantEnergy,
                    totalMeatE = meatEnergy,

                    births = emaBirths,
                    deaths = emaDeaths,
                    lifeMeas = emaLifespan,
                    lifeMath = mathLifespan,

                    plantsEaten = emaPlantsEaten,
                    meatsEaten = emaMeatsEaten,
                    attacks = emaAttacks,
                    killRate = emaKills,

                    avgAge = avgAge,
                    avgGen = avgGen,
                    maxGen = maxGen,

                    simLoad = NEMO.emaSimTime,
                    uiLoad = NEMO.emaUiTime,

                    herbivores = herbivores,
                    omnivores = omnivores,
                    hunters = hunters,
                    scavengers = scavengers,
                    parasites = parasites,
                    avgCarnivory = avgMeatBias,
                    avgGenes = avgGenes,

                    massP10 = p10,
                    massP50 = p50,
                    massP90 = p90,

                    govCap = govDynamicCapacity,
                    govCurE = govCurrentEnergy,
                    govBaseE = govBaselineEnergy,
                    govActLife = govActiveLifespan,
                    govMathLife = govMathLifespan,
                    govBlend = govBlendFactor,
                    govMom = govMomentum,
                    govWastePen = govWastePenalty,
                    govDiet = govDietFactor,
                },
                trackedInfo = trackedInfoObj,

                blocks = blocksSnap,
                foods = exportFoods,
                creatures = exportCreatures,
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
            NEMO.Log("[WORLD] Starting procedural terrain generation...", "#aaa", ConsoleColor.DarkGray);
            this.width = width;
            this.height = height;

            grid = new Cell[width*height];
            this.fertilityMap = new float[width * height];

            for(int x = 0; x < width; x++)
{
                for (int y = 0; y < height; y++)
                {
                    grid[x + (y * width)] = new Cell(x, y);
                }
            }

            bool terrainValid = false;
            int attempt = 0;

            while (!terrainValid && attempt < Config.maxGenAttempts)
            {
                attempt++;
                staticBlocks.Clear();
                activeFoods.Clear();

                for (int x = 0; x < width; x++)
                    for (int y = 0; y < height; y++)
                    {
                        grid[x + (y * width)].isBlock = false;
                        grid[x + (y * width)].isOasis = false;
                    }

                float terrainOffsetX = (float)rand.NextDouble() * 10000f;
                float terrainOffsetY = (float)rand.NextDouble() * 10000f;
                this.fertOffsetX = (float)rand.NextDouble() * 10000f;
                this.fertOffsetY = (float)rand.NextDouble() * 10000f;

                for (int x = 0; x < width; x++)
                {
                    for (int y = 0; y < height; y++)
                    {
                        float elevation = 0f;
                        float amplitude = Config.amplitude;
                        float frequency = Config.frequency;
                        float maxAmp = 0f;

                        for (int i = 0; i < Config.numOctaves; i++)
                        {
                            float nx = (x * frequency) / 60f + terrainOffsetX;
                            float ny = (y * frequency) / 60f + terrainOffsetY;
                            elevation += MathfPerlin(nx, ny) * amplitude;
                            maxAmp += amplitude;
                            amplitude *= 0.5f;
                            frequency *= 2.0f;
                        }
                        elevation /= maxAmp;

                        if (elevation > Config.elevation) grid[x + (y * width)].isBlock = true;

                        float fnx = (x * Config.plantFrequency) / 60f + fertOffsetX;
                        float fny = (y * Config.plantFrequency) / 60f + fertOffsetY;
                        this.fertilityMap[x + (y * width)] = MathfPerlin(fnx, fny);
                    }
                }

                float veinOffsetX = (float)rand.NextDouble() * 10000f;
                float veinOffsetY = (float)rand.NextDouble() * 10000f;

                for (int x = 0; x < width; x++)
                {
                    for (int y = 0; y < height; y++)
                    {
                        float vx = (x * Config.frequency * Config.caveFrequency) / 60f + veinOffsetX;
                        float vy = (y * Config.frequency * Config.caveFrequency) / 60f + veinOffsetY;
                        float veinNoise = MathfPerlin(vx, vy);

                        if (veinNoise > 0.46f && veinNoise < 0.54f)
                        {
                            grid[x + (y * width)].isBlock = false;

                            if (rand.NextDouble() < 0.0005)
                            {
                                int radius = rand.Next(5, 25);
                                for (int dx = -radius; dx <= radius; dx++)
                                {
                                    for (int dy = -radius; dy <= radius; dy++)
                                    {
                                        if (dx * dx + dy * dy <= radius * radius)
                                        {
                                            int nx = x + dx, ny = y + dy;
                                            if (nx >= 0 && nx < width && ny >= 0 && ny < height)
                                            {
                                                grid[nx + (ny * width)].isBlock = false;

                                                grid[nx + (ny * width)].isOasis = true;
                                                this.fertilityMap[nx + (ny * width)] = 1.0f;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                for (int x = 1; x < width - 1; x++)
                {
                    for (int y = 1; y < height - 1; y++)
                    {
                        if (!grid[x + (y * width)].isBlock)
                        {
                            int neighborBlocks = 0;
                            if (grid[x + 1 + (y * width)].isBlock) neighborBlocks++;
                            if (grid[x - 1 + (y * width)].isBlock) neighborBlocks++;
                            if (grid[x + ((y + 1) * width)].isBlock) neighborBlocks++;
                            if (grid[x + ((y - 1) * width)].isBlock) neighborBlocks++;

                            if (neighborBlocks == 4) grid[x + (y * width)].isBlock = true;
                        }
                    }
                }

                staticBlocks.Clear();
                for (int x = 0; x < width; x++)
                {
                    for (int y = 0; y < height; y++)
                    {
                        if (grid[x + (y * width)].isBlock)
                        {
                            staticBlocks.Add(new ExportBlock { x = x, y = y });
                        }
                    }
                }

                terrainValid = EnforceConnectivity();
            }

            if (!terrainValid)
                NEMO.Log($"[WORLD] Terrain max attempts ({attempt}) reached. Breaking.", "tomato", ConsoleColor.Red);
            else
                NEMO.Log($"[WORLD] Terrain generated after {attempt} attempts.", "palegreen", ConsoleColor.Green);

            creatures = new List<Creature>();

            NEMO.Log($"[WORLD] Spawning {Config.creatureCount} initial creatures...", "#aaa", ConsoleColor.DarkGray);
            while (creatures.Count < Config.creatureCount)
            {
                int x = rand.Next(0, width);
                int y = rand.Next(0, height);

                if (!grid[x + (y * width)].isBlock && grid[x + (y * width)].occupant == null)
                {
                    Genome gen = genomePool.Count > 0 ? genomePool[rand.Next(genomePool.Count)] : GeneTools.GenerateGenome();
                    Creature c = new Creature(x, y, gen, this);
                    c.energy = c.startingEnergy * (c.GetPheno(PType.ReproductionThreshold) + Config.deathEnergy) / 2f;

                    creatures.Add(c);
                    grid[x + (y * width)].occupant = c;
                }
            }

            int initialFoodTarget = (int)(width * height * Config.foodWorldCoverage);
            int foodPlaced = 0;
            int placementAttempts = 0;

            while (foodPlaced < initialFoodTarget && placementAttempts < initialFoodTarget * 10)
            {
                placementAttempts++;
                int fx = rand.Next(0, width);
                int fy = rand.Next(0, height);

                if (!grid[fx + (fy * width)].isBlock && grid[fx + (fy * width)].occupant == null && grid[fx + (fy * width)].foodItem == null)
                {
                    bool isFertileZone = fertilityMap[fx + (fy * width)] > Config.plantCutoff;
                    bool isWildSprout = World.rand.NextDouble() < Config.lingeringPlants;

                    if (isFertileZone || isWildSprout)
                    {
                        var plant = new FoodItem(fx, fy, false);
                        grid[fx + (fy * width)].foodItem = plant;
                        activeFoods.Add(plant);
                        tickEnergyIn += Config.baseNutrition;
                        foodPlaced++;
                    }
                }
            }
            NEMO.Log($"[WORLD] Seeded {foodPlaced} food items.", "palegreen", ConsoleColor.Green);
        }

        private bool EnforceConnectivity()
        {
            bool[,] visited = new bool[width, height];
            List<List<(int x, int y)>> regions = new List<List<(int x, int y)>>();

            int[] dx = { 0, 0, 1, -1 };
            int[] dy = { 1, -1, 0, 0 };

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    if (!grid[x + (y * width)].isBlock && !visited[x, y])
                    {
                        List<(int x, int y)> newRegion = new List<(int x, int y)>();
                        Queue<(int x, int y)> queue = new Queue<(int x, int y)>();

                        queue.Enqueue((x, y));
                        visited[x, y] = true;

                        while (queue.Count > 0)
                        {
                            var (cx, cy) = queue.Dequeue();
                            newRegion.Add((cx, cy));

                            for (int i = 0; i < 4; i++)
                            {
                                int nx = cx + dx[i];
                                int ny = cy + dy[i];

                                if (nx >= 0 && nx < width && ny >= 0 && ny < height)
                                {
                                    if (!grid[nx + (ny * width)].isBlock && !visited[nx, ny])
                                    {
                                        visited[nx, ny] = true;
                                        queue.Enqueue((nx, ny));
                                    }
                                }
                            }
                        }
                        regions.Add(newRegion);
                    }
                }
            }

            if (regions.Count == 0) return false;
            regions.Sort((a, b) => b.Count.CompareTo(a.Count));

            float openSpaceRatio = (float)regions[0].Count / (width * height);
            if (openSpaceRatio < (Config.elevation - 0.15f) || openSpaceRatio > (Config.elevation + 0.15f))
                return false;

            for (int i = 1; i < regions.Count; i++)
            {
                foreach (var cell in regions[i])
                {
                    grid[cell.x + (cell.y * width)].isBlock = true;
                    staticBlocks.Add(new ExportBlock { x = cell.x, y = cell.y });
                }
            }

            return true;
        }

        private float MathfPerlin(float x, float y)
        {
            int xi = (int)MathF.Floor(x);
            int yi = (int)MathF.Floor(y);
            float xf = x - xi;
            float yf = y - yi;

            float u = xf * xf * (3.0f - 2.0f * xf);
            float v = yf * yf * (3.0f - 2.0f * yf);

            float n00 = PseudoRandomHash(xi, yi);
            float n10 = PseudoRandomHash(xi + 1, yi);
            float x1 = Lerp(n00, n10, u);

            float n01 = PseudoRandomHash(xi, yi + 1);
            float n11 = PseudoRandomHash(xi + 1, yi + 1);
            float x2 = Lerp(n01, n11, u);

            return Lerp(x1, x2, v);
        }
        private float PseudoRandomHash(int x, int y)
        {
            int n = x + y * 57;
            n = (n << 13) ^ n;
            return (1.0f - ((n * (n * n * 15731 + 789221) + 1376312589) & 0x7fffffff) / 1073741824.0f) * 0.5f + 0.5f;
        }
        private float Lerp(float a, float b, float t)
        {
            return a + t * (b - a);
        }
    }

    public class Creature : ICreature
    {
        public Genome genome;
        public Brain brain;
        public World world;

        #region Initializations
        public Guid ID = Guid.NewGuid();

        IWorld ICreature.world => world;
        float[] ICreature.phenoCache => phenoCache;

        public int x { get; set; }
        public int y { get; set; }
        public int lastX { get; set; }
        public int lastY { get; set; }
        //0 north, 1 northeast, 2 east, 3 southeast, 4 south, 5 southwest, 6 west, 7 northwest
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

        public int genomeHash { get; set; }
        public byte colorR { get; set; }
        public byte colorG { get; set; }
        public byte colorB { get; set; }
        public int age { get; set; } = 0;


        public int generation = 0;
        public float lineageLifespan = 0f;
        public string parentID = "";
        public int meatsEaten = 0;
        public int plantsEaten = 0;
        public float damageDealt = 0f;
        public int kills = 0;
        public int gestationTimer = 0;
        public string currentAction = "Idle";
        public string? trackedSlot;

        public float[] phenoCache;

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

        public float ceqScore = 0f;
        public float survivalRatio = 0f;
        #endregion

        public void Update()
        {
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

                    float reactivity = Math.Clamp(motorDelta, 0f, 1f);
                    conditionalReactivityScore += reactivity;
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

            return Config.costOfLiving
                 * GetPheno(PType.MetabolicRate)
                 * massFactor
                 * GetPheno(PType.BrainSize)
                 * (1f + GetPheno(PType.VisionAcuity) * 0.1f)
                 * (1f + GetPheno(PType.SpikeCoating) * 0.2f)
                 * (1f + GetPheno(PType.Camouflage) * 0.2f);
        }

        public float EvaluateCEQ(float survivalRatio)
        {
            if (age < 100 || generation < 2)
            {
                ceqScore = 0f;
                return 0f;
            }

            float causality = foodItemsEaten > 0 ? (totalPathEfficiencySum / foodItemsEaten) : 0f;
            float precision = totalActionAttempts > 0 ? (successfulActions / totalActionAttempts) : 0f;
            float entropy = reactivitySamples > 0 ? (conditionalReactivityScore / reactivitySamples) : 0f;

            float rawScore = (causality * 0.35f) + (precision * 0.25f) + (entropy * 0.40f);
            float confidence = Math.Clamp((age - 100) / 200f, 0f, 1f);

            ceqScore = rawScore * confidence;

            return ceqScore;
        }

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

            int pTypeCount = Enum.GetValues(typeof(PType)).Length;
            this.phenoCache = new float[pTypeCount];
            foreach (var kvp in genome.phenotypes)
            {
                this.phenoCache[(int)kvp.Key] = kvp.Value.value;
            }

            var color = genome.GenerateColor();
            this.colorR = color.r;
            this.colorG = color.g;
            this.colorB = color.b;

            this.brain = NeuralTools.GenomeToBrain(genome);
            foreach (Neuron n in brain.neurons)
            {
                n.host = this;
            }

            this.startingEnergy = Config.baseStartingEnergy * GetPheno(PType.BodyMass);
            this.energy = (this.startingEnergy * Config.deathEnergy) +
                          (Config.maturationTime * Config.costOfLiving);
        }
    }

    public class Cell : ICell
    {
        public int x;
        public int y;

        public bool isBlock { get; set; } = false;
        public bool isOasis { get; set; } = false;
        public SignalData[] signals { get; set; } = new SignalData[16];

        public FoodItem? foodItem = null;
        public Creature? occupant = null;

        object? ICell.foodItem => foodItem;
        ICreature? ICell.occupant => occupant;

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
