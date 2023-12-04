using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using Equinox76561198048419394.Cartography.MapLayers;
using Equinox76561198048419394.Cartography.Utils;
using Equinox76561198048419394.Core.Util;
using Equinox76561198048419394.Core.Util.Memory;
using Medieval.ObjectBuilders.Components.Grid;
using ObjectBuilders.Definitions.GUI;
using Sandbox.Game.Entities.Planet;
using Sandbox.ModAPI;
using VRage;
using VRage.Collections;
using VRage.Collections.Graph;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Library.Collections;
using VRage.ObjectBuilders.Components.Entity.Grid;
using VRageMath;

namespace CartographyBaker;

public static class BendyBaker
{
    public class EdgeType
    {
        public string Material;
    }

    private readonly struct EdgeState : IEquatable<EdgeState>
    {
        public readonly EdgeType Type;
        public readonly bool Built;

        public EdgeState(bool built, EdgeType type)
        {
            Built = built;
            Type = type;
        }

        public bool Equals(EdgeState other) => Equals(Type, other.Type) && Built == other.Built;

        public override bool Equals(object obj) => obj is EdgeState other && Equals(other);

        public override int GetHashCode() => ((Type != null ? Type.GetHashCode() : 0) * 397) ^ Built.GetHashCode();
    }

    private sealed class BendyGraphCollector
    {
        public const float NodeMergeDistance = 1.5f;
        public const float NodeMergeDistanceSq = NodeMergeDistance * NodeMergeDistance;
        private readonly MyDynamicAABBTreeD _nodes = new();
        private readonly Dictionary<GraphEdge<int>, EdgeState> _edgeStates = new(GraphEdge<int>.Comparer);
        public readonly AlGraph<int> Graph = new();
        public DictionaryReader<GraphEdge<int>, EdgeState> EdgeStates => _edgeStates;

        public int? NearestNode(Vector3D v, double maxDistanceSq = double.PositiveInfinity)
        {
            using var e = _nodes.EquiSortedByDistance(v);
            if (e.MoveNext())
                return e.Current.NodeIndex;
            return null;
        }

        public Vector3D NodePosition(int id) => _nodes.GetAabb(id).Center;

        public int GetOrCreateNode(Vector3D pos)
        {
            var nearest = NearestNode(pos, NodeMergeDistanceSq);
            if (nearest != null && Vector3D.DistanceSquared(NodePosition(nearest.Value), pos) < NodeMergeDistanceSq)
                return nearest.Value;

            var box = BoundingBoxD.CreatePoint(pos).Inflate(.005f);
            var id = _nodes.AddProxy(in box, null, 0);
            Graph.AddVertex(id);
            return id;
        }

        public void AddEdge(int from, int to, EdgeState state)
        {
            if (from != to)
                Graph.AddEdge(from, to);
            _edgeStates[new GraphEdge<int>(from, to)] = state;
        }
    }

    private sealed class TopLevelEdge
    {
        public readonly EqReadOnlySpan<int> Nodes;
        public readonly double Length;
        public readonly EdgeState State;

        public TopLevelEdge(EqReadOnlySpan<int> nodes, double length, EdgeState state)
        {
            Nodes = nodes;
            Length = length;
            State = state;
        }
    }

    private sealed class SimplifiedGraph
    {
        public readonly BendyGraphCollector Raw;
        private readonly AlGraph<int> _topLevelGraph;
        public readonly MyListDictionary<GraphEdge<int>, TopLevelEdge> TopLevelEdges = new(GraphEdge<int>.Comparer);
        public readonly MyListDictionary<GraphEdge<int>, TopLevelEdge> TopLevelSimplifiedEdges = new(GraphEdge<int>.Comparer);

        public IReadOnlyGraph<int> TopLevelGraph => _topLevelGraph;

