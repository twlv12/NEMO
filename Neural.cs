
namespace NEMO
{

    public class Genome
    {
        public List<Gene> genes;

        public Genome(List<Gene> genes)
        {
            this.genes = genes;
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
    }

    public class Connection
    {
        public byte sourceID;
        public byte targetID;

        public byte slot;
        public float weight;
        
        public Connection(byte source, byte target, ushort weight, byte slot)
        {
            this.sourceID = source;
            this.targetID = target;
            this.weight = weight;
            this.slot = slot;
        }
    }

    public class Neuron
    {
        public NType neuronType;
        public NFunc neuronFunc;
        public byte ID;

        public float value; //previous tick value, expired
        public float slotASum; //current tick values , pre-activation
        public float slotBSum;
        public float memory; //leaky decay for mem neuron

        public ushort data;

        public Neuron(byte ID, NType type, NFunc func, ushort data)
        {
            this.ID = ID;
            this.neuronType = type;
            this.neuronFunc = func;
            this.data = data;

            slotASum = 0; slotBSum = 0;
            value = 0; memory = 0;
        }
    }

    public class Brain
    {
        public Neuron[] neurons;
        public Connection[] connections;

        public Brain(Neuron[] neurons, Connection[] connections)
        {
            this.neurons = neurons;
            this.connections = connections;
        }

        public void Update()
        {

        }
    }
}
