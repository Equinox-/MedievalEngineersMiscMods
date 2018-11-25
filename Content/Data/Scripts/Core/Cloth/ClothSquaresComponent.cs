using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Xml.Serialization;
using Sandbox.Game.Entities;
using Sandbox.Game.GameSystems;
using Sandbox.ModAPI;
using VRage;
using VRage.Components;
using VRage.Components.Entity.Animations;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Definitions;
using VRage.Game.Entity;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.Library.Logging;
using VRage.ObjectBuilders;
using VRage.Session;
using VRage.Utils;
using VRageMath;

namespace Equinox76561198048419394.Core.Cloth
{
    [MyComponent(typeof(MyObjectBuilder_ClothSquaresComponent))]
    [MyDefinitionRequired]
    public class ClothSquaresComponent : MyEntityComponent
    {
        public static readonly Vector3 WindVector = new Vector3(1, 0, 0);
        public static readonly float WindStrength = 0.3f;

        public override void OnAddedToScene()
        {
            base.OnAddedToScene();
        }

        public override void OnRemovedFromScene()
        {
            base.OnRemovedFromScene();
        }

        private readonly MyStringId _mtl = MyStringId.GetOrCompute("CapeTest");
        private readonly MyStringId _square = MyStringId.GetOrCompute("Square");

        private MatrixD? _prevTickPosition;
        private bool _initialRun = true;