        public SimplifiedGraph(BendyGraphCollector raw)
        {
            Raw = raw;
            _topLevelGraph = new AlGraph<int>();
            var full = Raw.Graph;
            foreach (var vert in full.Vertices)
            {
                var adjacent = full.GetAdjacentVertices(vert);
                if (adjacent.Count != 2)
                {
                    _topLevelGraph.AddVertex(vert);
                    continue;
                }

                using var e = adjacent.GetEnumerator();
                if (!e.MoveNext())
                    throw new Exception();
                var firstState = Raw.EdgeStates[new GraphEdge<int>(vert, e.Current)];
                if (!e.MoveNext())
                    throw new Exception();
                var secondState = Raw.EdgeStates[new GraphEdge<int>(vert, e.Current)];
                if (e.MoveNext())
                    throw new Exception();
                if (!firstState.Equals(secondState))
                    _topLevelGraph.AddVertex(vert);
            }

            using var handle2 = PoolManager.Get(out HashSet<int> consumedVertices);
            foreach (var vert in _topLevelGraph.Vertices)
                CollectStartingAt(vert, consumedVertices);
            SimplifyEdges();
        }

        private void CollectStartingAt(int start, HashSet<int> consumedVertices)
        {
            using var handle1 = PoolManager.Get(out List<int> temp);
            var adjacent = Raw.Graph.GetAdjacentVertices(start);
            consumedVertices.Add(start);
            foreach (var adj in adjacent)
            {
                if (consumedVertices.Contains(adj))
                    continue;
                temp.Clear();
                temp.Add(start);
                var prevPos = Raw.NodePosition(start);
                var prev = start;
                var curr = adj;
                var length = 0.0;
                EdgeState? prevEdgeState = null;
                while (true)
                {
                    var edgeState = Raw.EdgeStates[new GraphEdge<int>(prev, curr)];
                    if (prevEdgeState != null && !edgeState.Equals(prevEdgeState.Value))
                        throw new Exception();

                    temp.Add(curr);
                    var wasAlreadyConsumed = !consumedVertices.Add(curr);
                    var currPos = Raw.NodePosition(curr);
                    length += Vector3D.Distance(in prevPos, in currPos);

                    if (_topLevelGraph.ContainsVertex(curr))
                    {
                        TopLevelEdges.Add(
                            new GraphEdge<int>(start, curr),
                            new TopLevelEdge(new EqReadOnlySpan<int>(temp.ToArray()), length, edgeState));
                        _topLevelGraph.AddVertex(curr);
                        if (start != curr)
                            _topLevelGraph.AddEdge(start, curr);
                        break;
                    }

                    if (wasAlreadyConsumed)
                        throw new Exception("Already consumed");

                    var next = -1;
                    var currAdjacent = Raw.Graph.GetAdjacentVertices(curr);
                    foreach (var e in currAdjacent)
                    {
                        if (e == prev) continue;
                        next = e;
                        break;
                    }

                    if (next == -1)
                        throw new Exception("Failed to find next step on route");

                    prev = curr;
                    curr = next;
                    prevPos = currPos;
                    prevEdgeState = edgeState;
                }
            }
        }

        private void SimplifyEdges()
        {
            using var handle1 = PoolManager.Get(out List<int> temp);
            using var handle2 = PoolManager.Get(out List<Vector3D> pts);
            foreach (var edge in TopLevelEdges)
            foreach (var edgeData in edge.Value)
            {
                var nodes = edgeData.Nodes;
                if (nodes.Length <= 2)
                {
                    TopLevelSimplifiedEdges.Add(edge.Key, edgeData);
                    continue;
                }

                pts.Clear();
                pts.EnsureCapacity(nodes.Length);
                foreach (var pt in nodes)
                    pts.Add(Raw.NodePosition(pt));
                var ptSpan = pts.AsEqSpan();
                LineSimplifier.SimplifySequenceWithNan(ptSpan, 10 * 10);

                temp.Clear();
                for (var i = 0; i < ptSpan.Length; i++)
                {
                    if (double.IsNaN(ptSpan[i].X))
                        continue;
                    temp.Add(nodes[i]);
                }

                TopLevelSimplifiedEdges.Add(edge.Key,
                    new TopLevelEdge(new EqReadOnlySpan<int>(temp.ToArray()), edgeData.Length, edgeData.State));
            }
        }
    }

