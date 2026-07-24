using System.Collections.Concurrent;
using System.Text.Json;

namespace NEMO
{
    public class World
    {
        public Cell[,] grid;
        public float[,] fertilityMap;
        public List<Creature> creatures;
        public ConcurrentQueue<Creature> pendingNewborns = new();

        public int width = Config.worldWidth;
        public int height = Config.worldHeight;
        public static Random rand = new Random();
        public float fertOffsetX;
        public float fertOffsetY;
        private int fertUpdateCol = 0;

        #region Telemetry
        public long totalTicks = 0;
        public Genome? bestGenome = null;
        public int highestGeneration = 0;
        public float highestSignificance = 0f;

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
        public int tickPlantsEaten = 0;
        public int tickMeatsEaten = 0;
        public int tickAttacks = 0;

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
        public struct ExportCone { public float range { get; set; } public float fov { get; set; } public float offset { get; set; } }
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

            emaPlantsEaten = 0f;
            emaMeatsEaten = 0f;
            emaAttacks = 0f;
            tickPlantsEaten = 0;
            tickMeatsEaten = 0;
            tickAttacks = 0;
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
            }
            foreach (var r in rottedMeatBuf)
            {
                grid[r.x, r.y].foodItem = null;
                activeFoods.Remove(r);
            }

            int currentPlants = activeFoods.Count(f => !f.isMeat);

            fertOffsetX += 0.005f * Config.migrationSpeed;
            fertOffsetY += 0.005f * Config.migrationSpeed;

            for (int y = 0; y < height; y++)
            {
                float fnx = (fertUpdateCol * Config.plantFrequency) / 60f + fertOffsetX;
                float fny = (y * Config.plantFrequency) / 60f + fertOffsetY;
                this.fertilityMap[fertUpdateCol, y] = MathfPerlin(fnx, fny);
            }
            fertUpdateCol = (fertUpdateCol + 1) % width;

            DecaySignals();

            System.Threading.Tasks.Parallel.For(0, creatures.Count, i =>
            {
                if (!creatures[i].isDead) creatures[i].Update();
            });

            ResolveIntents();

            creatures.RemoveAll(c => c.isDead);

            if (!NEMO.disableGovernor && creatures.Count < Config.creatureCount && Config.maintainPopulation)
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
            float blendFactor = Math.Clamp(avgGen / 3.0f, 0f, 1f);
            float activeLifespan = (mathLifespan * (1f - blendFactor)) + (Math.Max(50f, emaLifespan) * blendFactor);

            float baselineEnergy = Config.creatureCount * Config.baseStartingEnergy * Config.globalEnergyMultiplier;

            float demographicShift = (emaBirths - emaDeaths) / Math.Max(0.001f, emaBirths + emaDeaths);
            float momentum = 1.0f + (Config.momentumInfluence * demographicShift * blendFactor);

            float wasteRatio = Math.Min(1.0f, emaEnergyWasted / Math.Max(0.001f, emaEnergyOut));
            float wastePenalty = wasteRatio * Config.wastePenaltyMultiplier * blendFactor;

            float dietFactor = 1.0f - (avgCarnivory * 0.8f);

            float dynamicCapacity = baselineEnergy * momentum * Math.Max(0.1f, 1.0f - wastePenalty) * dietFactor;

            this.govDynamicCapacity = dynamicCapacity;
            this.govCurrentEnergy = currentSystemEnergy;
            this.govBaselineEnergy = baselineEnergy;
            this.govActiveLifespan = activeLifespan;
            this.govMathLifespan = mathLifespan;
            this.govBlendFactor = blendFactor;
            this.govMomentum = momentum;
            this.govWastePenalty = wastePenalty;
            this.govDietFactor = dietFactor;
            #endregion

