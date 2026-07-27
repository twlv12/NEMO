using System.Text.Json;

namespace NEMO
{
	public struct VisionOffset
	{
		public int dx;
		public int dy;
		public float weight;
	}

	public struct VisionLUT
	{
		public VisionOffset[] offsets;
		public float maxWeight;
	}

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

		public Creature? host;

		public float value = 0f; //current committed output
		public float slotASum = 0f; //new values for input
		public float slotBSum = 0f;

		public NeuronGeneData geneData;
		public List<NeuronDataField> dataFieldsList;
		public List<Connection> outgoingConnections;
		public List<Connection> incomingConnections;

		public NeuronDataField[] dataFields;
		public Connection[] incomingArray;
		public Connection[] outgoingArray;

		public List<float> lastValues = new() { 0 }; //used for random
		public float lastValue = 0f; //used for pulse

		public VisionLUT[] visionLUT;
		private static readonly float[] AngleMap = { -90f, -45f, -20f, 0f, 20f, 45f, 90f, 180f };
		private static readonly float[] SteepnessMap = { 0.33f, 0.66f, 1.0f, 1.5f, 2.0f, 3.0f, 4.0f, 5.0f };
		private static readonly float[] FacingToAngle = { 270f, 315f, 0f, 45f, 90f, 135f, 180f, 225f };