    public enum LayerType
    {
        Standard,
        Narrow
    }

    public class BendyInfo
    {
        public LayerType Layer;
        public EdgeType EdgeType;
    }

    public class BlockInfo
    {
        public LayerType Layer;
        public float GridSize;
        public BoundingBoxI Bounds;
        public Vector3[] Segments;
        public EdgeType EdgeType;
    }

    private const float ActivationLevel = 0.95f;

    private static IDictionary<LayerType, BendyGraphCollector> CollectLayers(string saveFile)
    {
        var steelType = new EdgeType { Material = "Steel" };
        var woodType = new EdgeType { Material = "Wood" };
        var lgStraightSteelBlockInfo = new BlockInfo
        {
            Layer = LayerType.Standard,
            GridSize = 2.5f,
            Bounds = new BoundingBoxI(new Vector3I(0), new Vector3I(4, 1, 1)),
            Segments = new[] { new Vector3(-5, -1.25f, 0), new Vector3(5, -1.25, 0) }, EdgeType = steelType
        };
        var sgStraightSteelBlockInfo = new BlockInfo
        {
            Layer = LayerType.Standard,
            GridSize = .25f,
            Bounds = new BoundingBoxI(new Vector3I(0, -9, 0), new Vector3I(40, -8, 10)),
            Segments = lgStraightSteelBlockInfo.Segments, EdgeType = steelType
        };

        var bendySubtypes = new Dictionary<string, BendyInfo>
        {
            ["RailSteelBendy"] = new() { Layer = LayerType.Standard, EdgeType = steelType },
            ["RailWoodBendy"] = new() { Layer = LayerType.Standard, EdgeType = woodType },
            ["MiningSleeper_Bendy"] = new() { Layer = LayerType.Narrow, EdgeType = steelType },
        };
        var blockSubtypes = new Dictionary<string, BlockInfo>
        {
            ["RailSteelStraight"] = lgStraightSteelBlockInfo,
            ["RailSteelMaintStraight"] = lgStraightSteelBlockInfo,
            ["RailSteelStraightSmall"] = sgStraightSteelBlockInfo,
            ["RailSteelDynStraight40_Small"] = sgStraightSteelBlockInfo,
            ["RailSteelMaintStraightSmall"] = sgStraightSteelBlockInfo,
        };
        var graphs = new ConcurrentDictionary<LayerType, BendyGraphCollector>();
        SaveFileAccessor.Entities(saveFile)
            .ForEach(entity =>
            {
                var subtype = entity.Subtype;
                var gridWorldMatrix = entity.Position.GetMatrix();
                // Process as bendy
                if (subtype != null && bendySubtypes.TryGetValue(subtype, out var bendyInfo))
                {
                    var bendyNodes = entity.Component("MyObjectBuilder_BendyComponent")?.OfType<XmlNode>()
                        .Where(node => node.Name == "Node")
                        .ToList();
                    if (bendyNodes is not { Count: 2 })
                        return;
                    var constructable = entity.Component("MyObjectBuilder_ConstructableComponent")?.Attributes?["BInteg"]?.InnerText ?? "1.0";
                    var constructed = !float.TryParse(constructable, out var integrity) || integrity >= ActivationLevel;

                    Vector3D NodePosition(XmlNode node) => Vector3D.Transform((Vector3)node["Position"].DeserializeAs<SerializableVector3>(), gridWorldMatrix);

                    var graph = graphs.GetOrAdd(bendyInfo.Layer, _ => new BendyGraphCollector());
                    var from = graph.GetOrCreateNode(NodePosition(bendyNodes[0]));
                    var to = graph.GetOrCreateNode(NodePosition(bendyNodes[1]));
                    if (from != to)
                        graph.AddEdge(from, to, new EdgeState(constructed, bendyInfo.EdgeType));
                }

                // Process as grid
                var gridData = entity.Component(nameof(MyObjectBuilder_GridDataComponent));
                var gridBuildingComponent = entity.Component<MyObjectBuilder_GridBuildingComponent>()?.StoredStates?.ToDictionary(
                                                x => x.BlockId,
                                                x => x.BuildIntegrity)
                                            ?? new Dictionary<ulong, uint>();
                var blocks = gridData?["Blocks"];
                if (blocks != null)
                {
                    foreach (var block in blocks.OfType<XmlNode>())
                    {
                        var subtypeName = block["DefinitionId"]?.Attributes?["Subtype"]?.Value;
                        if (subtypeName != null && blockSubtypes.TryGetValue(subtypeName, out var blockInfo))
                        {
                            var id = ulong.Parse(block["Id"]!.InnerText);
                            var min = block["Min"].DeserializeAs<SerializableVector3I>();
                            var orientation = block["Orientation"].DeserializeAs<SerializableBlockOrientation>();
                            var blockMatrix = GetBlockLocalMatrix(blockInfo, min, orientation);
                            var blockWorldMatrix = blockMatrix * gridWorldMatrix;
                            var segments = blockInfo.Segments;
                            var graph = graphs.GetOrAdd(blockInfo.Layer, _ => new BendyGraphCollector());
                            var constructed = !gridBuildingComponent.TryGetValue(id, out var integrity) || integrity >= ActivationLevel;
                            for (var i = 0; i < segments.Length - 1; i += 2)
                            {
                                var from = graph.GetOrCreateNode(Vector3D.Transform(segments[i], in blockWorldMatrix));
                                var to = graph.GetOrCreateNode(Vector3D.Transform(segments[i + 1], in blockWorldMatrix));
                                graph.AddEdge(from, to, new EdgeState(constructed, blockInfo.EdgeType));
                            }
                        }
                    }
                }
            });
        return graphs;
    }