            if (!NEMO.disableGovernor)
            {
                if (currentSystemEnergy < dynamicCapacity)
                {
                    float deficit = dynamicCapacity - currentSystemEnergy;
                    int plantsToSpawn = (int)(deficit / Config.baseNutrition);

                    plantsToSpawn = Math.Min(plantsToSpawn, (int)(width * height * 0.02f));

                    for (int i = 0; i < plantsToSpawn; i++)
                    {
                        int fx = World.rand.Next(width);
                        int fy = World.rand.Next(height);

                        if (!grid[fx, fy].isBlock && grid[fx, fy].occupant == null && grid[fx, fy].foodItem == null)
                        {
                            bool isFertileZone = fertilityMap[fx, fy] > Config.plantCutoff;
                            bool isWildSprout = World.rand.NextDouble() < Config.lingeringPlants;

                            if (isFertileZone || isWildSprout)
                            {
                                var plant = new FoodItem(fx, fy, false);
                                grid[fx, fy].foodItem = plant;
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
                    plantsToWilt = Math.Min(plantsToWilt, (int)(width * height * 0.02f));

                    int wilted = 0;
                    for (int i = activeFoods.Count - 1; i >= 0 && wilted < plantsToWilt; i--)
                    {
                        if (!activeFoods[i].isMeat)
                        {
                            grid[activeFoods[i].x, activeFoods[i].y].foodItem = null;
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

                c.lastX = c.x;
                c.lastY = c.y;
                c.lastFacing = c.facingDirection;

                if (Math.Abs(c.intentRotate) > 0.01f && rand.NextDouble() < Math.Abs(c.intentRotate))
                {
                    if (c.intentRotate > 0) c.facingDirection = (c.facingDirection + 1) % 8;
                    else c.facingDirection = (c.facingDirection + 7) % 8;
                    float rotCost = NEMO.disableEnergyDrain ? 0f : Config.movementCost * 0.25f * (1f / c.GetPheno(PType.RotationalAgility));
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
                bool hitBlock = !outOfBounds && grid[targetX, targetY].isBlock;

                if (!outOfBounds && !hitBlock && grid[targetX, targetY].occupant == null)
                {
                    if (!NEMO.disableEnergyDrain)
                    {
                        c.energy -= Config.movementCost *
                            c.GetPheno(PType.BodyMass) *
                            c.GetPheno(PType.FastTwitchMuscle) *
                            (1f + c.GetPheno(PType.ArmorDensity)) *
                            (1f + c.GetPheno(PType.RotationalAgility) * 0.2f);
                    }

                    grid[c.x, c.y].occupant = null;
                    c.x = targetX;
                    c.y = targetY;
                    grid[targetX, targetY].occupant = c;
                }
                else if (outOfBounds || hitBlock)
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

                            float deathThreshold = target.startingEnergy * Config.deathEnergy;
                            float maxDrain = Math.Max(0, target.energy - deathThreshold);
                            float actualDamage = Math.Min(finalDamage, maxDrain);

                            target.energy -= actualDamage;
                            c.damageDealt += actualDamage;

                            float vampiricDraw = actualDamage * c.GetPheno(PType.Vampirism);
                            float vampiricAbsorbed = vampiricDraw * Config.meatEntropyMulti;

                            c.energy += vampiricAbsorbed;
                            c.energy -= actualDamage * target.GetPheno(PType.SpikeCoating);

                            if (target.energy <= target.startingEnergy * Config.deathEnergy)
                            {
                                tickDeaths++;
                                c.kills++;
                                EvaluateSignificance(target);
                                emaLifespan = (emaLifespan * 0.999f) + (target.age * 0.001f);

                                target.isDead = true;
                                grid[targetX, targetY].occupant = null;

                                FoodItem? existingItem = grid[targetX, targetY].foodItem;
                                if (existingItem != null) activeFoods.Remove(existingItem);

                                float totalCorpseEnergy = Math.Min(Math.Max(0, target.energy), deathThreshold);
                                float corpseCalories = totalCorpseEnergy * Config.meatEntropyMulti;

                                var meat = new FoodItem(targetX, targetY, true)
                                {
                                    toxicity = target.GetPheno(PType.ToxicCorpse),
                                    nutrition = corpseCalories * (1f - target.GetPheno(PType.ToxicCorpse) * 0.5f)
                                };

                                grid[targetX, targetY].foodItem = meat;
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
                            Creature victim = grid[cx, cy].occupant;
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
                            Creature neighbor = grid[cx, cy].occupant;
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

                var currentCell = grid[c.x, c.y];
                if (currentCell.foodItem != null)
                {
                    FoodItem meal = currentCell.foodItem;
                    float efficiency = meal.isMeat ? c.GetPheno(PType.CarnivoryBias) : (1f - c.GetPheno(PType.CarnivoryBias));
                    if (meal.isMeat) efficiency *= c.GetPheno(PType.ScavengerTolerance);
                    efficiency *= (1f - c.GetPheno(PType.Vampirism) * 0.8f);

                    c.energy += meal.nutrition * efficiency;

                    float poisonTaken = meal.toxicity * Config.baseNutrition * (1f - c.GetPheno(PType.ScavengerTolerance));
                    c.energy -= Math.Max(0f, poisonTaken);

                    if (meal.isMeat)
                    {
                        tickMeatsEaten++;
                        c.meatsEaten++;
                    }
                    else
                    {
                        tickPlantsEaten++;
                        c.plantsEaten++;
                    }
                    activeFoods.Remove(meal);
                    currentCell.foodItem = null;
                }
                if (c.intentConsume > 0.1f)
                {
                    float consumeCost = Config.costOfLiving * c.intentConsume;
                    c.energy -= consumeCost;
                    bool ateSomething = false;

                    for (int dx = -1; dx <= 1; dx++)
                    {
                        for (int dy = -1; dy <= 1; dy++)
                        {
                            if (dx == 0 && dy == 0) continue;

                            int cx = c.x + dx;
                            int cy = c.y + dy;

                            if (cx >= 0 && cx < width && cy >= 0 && cy < height)
                            {
                                FoodItem? adjMeal = grid[cx, cy].foodItem;
                                if (adjMeal != null)
                                {
                                    float efficiency = adjMeal.isMeat ? c.GetPheno(PType.CarnivoryBias) : (1f - c.GetPheno(PType.CarnivoryBias));
                                    if (adjMeal.isMeat) efficiency *= c.GetPheno(PType.ScavengerTolerance);
                                    efficiency *= (1f - c.GetPheno(PType.Vampirism) * 0.8f);

                                    c.energy += adjMeal.nutrition * efficiency * c.intentConsume;

                                    float poisonTaken = adjMeal.toxicity * Config.baseNutrition * (1f - c.GetPheno(PType.ScavengerTolerance));
                                    c.energy -= Math.Max(0f, poisonTaken);

                                    if (adjMeal.isMeat)
                                    {
                                        tickMeatsEaten++;
                                        c.meatsEaten++;
                                    }
                                    else
                                    {
                                        tickPlantsEaten++;
                                        c.plantsEaten++;
                                    }
                                    activeFoods.Remove(adjMeal);
                                    grid[cx, cy].foodItem = null;
                                    ateSomething = true;
                                }
                            }
                        }
                    }

                    if (!ateSomething) tickEnergyWasted += consumeCost;
                }

                c.energy = Math.Clamp(c.energy, 0f, 3f*c.startingEnergy);

                if (c.energy <= c.startingEnergy * Config.deathEnergy)
                {
                    tickDeaths++;
                    EvaluateSignificance(c);
                    emaLifespan = (emaLifespan * 0.999f) + (c.age * 0.001f);

                    c.isDead = true;
                    grid[c.x, c.y].occupant = null;

                    FoodItem? existingItem = grid[c.x, c.y].foodItem;
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

                    grid[c.x, c.y].foodItem = meat;
                    activeFoods.Add(meat);

                    continue;
                }

                if (c.intentSignalChannel >= 0 && c.intentSignalIntensity > 0)
                {
                    float volume = c.GetPheno(PType.PheromoneVolume);
                    grid[c.x, c.y].signals[c.intentSignalChannel].intensity += c.intentSignalIntensity * volume;
                    c.energy -= c.intentSignalIntensity * volume * 0.5f;

                    activeSignalCells.Add(grid[c.x, c.y]);

                    float mappedDecay = 0.2f + 0.797f * (1f - MathF.Pow(1f - c.intentSignalDecay, 3));
                    mappedDecay *= (1f / c.GetPheno(PType.ChemicalVolatility));

                    grid[c.x, c.y].signals[c.intentSignalChannel].decayRate = Math.Clamp(mappedDecay, 0.1f, 0.999f);
                }

                float reqEnergy = c.startingEnergy * c.GetPheno(PType.ReproductionThreshold);
                if (c.energy >= reqEnergy && c.genome.genes.Count > 0)
                {
                    bool placed = false;
                    int spawnX = c.x, spawnY = c.y;

                    int[] dirs = { 0, 1, 2, 3, 4, 5, 6, 7 };
                    World.rand.Shuffle(dirs);
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

                        EvaluateSignificance(c);

                        float investment = c.GetPheno(PType.OffspringInvestment);
                        float investedEnergy = c.energy * investment;

                        c.energy -= investedEnergy;

                        Genome childGenome = GeneTools.MutateGenome(c.genome.Clone());
                        Creature child = new Creature(spawnX, spawnY, childGenome, this);

                        child.energy = investedEnergy * Config.birthEfficiency;

                        child.generation = c.generation + 1;
                        child.lineageLifespan = (c.lineageLifespan == 0f) ? c.age : (c.lineageLifespan * 0.8f) + (c.age * 0.2f);
                        child.parentID = c.ID.ToString();

                        grid[spawnX, spawnY].occupant = child;
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

        public void EvaluateSignificance(Creature c)
        {
            float specificMathLifespan = c.startingEnergy / Math.Max(0.001f, c.GetBaseTickCost());
            float effectiveAge = (c.age * 0.5f) + (c.lineageLifespan * 0.5f);

            float significance = effectiveAge / Math.Max(1f, specificMathLifespan);

            if (significance > highestSignificance)
            {
                highestSignificance = significance;
                highestGeneration = c.generation;
                bestGenome = c.genome.Clone();
            }
        }

        public bool IsCellObstructed(int x, int y)
        {
            if (x < 0 || x >= width || y < 0 || y >= height) return true;
            return grid[x, y].isBlock || grid[x, y].occupant != null;
        }

        public string GetStateJson(Creature[] creaturesSnap, FoodItem[] foodsSnap, ExportBlock[] blocksSnap)
        {
            float avgBurnPerCreature = emaEnergyOut / Math.Max(1f, Config.creatureCount);
            float mathLifespan = Config.baseStartingEnergy / Math.Max(0.001f, avgBurnPerCreature);

            float avgAge = 0, avgGen = 0, avgEnergy = 0, avgMeatBias = 0, avgArmor = 0, avgLethality = 0, avgGenes = 0;
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
                    avgArmor += c.GetPheno(PType.ArmorDensity);
                    avgLethality += c.GetPheno(PType.Lethality);
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
                avgArmor /= count;
                avgLethality /= count;
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

                    float specificMathLifespan = tc.startingEnergy / Math.Max(0.001f, tc.GetBaseTickCost());
                    float liveSignificance = ((tc.age * 0.5f) + (tc.lineageLifespan * 0.5f)) / Math.Max(1f, specificMathLifespan);

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
                        significance = liveSignificance,
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

            var payload = new
            {
                type = "petri",
                width = this.width,
                height = this.height,

                stats = new
                {
                    ticks = totalTicks,
                    tps = NEMO.currentTPS,
                    pop = creaturesSnap.Length,
                    extinctions = NEMO.extinctionCount,
                    savedGenomesTotal = NEMO.savedGenomesTotal,
                    savedGenomesSession = NEMO.savedGenomesSession,
                    highestSignificance = highestSignificance,
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
                    avgArmor = avgArmor,
                    avgLethality = avgLethality,
                    avgGenes = avgGenes,

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
                foods = foodsSnap.Select(f => new ExportFood { x = f.x, y = f.y, meat = f.isMeat }).ToList(),
                creatures = creaturesSnap.Select(c =>
                {
                    List<ExportCone> creatureCones = new List<ExportCone>();

                    var blockNeurons = c.brain.neurons.Where(n => n.func == NFunc.Blockage);

                    foreach (var blockNeuron in blockNeurons)
                    {
                        if (blockNeuron.dataFields != null && blockNeuron.dataFields.Length >= 3)
                        {
                            int angleOffset = blockNeuron.dataFields[0].intVal;
                            int fovMode = blockNeuron.dataFields[1].intVal;
                            int baseRange = blockNeuron.dataFields[2].intVal;

                            float cFov = fovMode switch
                            {
                                0 => 5f,
                                1 => 45f,
                                2 => 90f,
                                3 => 180f,
                                4 => 270f,
                                _ => 45f
                            };

                            float cRange = baseRange * (1f + c.GetPheno(PType.VisionAcuity));
                            float cOffset = angleOffset * 45f;

                            creatureCones.Add(new ExportCone { range = cRange, fov = cFov, offset = cOffset });
                        }
                    }

                    return new ExportCreature
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
                    };
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
            this.fertilityMap = new float[width, height];

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    grid[x, y] = new Cell(x, y);
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
                        grid[x, y].isBlock = false;

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

                        if (elevation > Config.elevation)
                        {
                            grid[x, y].isBlock = true;
                            staticBlocks.Add(new ExportBlock { x = x, y = y });
                        }

                        float fnx = (x * Config.plantFrequency) / 60f + fertOffsetX;
                        float fny = (y * Config.plantFrequency) / 60f + fertOffsetY;
                        this.fertilityMap[x, y] = MathfPerlin(fnx, fny);
                    }
                }

                for (int x = 1; x < width - 1; x++)
                {
                    for (int y = 1; y < height - 1; y++)
                    {
                        if (!grid[x, y].isBlock)
                        {
                            int neighborBlocks = 0;
                            if (grid[x + 1, y].isBlock) neighborBlocks++;
                            if (grid[x - 1, y].isBlock) neighborBlocks++;
                            if (grid[x, y + 1].isBlock) neighborBlocks++;
                            if (grid[x, y - 1].isBlock) neighborBlocks++;

                            if (neighborBlocks == 4)
                            {
                                grid[x, y].isBlock = true;
                                staticBlocks.Add(new ExportBlock { x = x, y = y });
                            }
                        }
                    }
                }

                terrainValid = CheckConnectivity();
            }

            if (!terrainValid)
                Console.WriteLine("[TERRAIN] Max terrain generation attempts reached.");
            else
                Console.WriteLine($"[TERRAIN] Terrain generation completed in {attempt} attempts");

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
            int foodPlaced = 0;
            int placementAttempts = 0;

            while (foodPlaced < initialFoodTarget && placementAttempts < initialFoodTarget * 10)
            {
                placementAttempts++;
                int fx = rand.Next(0, width);
                int fy = rand.Next(0, height);

                if (!grid[fx, fy].isBlock && grid[fx, fy].occupant == null && grid[fx, fy].foodItem == null)
                {
                    bool isFertileZone = fertilityMap[fx, fy] > Config.plantCutoff;
                    bool isWildSprout = World.rand.NextDouble() < Config.lingeringPlants;

                    if (isFertileZone || isWildSprout)
                    {
                        var plant = new FoodItem(fx, fy, false);
                        grid[fx, fy].foodItem = plant;
                        activeFoods.Add(plant);
                        tickEnergyIn += Config.baseNutrition;
                    }
                }
            }
        }

        private bool CheckConnectivity()
        {
            bool[,]? visited = new bool[width, height];
            int totalOpenCells = 0;
            int startX = -1, startY = -1;

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    if (!grid[x, y].isBlock)
                    {
                        totalOpenCells++;
                        if (startX == -1)
                        {
                            startX = x;
                            startY = y;
                        }
                    }
                }
            }

            if (totalOpenCells == 0) return false;

            Queue<(int x, int y)> queue = new Queue<(int x, int y)>();
            queue.Enqueue((startX, startY));
            visited[startX, startY] = true;
            int reachedCount = 1;

            int[] dx = { 0, 0, 1, -1 };
            int[] dy = { 1, -1, 0, 0 };

            while (queue.Count > 0)
            {
                var (cx, cy) = queue.Dequeue();

                for (int i = 0; i < 4; i++)
                {
                    int nx = cx + dx[i];
                    int ny = cy + dy[i];

                    if (nx >= 0 && nx < width && ny >= 0 && ny < height)
                    {
                        if (!grid[nx, ny].isBlock && !visited[nx, ny])
                        {
                            visited[nx, ny] = true;
                            queue.Enqueue((nx, ny));
                            reachedCount++;
                        }
                    }
                }
            }

            return reachedCount == totalOpenCells;
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
        public float lineageLifespan = 0f;
        public string parentID = "";
        public int meatsEaten = 0;
        public int plantsEaten = 0;
        public float damageDealt = 0f;
        public int kills = 0;

        public float energy = Config.baseStartingEnergy;
        public bool isDead = false;

        public float intentMove = 0f;
        public float intentMoveX = 0f;
        public float intentMoveY = 0f;
        public float intentRotate = 0f;
        public float intentConsume = 0f;
        public float intentAttack = 0f;
        public float intentSignalIntensity = 0f;
        public int intentSignalChannel = -1;
        public float intentSignalDecay = 0f;
        public string currentAction = "Idle";

        public int genomeHash;
        public byte colorR;
        public byte colorG;
        public byte colorB;
        public string? trackedSlot;

        public float[] phenoCache;

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
            intentSignalIntensity = 0f;
            intentSignalChannel = -1;
            intentSignalDecay = 0f;
        }

        public float GetPheno(PType type) => phenoCache[(int)type];

        public float GetBaseTickCost()
        {
            return Config.costOfLiving
                 * GetPheno(PType.MetabolicRate)
                 * GetPheno(PType.BodyMass)
                 * GetPheno(PType.BrainSize)
                 * (1f + GetPheno(PType.VisionAcuity) * 0.1f)
                 * (1f + GetPheno(PType.SpikeCoating) * 0.2f)
                 * (1f + GetPheno(PType.Camouflage) * 0.2f);
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
            foreach (Neuron n in brain.neurons){
                n.host = this;
            }

            this.startingEnergy = Config.baseStartingEnergy * GetPheno(PType.BodyMass);
            this.energy = (this.startingEnergy * Config.deathEnergy) +
                          (Config.maturationTime * Config.costOfLiving);
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