        [FixedUpdate]
        public void Render()
        {
            if (MyAPIGateway.Utilities.IsDedicated)
                return;
            var gravity = Vector3.TransformNormal(MyGravityProviderSystem.CalculateTotalGravityInPoint(Entity.GetPosition()),
                Entity.PositionComp.WorldMatrixNormalizedInv);
            Vector3 windDir = Vector3.TransformNormal(WindVector, Entity.PositionComp.WorldMatrixNormalizedInv);
            float windStrength = 0;
            {
//                float3 CalculateWindOffset(float3 position)
//                {
//                    const float3 wind_d = frame_.Foliage.wind_vec;
//                    if ( !any(wind_d) )
//                        return 0;
//
//                    float4 freq = float4(1.975, 0.973, 0.375, 0.193);
//                    float4 x = mad(frame_.frameTime, length(wind_d), dot(normalize(wind_d), position));
//                    float4 waves = smooth_triangle_wave(freq * x);
//
//                    return normalize(wind_d) * dot(waves, 0.25);
//                }

//                float4 smooth_curve( float4 x ) 
//                {
//                    return x * x *( 3.0 - 2.0 * x );  
//                }  
//
//                float4 triangle_wave( float4 x ) 
//                {
//                    return abs( frac( x + 0.5 ) * 2.0 - 1.0 );  
//                }  
//
//                float4 smooth_triangle_wave( float4 x ) 
//                {
//                    return smooth_curve( triangle_wave( x ) );  
//                }  
                var freq = new Vector4(1.975f, 0.973f, 0.375f, 0.193f);
                var x = (float) MySession.Static.ElapsedGameTime.TotalSeconds * WindStrength + (float) ((Vector3D) WindVector).Dot(Entity.GetPosition());
                var waves = freq * x;
                waves = new Vector4(Math.Abs(((waves.X + 0.5f) % 1) * 2 - 1), Math.Abs(((waves.Y + 0.5f) % 1) * 2 - 1),
                    Math.Abs(((waves.Z + 0.5f) % 1) * 2 - 1), Math.Abs(((waves.W + 0.5f) % 1) * 2 - 1));
                waves = waves * waves * (new Vector4(3) - new Vector4(2) * waves);

                windStrength = (waves.X + waves.Y + waves.Z + waves.W) * 0.25f;
            }


            var parent = Entity;
            var parentRenderObj = parent.Get<MyRenderComponentBase>();
            while (parentRenderObj == null && parent != null)
            {
                parent = parent.Parent;
                parentRenderObj = parent.Get<MyRenderComponentBase>();
            }

            if (parentRenderObj == null)
                return;

            var skeleton = Entity.Get<MySkeletonComponent>();

            if (_prevTickPosition.HasValue)
            {
                var pp = _prevTickPosition.Value;
                // Apply a transform to the cloth such that:  transform(CurrentPos, ClothDataNew) == transform(PrevPos, ClothDataOld)
                var transform = (Matrix) (pp * Entity.PositionComp.WorldMatrixInvScaled);
                foreach (var cloth in _cloths)
                    cloth.ApplyTransform(transform);
            }

            _prevTickPosition = Entity.PositionComp.WorldMatrix;
            for (var id = 0; id < _cloths.Length; id++)
            {
                var cloth = _cloths[id];
                var section = Definition.Quads[id];
                if (skeleton != null)
                    foreach (var binding in section.Bindings)
                    {
                        int boneIndex;
                        var bone = skeleton.FindBone(binding.Bone, out boneIndex);
                        if (bone == null)
                            continue;

                        for (var yo = 0; yo < binding.YCount; yo++)
                        {
                            var by = binding.Y + yo;
                            var fy = by / (float) (section.Def.ResY - 1);
                            var p0 = Vector3.Lerp(section.Def.V00, section.Def.V01, fy);
                            var p1 = Vector3.Lerp(section.Def.V10, section.Def.V11, fy);
                            for (var xo = 0; xo < binding.XCount; xo++)
                            {
                                var bx = binding.X + xo;
                                var idx = section.Def.ResX * by + bx;
                                var fx = bx / (float) (section.Def.ResX - 1);
                                var bindPos = Vector3.Lerp(p0, p1, fx);
                                var bindPosRel = Vector3.Transform(bindPos, ref bone.Transform.AbsoluteBindTransformInv);
                                var currPos = Vector3.Transform(bindPosRel, ref skeleton.BoneAbsoluteTransforms[boneIndex]);
                                cloth.Particles[idx].Position = currPos;
                            }
                        }
                    }

                var region = cloth.CalculateInflatedBox(MyEngineConstants.UPDATE_STEPS_PER_SECOND * 2f);
                var regionWorld = region.Transform(Entity.WorldMatrix);
                var entities = MyEntities.GetEntitiesInAABB(ref regionWorld);
                cloth.SphereColliders.Clear();
                cloth.CapsuleColliders.Clear();
                var invWorld = Entity.PositionComp.WorldMatrixInvScaled;
                foreach (var ent in entities)
                foreach (var collider in ent.Components.GetComponents<ClothColliderComponent>())
                {
                    Cloth.Sphere[] spheres;
                    Cloth.Capsule[] capsules;
                    collider.GetColliders(out spheres, out capsules);
                    var conv = (Matrix) (ent.WorldMatrix * invWorld);
                    foreach (var s in spheres)
                        cloth.SphereColliders.Add(new Cloth.Sphere(Vector3.Transform(s.P0, conv), s.Radius));
                    foreach (var c in capsules)
                        cloth.CapsuleColliders.Add(new Cloth.Capsule(new Line
                        {
                            From = Vector3.Transform(c.Line.From, conv),
                            To = Vector3.Transform(c.Line.To, conv),
                            Direction = Vector3.TransformNormal(c.Line.Direction, conv),
                            Length = c.Line.Length,
                            BoundingBox = c.Line.BoundingBox
                        }, c.Radius));
                }

                cloth.Gravity = gravity;
                cloth.WindDirection = windDir;
                cloth.WindStrength = windStrength;
                cloth.Simulate();

                var qs = cloth.QuadStream;
                var pts = cloth.Particles;
                for (var i = 0; i < qs.Length; i += 4)
                {
//                    var quad = new MyQuadD
//                    {
//                        Point0 = pts[qs[i]].Position,
//                        Point1 = pts[qs[i + 1]].Position,
//                        Point2 = pts[qs[i + 2]].Position,
//                        Point3 = pts[qs[i + 3]].Position
//                    };
//                    MyTransparentGeometry.AddAttachedQuad(_mtl, ref quad, Vector4.One, ref quad.Point0, parentRenderObj.GetRenderObjectID());
                    var pt0 = pts[qs[i]];
                    var pt1 = pts[qs[i + 1]];
                    var pt2 = pts[qs[i + 2]];
                    var pt3 = pts[qs[i + 3]];
                    MyTransparentGeometry.AddTriangleBillboard(pt0.Position, pt1.Position, pt2.Position, -pt0.Normal, -pt1.Normal, -pt2.Normal, pt0.Uv, pt1.Uv,
                        pt2.Uv, _mtl, parentRenderObj.GetRenderObjectID(), pt0.Position);

                    MyTransparentGeometry.AddTriangleBillboard(pt0.Position, pt2.Position, pt3.Position, -pt0.Normal, -pt2.Normal, -pt3.Normal, pt0.Uv, pt2.Uv,
                        pt3.Uv, _mtl, parentRenderObj.GetRenderObjectID(), pt3.Position);
//                    quad.Point0 = Vector3D.Transform(quad.Point0, Entity.WorldMatrix);
//                    quad.Point1 = Vector3D.Transform(quad.Point1, Entity.WorldMatrix);
//                    quad.Point2 = Vector3D.Transform(quad.Point2, Entity.WorldMatrix);
//                    quad.Point3 = Vector3D.Transform(quad.Point3, Entity.WorldMatrix);
//                    MyTransparentGeometry.AddQuad(_mtl, ref quad, Vector4.One, ref quad.Point0);
                }
            }

            _initialRun = false;
        }