    private readonly struct LayerArgs
    {
        public readonly EdgeState State;
        public readonly LayerType Layer;

        public LayerArgs(LayerType layer, EdgeState state)
        {
            Layer = layer;
            State = state;
        }
    }

    public static void Generate(string saveFile)
    {
        var layers = CollectLayers(saveFile)
            .ToDictionary(x => x.Key, x => new SimplifiedGraph(x.Value));
        var linesFactory = (LayerArgs args) =>
        {
            // "1*16,5*2"
            var innerSize = args.Layer == LayerType.Standard ? 3 : 1;
            var spokeSize = args.Layer == LayerType.Standard ? 7 : 5;

            var color = args.Layer switch
            {
                LayerType.Standard => new ColorDefinitionRGBA(255, 0, 0, 200),
                LayerType.Narrow => new ColorDefinitionRGBA(255, 0, 255, 200),
                _ => throw new ArgumentOutOfRangeException()
            };

            if (!args.State.Built)
                color = new ColorDefinitionRGBA(255, 128, 0, 200);
            var ob = new MyObjectBuilder_ShapesLayerLine
            {
                StrokeColor = color,
                StrokeWidth = $"{innerSize}*16,{spokeSize}*2",
                Tooltip = new List<TooltipLine>
                {
                    new() { Content = $"{args.Layer} Gauge", Title = true },
                },
                Lines = new List<MyObjectBuilder_ShapesLayerCoordinates>(),
                Visibility = CustomMapLayerVisibility.Both,
            };
            if (!args.State.Built)
                ob.Tooltip.Add(new TooltipLine { Content = "Not Built" });
            return ob;
        };
        var lines = new ConcurrentDictionary<LayerArgs, MyObjectBuilder_ShapesLayerLine>();

        foreach (var layer in layers)
        foreach (var info in layer.Value.TopLevelSimplifiedEdges)
        {
            var isLeftTerminal = layer.Value.TopLevelGraph.GetAdjacentVertices(info.Key.Left).Count <= 1;
            var isRightTerminal = layer.Value.TopLevelGraph.GetAdjacentVertices(info.Key.Right).Count <= 1;
            double minLengthToInclude;
            if (isLeftTerminal && isRightTerminal)
                minLengthToInclude = 50;
            else if (isLeftTerminal || isRightTerminal)
                minLengthToInclude = 25;
            else
                minLengthToInclude = 0;

            foreach (var edge in info.Value)
            {
                if (edge.Length < minLengthToInclude)
                    continue;
                var line = lines.GetOrAdd(new LayerArgs(layer.Key, edge.State), linesFactory);
                MyObjectBuilder_ShapesLayerCoordinates temp = null;
                foreach (var i in edge.Nodes)
                {
                    var pos = layer.Value.Raw.NodePosition(i);
                    MyEnvironmentCubemapHelper.ProjectToCube(ref pos, out var face, out var uv);
                    if (temp != null && face == temp.Face)
                    {
                        temp.Points.Add((Vector2)uv);
                        continue;
                    }

                    if (temp != null && temp.Face != -1 && temp.Points.Count > 1) line.Lines.Add(temp);

                    temp = new MyObjectBuilder_ShapesLayerCoordinates
                    {
                        Face = face,
                        Points = new List<SerializableVector2> { (Vector2)uv }
                    };
                }

                if (temp != null && temp.Face != -1 && temp.Points.Count > 1) line.Lines.Add(temp);
            }
        }

        var definition = new MyObjectBuilder_EquiShapesMapLayerDefinition
        {
            Lines = lines.Values.ToList()
        };
        var result = ((IMyUtilities)MyAPIUtilities.Static).SerializeToXML(definition);
        result = Regex.Replace(result, "\\s+<\\w+ xsi:nil=\"true\" \\/>", string.Empty);
        Console.WriteLine(result);
    }

