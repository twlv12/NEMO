namespace NEMO
{
    public class World
    {
        public Cell[,] grid;
        public List<Creature> creatures;

        public int width = Config.worldWidth;
        public int height = Config.worldHeight;
        public static Random rand = new Random();

        public void Update()
        {
            DecaySignals();
            foreach (var creature in creatures)
            {
                if (creature.isDead) continue;
                creature.Update();
            }
            ResolveIntents();

            creatures.RemoveAll(c => c.isDead);
        }

        public void DecaySignals()
        {
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    for (int c = 0; c < 16; c++)
                    {
                        if (grid[x, y].signals[c].intensity > 0.01f)
                            grid[x, y].signals[c].intensity *= grid[x, y].signals[c].decayRate;
                        else
                            grid[x, y].signals[c] = new SignalData();
                    }
                }
            }
        }

        private void ResolveIntents()
        {
            List<Creature> movingCreatures = new List<Creature>();

            foreach (var c in creatures)
            {
                if (c.isDead) continue;

                c.lastX = c.x;
                c.lastY = c.y;
                c.lastFacing = c.facingDirection;

                if (Math.Abs(c.intentRotate) > 0.01f && rand.NextDouble() < Math.Abs(c.intentRotate))
                {
                    if (c.intentRotate > 0) c.facingDirection = (c.facingDirection + 1) % 8;
                    else c.facingDirection = (c.facingDirection + 7) % 8;
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

            movingCreatures = movingCreatures.OrderByDescending(c => c.intentMove).ToList();

            foreach (var c in movingCreatures)
            {
                int targetX = c.x + (int)c.intentMoveX;
                int targetY = c.y + (int)c.intentMoveY;

                if (!IsCellObstructed(targetX, targetY))
                {
                    c.energy -= Config.movementCost;
                    grid[c.x, c.y].occupant = null;
                    c.x = targetX;
                    c.y = targetY;
                    grid[targetX, targetY].occupant = c;
                }
            }

            foreach (var c in creatures)
            {
                if (c.isDead) continue;

                if (rand.NextDouble() < c.intentAttack)
                {
                    c.energy -= Config.attackCost;
                    var vec = DirectionToVector[c.facingDirection];
                    int targetX = c.x + vec.dx;
                    int targetY = c.y + vec.dy;

                    if (targetX >= 0 && targetX < width && targetY >= 0 && targetY < height)
                    {
                        Creature target = grid[targetX, targetY].occupant;
                        if (target != null && !target.isDead)
                        {
                            target.isDead = true;
                            grid[targetX, targetY].occupant = null;
                            grid[targetX, targetY].foodItem = new FoodItem(targetX, targetY, true);
                        }
                    }
                }

                c.energy -= Config.costOfLiving;
                var currentCell = grid[c.x, c.y];
                if (currentCell.foodItem != null)
                {
                    float baseNutrition = currentCell.foodItem.nutrition;
                    float bonusMultiplier = 1f + Math.Max(0f, c.intentConsume);
                    c.energy += baseNutrition * bonusMultiplier;
                    currentCell.foodItem = null;
                }

                if (c.energy <= 0)
                {
                    c.isDead = true;
                    grid[c.x, c.y].occupant = null;
                    grid[c.x, c.y].foodItem = new FoodItem(c.x, c.y, true);
                }

                c.ResetIntents();
            }
        }

        public bool IsCellObstructed(int x, int y)
        {
            if (x < 0 || x >= width || y < 0 || y >= height) return true;
            return grid[x, y].isBlock || grid[x, y].occupant != null;
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
        }
    }

    public class Creature
    {
        public Genome genome;
        public Brain brain;
        public World world;

        public int x;
        public int y;
        public int lastX;
        public int lastY;

        //0 north, 1 northeast, 2 east, 3 southeast, 4 south, 5 southwest, 6 west, 7 northwest
        public int facingDirection;
        public int lastFacing;

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

        public FoodItem(int x, int y, bool isMeat)
        {
            this.x = x;
            this.y = y;
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