        public ClothSquaresComponentDefinition Definition { get; private set; }
        private Cloth[] _cloths;

        public override void Init(MyEntityComponentDefinition def)
        {
            base.Init(def);
            Definition = (ClothSquaresComponentDefinition) def;
            _cloths = Definition.Quads.Select(x => Cloth.CreateQuad(x.Def)).ToArray();
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_ClothSquaresComponent : MyObjectBuilder_EntityComponent
    {
    }

    [MyDefinitionType(typeof(MyObjectBuilder_ClothSquaresComponentDefinition))]
    public class ClothSquaresComponentDefinition : MyEntityComponentDefinition
    {
        public struct ClothSection
        {
            public readonly ClothQuadDefinition Def;

            public readonly Binding[] Bindings;

            public struct Binding
            {
                public readonly int X, Y;
                public readonly int XCount, YCount;
                public readonly string Bone;

                public Binding(MyObjectBuilder_ClothSquaresComponentDefinition.ClothSquare.PointRef def)
                {
                    X = def.X;
                    Y = def.Y;
                    XCount = def.XCount;
                    YCount = def.YCount;
                    Bone = def.Bone;
                }
            }

            public ClothSection(MyObjectBuilder_ClothSquaresComponentDefinition.ClothSquare sq)
            {
                Def = new ClothQuadDefinition(sq);
                Bindings = sq.Pins.Where(x => !string.IsNullOrWhiteSpace(x.Bone)).Select(x => new Binding(x)).ToArray();
            }
        }

        public ClothSection[] Quads;

        protected override void Init(MyObjectBuilder_DefinitionBase def)
        {
            base.Init(def);
            var ob = (MyObjectBuilder_ClothSquaresComponentDefinition) def;
            Quads = ob.Squares.Select(x => new ClothSection(x)).ToArray();
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_ClothSquaresComponentDefinition : MyObjectBuilder_EntityComponentDefinition
    {
        public class ClothSquare
        {
            public int ResX;
            public int ResY;

            public float Mass;

            public float TensionStrength;
            public float ShearStrength;
            public float StructuralStrength;

            [DefaultValue(0.95f)]
            public float Damping = 0.95f;

            public SerializableVector3 V00, V01, V10, V11;

            public class PointRef
            {
                [XmlAttribute("x")]
                public int X;

                [XmlAttribute("y")]
                public int Y;

                [XmlAttribute("XCount")]
                [DefaultValue(1)]
                public int XCount = 1;

                [XmlAttribute("YCount")]
                [DefaultValue(1)]
                public int YCount = 1;

                [XmlAttribute]
                public string Bone;
            }

            [XmlElement("Pin")]
            public PointRef[] Pins;
        }

        [XmlElement("Square")]
        public ClothSquare[] Squares;
    }
}