    private static void GetRange(Vector3I position, MyBlockOrientation orientation, in BoundingBoxI bounds, out Vector3I min, out Vector3I max)
    {
        var blockMin = bounds.Min;
        var blockMax = bounds.Max;

        var orientFwd = Base6Directions.GetIntVector(orientation.Forward);
        var orientRight = -Base6Directions.GetIntVector(orientation.Left);
        var orientUp = Base6Directions.GetIntVector(orientation.Up);

        var fwdOffset = Vector3I.Clamp(orientFwd, Vector3I.Zero, Vector3I.One);
        var rightOffset = Vector3I.Clamp(-orientRight, Vector3I.Zero, Vector3I.One);
        var upOffset = Vector3I.Clamp(-orientUp, Vector3I.Zero, Vector3I.One);

        var forwardMax = -orientFwd * blockMax.Z;
        var rightMax = orientRight * blockMax.X;
        var upMax = orientUp * blockMax.Y;
        var forwardMin = -orientFwd * blockMin.Z;
        var rightMin = orientRight * blockMin.X;
        var upMin = orientUp * blockMin.Y;

        var firstCorner = position + forwardMin + rightMin + upMin + fwdOffset + rightOffset + upOffset;
        var otherCorner = position + forwardMax + rightMax + upMax + fwdOffset + rightOffset + upOffset;

        Vector3I.Min(firstCorner, otherCorner, out min);
        Vector3I.Max(firstCorner, otherCorner, out max);
    }

    private static Matrix GetBlockLocalMatrix(BlockInfo blockInfo, Vector3I blockMin, MyBlockOrientation orientation)
    {
        GetRange(Vector3I.Zero, orientation, blockInfo.Bounds, out var min, out var max);
        var position = blockMin - min;
        GetRange(position, orientation, blockInfo.Bounds, out min, out max);
        var center = (min + max) * blockInfo.GridSize / 2f;
        orientation.GetMatrix(out var result);
        result.Translation = center;
        return result;
    }
}