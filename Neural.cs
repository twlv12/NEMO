
namespace NEMO
{
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

        public void Update()
        {
            //3. ADD CASE FOR NEW NEURON FUNC
        }

        //2. ADD NEURON METHOD
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