		public void RunFunction()
		{
			float combinedInput = slotASum + slotBSum;

			if (host == null)
			{
				if (type == NType.Action)
				{
					value = Math.Clamp(combinedInput, -1f, 1f);
					return;
				}

				else if (type == NType.Sensor
					&& func != NFunc.Constant
					&& func != NFunc.GetRandom)
				{
					value = 0f;
					return;
				}
			}

			int w = host?.world.width ?? 0;
			int h = host?.world.height ?? 0;
			Cell[] grid = host?.world.grid;
			int hx = host?.x ?? 0;
			int hy = host?.y ?? 0;

			switch (func)
			{
				case NFunc.Constant:
					value = dataFields[0].floatVal;
					break;
				case NFunc.GetRandom:
					float newRand = Random.Shared.NextSingle() * 2f - 1f;
					float alpha = 1f / (dataFields[0].intVal + 1f);
					lastValue = (lastValue * (1f - alpha)) + (newRand * alpha);
					value = lastValue;
					break;
				case NFunc.Gradient:
					int axis = dataFields[0].intVal;
					value = axis == 0 ? (float)hx / w : (float)hy / h;
					value = (value * 2f) - 1f;
					break;
				case NFunc.MoveDelta:
					bool checkRot = dataFields[0].boolVal;
					if (checkRot)
						value = host.facingDirection == host.lastFacing ? 0f : 1f;
					else
						value = (hx != host.lastX || hy != host.lastY) ? 1f : 0f;
					break;
				case NFunc.GetSignal:
					int channel = dataFields[0].intVal;
					int radius = dataFields[1].intVal;
					float maxSignal = 0f;

					for (int dx = -radius; dx <= radius; dx++)
					{
						for (int dy = -radius; dy <= radius; dy++)
						{
							int cx = hx + dx;
							int cy = hy + dy;
							if ((uint)cx < (uint)w && (uint)cy < (uint)h)
							{
								maxSignal = Math.Max(maxSignal, grid[cx + (cy * w)].signals[channel].intensity);
							}
						}
					}

					maxSignal *= host.GetPheno(PType.OlfactorySensitivity);
					value = Math.Clamp(maxSignal, 0f, 1f);
					break;
				case NFunc.Age:
					value = Math.Clamp(((float)host.age / host.startingEnergy) * 3f, 0f, 1f);
					break;
				case NFunc.Density:
					int targetType = dataFields[0].intVal;
					int r = dataFields[1].intVal;
					float amplifier = dataFields[2].floatVal * (float)Math.Pow(r + 1, 2);
					int hits = 0;

					for (int dx = -r; dx <= r; dx++)
					{
						for (int dy = -r; dy <= r; dy++)
						{
							int cx = hx + dx;
							int cy = hy + dy;
							if ((uint)cx < (uint)w && (uint)cy < (uint)h)
							{
								Cell cell = grid[cx + (cy * w)];
								if (targetType == 0 && (cell.occupant != null || cell.foodItem != null || cell.isBlock)) hits++;
								else if (targetType == 1 && cell.foodItem != null) hits++;
								else if (targetType == 2 && cell.occupant != null && cell.occupant != host) hits++;
								else if (targetType == 3 && cell.isBlock) hits++;
							}
						}
					}

					value = Math.Clamp(hits * amplifier, 0f, 1f);
					break;
				case NFunc.Energy:
					float energyRatio = host.energy / (host.startingEnergy * 3f);
					value = Math.Clamp((energyRatio * 2f) - 1f, -1f, 1f);
					break;
				case NFunc.Oscillator:
					bool useWorldTime = dataFields[0].boolVal;
					bool speciesSync = dataFields[1].boolVal;
					int periodScale = dataFields[2].intVal;

					long timeBase = useWorldTime ? host.world.totalTicks : host.age;
					float period = 10f + (periodScale * 10f);
					float phase = 0f;
					if (speciesSync)

					{
						phase = (Math.Abs(host.genomeHash) % 1000) / 1000f * MathF.PI * 2f;
					}

					value = MathF.Sin((timeBase / period) * MathF.PI * 2f + phase);
					break;

				case NFunc.VisionKinematics:
					if (visionLUT == null) GenerateVisionLUT();
					int targetAction = dataFields[3].intVal;
					VisionOffset[] kOffsets = visionLUT[host.facingDirection].offsets;

					value = 0f;

					for (int i = 0; i < kOffsets.Length; i++)
					{
						ref VisionOffset offset = ref kOffsets[i];
						int cx = hx + offset.dx;
						int cy = hy + offset.dy;

						if ((uint)cx < (uint)w && (uint)cy < (uint)h)
						{
							Creature target = grid[cx + (cy * w)].occupant;

							if (target != null && target != host && !target.isDead)
							{
								if (Random.Shared.NextDouble() < target.GetPheno(PType.Camouflage)) continue;

								float actionVal = 0f;
								if (targetAction == 0) actionVal = target.intentAttack;
								else if (targetAction == 1) actionVal = target.intentMove;
								else if (targetAction == 2) actionVal = target.intentRotate;
								else if (targetAction == 3) actionVal = target.intentConsume;

								value = actionVal * offset.weight;
								break;
							}
						}
					}
					break;
				case NFunc.VisionBlockage:
					if (visionLUT == null) GenerateVisionLUT();
					VisionOffset[] bOffsets = visionLUT[host.facingDirection].offsets;
					int targetMode = dataFields[3].intVal;

					if (targetMode == 0)
					{
						float hitValue = 0f;
						for (int i = 0; i < bOffsets.Length; i++)
						{
							ref VisionOffset offset = ref bOffsets[i];
							int cx = hx + offset.dx;
							int cy = hy + offset.dy;

							if (cx < 0 || cx >= w || cy < 0 || cy >= h)
							{
								hitValue = offset.weight;
								break;
							}

							Cell cell = grid[cx + (cy * w)];
							if (cell.isBlock || cell.foodItem != null || (cell.occupant != null && cell.occupant != host))
							{
								hitValue = offset.weight;
								break;
							}
						}
						value = hitValue;
					}
					else
					{
						float totalScore = 0f;
						for (int i = 0; i < bOffsets.Length; i++)
						{
							ref VisionOffset offset = ref bOffsets[i];
							int cx = hx + offset.dx;
							int cy = hy + offset.dy;
							if ((uint)cx < (uint)w && (uint)cy < (uint)h)
							{
								Cell cell = grid[cx + (cy * w)];
								if (cell.isBlock || cell.foodItem != null || (cell.occupant != null && cell.occupant != host))
								{
									totalScore += offset.weight;
								}
							}
						}
						value = Math.Clamp(totalScore, 0f, 1f);
					}
					break;
				case NFunc.VisionGenSim:
					if (visionLUT == null) GenerateVisionLUT();

					bool exactMatch = dataFields[3].boolVal;
					bool massMode = dataFields[4].boolVal;
					VisionOffset[] simOffsets = visionLUT[host.facingDirection].offsets;
					float simMax = visionLUT[host.facingDirection].maxWeight;

					float totalSimScore = 0f;
					value = 0f;

					int hHash = host.genomeHash;
					byte hR = host.colorR, hG = host.colorG, hB = host.colorB;

					for (int i = 0; i < simOffsets.Length; i++)
					{
						ref VisionOffset offset = ref simOffsets[i];
						int cx = hx + offset.dx;
						int cy = hy + offset.dy;

						if ((uint)cx < (uint)w && (uint)cy < (uint)h)
						{
							Creature target = grid[cx + (cy * w)].occupant;
							if (target != null && target != host)
							{
								float currentSim = 0f;
								if (exactMatch)
								{
									currentSim = (hHash == target.genomeHash) ? 1f : -1f;
								}
								else
								{
									float totalDiff = MathF.Abs(hR - target.colorR) + MathF.Abs(hG - target.colorG) + MathF.Abs(hB - target.colorB);
									currentSim = 1f - ((totalDiff / 765f) * 2f);
								}

								float visualWeight = offset.weight * (1f - target.GetPheno(PType.Camouflage));
								totalSimScore += currentSim * visualWeight;
								if (!massMode) break;
							}
						}
					}

					value = massMode ? (simMax > 0 ? totalSimScore / simMax : 0f) : totalSimScore;
					break;
				case NFunc.VisionProximity:
					if (visionLUT == null) GenerateVisionLUT();
					int proxTarget = dataFields[3].intVal;
					VisionOffset[] proxOffsets = visionLUT[host.facingDirection].offsets;
					float closestDistRatio = 0f;

					float maxD = dataFields[2].intVal * host.GetPheno(PType.VisionAcuity);

					for (int i = 0; i < proxOffsets.Length; i++)
					{
						ref VisionOffset offset = ref proxOffsets[i];
						int cx = hx + offset.dx;
						int cy = hy + offset.dy;

						if (cx < 0 || cx >= w || cy < 0 || cy >= h)
						{
							if (proxTarget == 0 || proxTarget == 3)
							{
								float dist = MathF.Sqrt(offset.dx * offset.dx + offset.dy * offset.dy);
								closestDistRatio = Math.Max(0f, 1f - (dist / Math.Max(1f, maxD)));
								break;
							}
							continue;
						}

						Cell cell = grid[cx + (cy * w)];
						bool hit = false;

						if (proxTarget == 0 && (cell.occupant != null || cell.foodItem != null || cell.isBlock)) hit = true;
						else if (proxTarget == 1 && cell.foodItem != null) hit = true;
						else if (proxTarget == 2 && cell.occupant != null && cell.occupant != host) hit = true;
						else if (proxTarget == 3 && cell.isBlock) hit = true;

						if (hit)
						{
							if (cell.occupant != null && cell.occupant != host)
							{
								if (Random.Shared.NextDouble() < cell.occupant.GetPheno(PType.Camouflage)) continue;
							}
							float dist = MathF.Sqrt(offset.dx * offset.dx + offset.dy * offset.dy);
							closestDistRatio = Math.Max(0f, 1f - (dist / Math.Max(1f, maxD)));
							break;
						}
					}
					value = closestDistRatio;
					break;
				case NFunc.VisionTrait:
					if (visionLUT == null) GenerateVisionLUT();
					int traitIndex = dataFields[3].intVal;
					VisionOffset[] traitOffsets = visionLUT[host.facingDirection].offsets;
					float traitMax = visionLUT[host.facingDirection].maxWeight;
					float totalTraitScore = 0f;

					for (int i = 0; i < traitOffsets.Length; i++)
					{
						ref VisionOffset offset = ref traitOffsets[i];
						int cx = hx + offset.dx;
						int cy = hy + offset.dy;

						if ((uint)cx < (uint)w && (uint)cy < (uint)h)
						{
							Creature target = grid[cx + (cy * w)].occupant;
							if (target != null && target != host)
							{
								float traitValue = 0f;
								if (traitIndex >= 0 && traitIndex < target.phenoCache.Length)
								{
									traitValue = target.phenoCache[traitIndex];
								}

								float visualWeight = offset.weight * (1f - target.GetPheno(PType.Camouflage));
								totalTraitScore += traitValue * visualWeight;
							}
						}
					}
					value = traitMax > 0 ? totalTraitScore / traitMax : 0f;
					break;
				case NFunc.VisionHealth:
					if (visionLUT == null) GenerateVisionLUT();
					bool massModeHealth = dataFields[3].boolVal;
					VisionOffset[] hOffsets = visionLUT[host.facingDirection].offsets;
					float hMax = visionLUT[host.facingDirection].maxWeight;
					float totalHScore = 0f;

					for (int i = 0; i < hOffsets.Length; i++)
					{
						ref VisionOffset offset = ref hOffsets[i];
						int cx = hx + offset.dx;
						int cy = hy + offset.dy;

						if ((uint)cx < (uint)w && (uint)cy < (uint)h)
						{
							Creature target = grid[cx + (cy * w)].occupant;
							if (target != null && target != host && !target.isDead)
							{
								float hRatio = target.energy / (target.startingEnergy * 3f);
								float visualWeight = offset.weight * (1f - target.GetPheno(PType.Camouflage));
								totalHScore += hRatio * visualWeight;
								if (!massModeHealth) break; 
							}
						}
					}
					value = massModeHealth ? (hMax > 0 ? totalHScore / hMax : 0f) : totalHScore;
					break;
				case NFunc.VisionIsolation:
					if (visionLUT == null) GenerateVisionLUT();
					int isoRadius = dataFields[3].intVal;
					VisionOffset[] iOffsets = visionLUT[host.facingDirection].offsets;

					value = 0f;
					for (int i = 0; i < iOffsets.Length; i++)
					{
						ref VisionOffset offset = ref iOffsets[i];
						int cx = hx + offset.dx;
						int cy = hy + offset.dy;

						if ((uint)cx < (uint)w && (uint)cy < (uint)h)
						{
							Creature target = grid[cx + (cy * w)].occupant;
							if (target != null && target != host && !target.isDead)
							{
								int neighbors = 0;
								for (int dx = -isoRadius; dx <= isoRadius; dx++)
								{
									for (int dy = -isoRadius; dy <= isoRadius; dy++)
									{
										if (dx == 0 && dy == 0) continue;
										int nX = cx + dx;
										int nY = cy + dy;
										if ((uint)nX < (uint)w && (uint)nY < (uint)h)
										{
											if (grid[nX + (nY * w)].occupant != null) neighbors++;
										}
									}
								}

								float isolationScore = 1f - (neighbors / 2.5f);
								value = Math.Clamp(isolationScore, -1f, 1f) * offset.weight;
								break;
							}
						}
					}
					break;

				case NFunc.Relay:
					value = Math.Clamp(combinedInput + dataFields[0].floatVal, -1f, 1f);
					break;
				case NFunc.Threshold:
					value = NeuralTools.FastTanh(0.5f + dataFields[2].floatVal * 10f * ((slotASum + slotBSum) - dataFields[0].floatVal)) * (dataFields[1].boolVal == false ? 1 : -1);
					break;
				case NFunc.Multiply:
					if (dataFields[0].boolVal)
					{
						value = NeuralTools.FastTanh(slotASum * slotBSum);
					}
					else
					{
						float product = 1f;
						if (slotASum != 0) product *= slotASum;
						if (slotBSum != 0) product *= slotBSum;
						value = Math.Clamp(product, -1f, 1f);
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
					value = Math.Clamp(combinedInput * (1f + dataFields[0].floatVal * 4f), -1f, 1f);
					break;
				case NFunc.Pulse:
					float diff = combinedInput - lastValue;
					if (MathF.Abs(diff) > dataFields[0].floatVal)
						value = dataFields[1].floatVal * MathF.Sign(diff);
					else
						value = 0f;
					lastValue = combinedInput;
					break;
				case NFunc.Transistor:
					bool aIsGate = dataFields[0].boolVal;
					bool invertGate = dataFields[1].boolVal;

					float gate = aIsGate ? slotASum : slotBSum;
					float signal = aIsGate ? slotBSum : slotASum;

					if (invertGate)
						value = gate < 0f ? signal : 0f;
					else
						value = gate > 0f ? signal : 0f;
					break;
				case NFunc.Derivative:
					value = Math.Clamp(combinedInput - lastValue, -1f, 1f);
					lastValue = combinedInput;
					break;
				case NFunc.Divide:
					if (Math.Abs(slotBSum) < 0.01f)
						value = Math.Sign(slotASum) * 1f;
					else
						value = Math.Clamp(slotASum / slotBSum, -1f, 1f);
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
						if (Random.Shared.NextDouble() > 0.5)
							host.intentMoveX += (Random.Shared.NextDouble() > 0.5 ? strength : -strength);
						else
							host.intentMoveY += (Random.Shared.NextDouble() > 0.5 ? strength : -strength);
					}
					else
					{
						if (Random.Shared.NextDouble() > 0.5)
							host.intentMove += (Random.Shared.NextDouble() > 0.5 ? strength : -strength);
						else
							host.intentRotate += (Random.Shared.NextDouble() > 0.5 ? strength : -strength);
					}

					value = combinedInput;
					break;
				case NFunc.EmitSignal:
					if (combinedInput > 0)
					{
						host.intentSignalChannel = dataFields[0].intVal;
						host.intentSignalIntensity = combinedInput;
						host.intentSignalDecay = dataFields[1].floatVal;
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
			for (int i = 0; i < incomingArray.Length; i++)
			{
				Connection conn = incomingArray[i];
				if (conn.slot == 0)
					slotASum += (conn.src.value * conn.weight);
				else
					slotBSum += (conn.src.value * conn.weight);
			}
		}

		public void GenerateVisionLUT()
		{
			visionLUT = new VisionLUT[8];

			int angleIdx = Math.Clamp(dataFields[0].intVal, 0, 7);
			float requestedAngleOffset = AngleMap[angleIdx];
			int fovMode = dataFields[1].intVal;

			int maxDist = (int)(dataFields[2].intVal *
				host.GetPheno(PType.VisionAcuity) * (1 - host.GetPheno(PType.Camouflage)));
			maxDist = Math.Clamp(maxDist, 1, 20);

			int steepnessIndex = (this.func == NFunc.VisionGenSim) ? 5 : 4;
			float steepnessExponent = SteepnessMap[Math.Clamp(dataFields[steepnessIndex].intVal, 0, 7)];

			steepnessExponent *= host.GetPheno(PType.FovSpecialization);

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
				var tempOffsets = new List<VisionOffset>();

				float globalFacingAngle = FacingToAngle[facing];
				float targetAngle = (globalFacingAngle + requestedAngleOffset) % 360f;

				float maxD = maxDist + 0.5f;

				for (int dx = -maxDist; dx <= maxDist; dx++)
				{
					for (int dy = -maxDist; dy <= maxDist; dy++)
					{
						if (dx == 0 && dy == 0) continue;

						float dist = MathF.Sqrt(dx * dx + dy * dy);
						if (dist > maxD) continue;

						float cellAngle = MathF.Atan2(dy, dx) * (180f / MathF.PI);
						if (cellAngle < 0) cellAngle += 360f;

						float diff = MathF.Abs(targetAngle - cellAngle);
						if (diff > 180f) diff = 360f - diff;

						if (diff <= fovDegrees / 2f)
						{
							float distRatio = Math.Clamp(1f - (dist / maxD), 0f, 1f);
							float angleRatio = Math.Clamp(1f - (diff / (fovDegrees / 2f)), 0f, 1f);

							float distWeight = MathF.Pow(distRatio, steepnessExponent);
							float finalWeight = distWeight * angleRatio;

							tempOffsets.Add(new VisionOffset { dx = dx, dy = dy, weight = finalWeight });
						}
					}
				}

				tempOffsets.Sort((a, b) => b.weight.CompareTo(a.weight));

				float maxWeight = 0f;
				for (int i = 0; i < tempOffsets.Count; i++) maxWeight += tempOffsets[i].weight;

				visionLUT[facing] = new VisionLUT { offsets = tempOffsets.ToArray(), maxWeight = maxWeight };
			}
		}

		public Neuron(NType type, NFunc func, uint id,
			List<NeuronDataField> fields, NeuronGeneData geneData)
		{
			this.type = type;
			this.func = func;
			ID = id;
			dataFieldsList = fields;
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
		public Brain(List<Neuron> neurons, List<Connection> connections)
		{
			this.neurons = neurons;
			this.connections = connections;
		}

		public void UpdateAllNeurons()
		{
			for (int i = 0; i < neurons.Count; i++)
			{
				Neuron n = neurons[i];
				n.slotASum = 0;
				n.slotBSum = 0;
				n.AccumulateConnections();
				n.RunFunction();
			}
		}
	}

	public static class NeuralTools
	{
		public static Random rand = new Random();

		public static Brain GenomeToBrain(Genome genome)
		{
			Dictionary<uint, Neuron> neurons = new Dictionary<uint, Neuron>();
			List<Connection> connections = new List<Connection>();

			foreach (Gene gene in genome.genes)
			{
				if (gene.disabled) continue;

				Neuron src = GetOrCreateNeuron(neurons, gene.src);
				Neuron tgt = GetOrCreateNeuron(neurons, gene.tgt);
				Connection c = ConnectTwoNeurons(src, tgt, gene);
				c.graphID = connections.Count;
				connections.Add(c);
			}

			bool changed = true;
			List<uint> toRemove = new List<uint>();
			while (changed)
			{
				changed = false;
				toRemove.Clear();

				foreach (var kvp in neurons)
				{
					Neuron n = kvp.Value;
					if (n.type == NType.Math && (n.incomingConnections.Count == 0 || n.outgoingConnections.Count == 0))
					{
						toRemove.Add(kvp.Key);
					}
				}

				foreach (uint id in toRemove)
				{
					Neuron dead = neurons[id];
					for (int i = dead.incomingConnections.Count - 1; i >= 0; i--)
					{
						var inConn = dead.incomingConnections[i];
						inConn.src.outgoingConnections.Remove(inConn);
						connections.Remove(inConn);
					}
					for (int i = dead.outgoingConnections.Count - 1; i >= 0; i--)
					{
						var outConn = dead.outgoingConnections[i];
						outConn.tgt.incomingConnections.Remove(outConn);
						connections.Remove(outConn);
					}
					neurons.Remove(id);
					changed = true;
				}
			}

			List<Neuron> sortedNeurons = new List<Neuron>(neurons.Count);
			List<Neuron> maths = new List<Neuron>();

			foreach (var n in neurons.Values)
			{
				if (n.type == NType.Sensor) sortedNeurons.Add(n);
				else if (n.type == NType.Math) maths.Add(n);
			}

			Dictionary<Neuron, int> mathInDegrees = new Dictionary<Neuron, int>(maths.Count);
			foreach (var m in maths)
			{
				int inCount = 0;
				foreach (var c in m.incomingConnections)
					if (c.src.type == NType.Math && c.src != m) inCount++;
				mathInDegrees[m] = inCount;
			}

			Queue<Neuron> mathQueue = new Queue<Neuron>();
			foreach (var m in maths)
				if (mathInDegrees[m] == 0) mathQueue.Enqueue(m);

			while (maths.Count > 0)
			{
				Neuron current;
				if (mathQueue.Count > 0)
				{
					current = mathQueue.Dequeue();
				}
				else
				{
					current = maths[0];
					int minDeg = mathInDegrees[current];
					for (int i = 1; i < maths.Count; i++)
					{
						int d = mathInDegrees[maths[i]];
						if (d < minDeg) { minDeg = d; current = maths[i]; }
					}
				}

				maths.Remove(current);
				sortedNeurons.Add(current);

				foreach (var outEdge in current.outgoingConnections)
				{
					if (outEdge.tgt.type == NType.Math && mathInDegrees.ContainsKey(outEdge.tgt))
					{
						mathInDegrees[outEdge.tgt]--;
						if (mathInDegrees[outEdge.tgt] == 0) mathQueue.Enqueue(outEdge.tgt);
					}
				}
			}

			foreach (var n in neurons.Values)
				if (n.type == NType.Action) sortedNeurons.Add(n);

			foreach (Neuron n in sortedNeurons)
			{
				n.incomingArray = n.incomingConnections.ToArray();
				n.outgoingArray = n.outgoingConnections.ToArray();
				n.dataFields = n.dataFieldsList.ToArray();
			}

			return new Brain(sortedNeurons, connections);
		}

		public static Neuron GetOrCreateNeuron
			(Dictionary<uint, Neuron> neurons,
			NeuronGeneData geneData)
		{
			if (neurons.TryGetValue(geneData.ID, out Neuron existing))
			{
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
			foreach (DataField field in NeuronDicts.DataDefinitions[(int)neuronData.func])
			{
				if (field.fieldType == FType.Float || field.fieldType == FType.SignedFloat)
				{
					float floatValue = GeneTools.DecodeField(neuronData.data, field);
					NeuronDataField data = new(field.fieldType, floatVal: floatValue);
					data.name = field.name;
					datas.Add(data);
				}
				else if (field.fieldType == FType.Bool)
				{
					bool boolValue = GeneTools.DecodeField(neuronData.data, field) != 0;
					NeuronDataField data = new(FType.Bool, boolVal: boolValue);
					data.name = field.name;
					datas.Add(data);
				}
				else
				{
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

		public static float FastTanh(float x)
		{
			return x / (1f + MathF.Abs(x));
		}

		public static void RenderGraph(Brain brain, string graphID, bool isDead = false, bool isPaused = false, bool isTracking = false)
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
				foreach (var field in neuron.dataFields)
				{
					label += "\n" + field.ToString();
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
				isDead = isDead,
				isPaused = isPaused,
				isTracking = isTracking,
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
