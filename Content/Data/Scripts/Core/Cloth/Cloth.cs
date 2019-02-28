using System;
using System.Collections.Generic;
using VRage.Game;
using VRageMath;

namespace Equinox76561198048419394.Core.Cloth
{
    public class Cloth
    {
        public struct ClothParticle
        {
            public Vector3 Position;
            public Vector3 Velocity;

            public bool Pinned;
            public Vector3 Tension;
            public float InverseMass;

            public Vector3 Normal;
            public float Area;
            public Vector2 Uv;
        }

        public struct ClothSpring
        {
            public int P0, P1;
            public float Length;
            public float InvLength;
            public float Stiffness;
        }

        public struct Capsule
        {
            public Line Line;
            public float Radius, RadiusSq;

            public Capsule(Vector3 a, Vector3 b, float rad)
            {
                Line = new Line(a, b, false);
                Radius = rad;
                RadiusSq = rad * rad;
            }

            public Capsule(Line line, float rad)
            {
                Line = line;
                Radius = rad;
                RadiusSq = rad * rad;
            }
        }

        public struct Sphere
        {
            public Vector3 P0;
            public float Radius, RadiusSq;

            public Sphere(Vector3 pos, float rad)
            {
                P0 = pos;
                Radius = rad;
                RadiusSq = rad * rad;
            }
        }

        public readonly ClothParticle[] Particles;
        public readonly ClothSpring[] Springs;
        public readonly int[] QuadStream;
        public float Damping;
        public Vector3 Gravity;
        public Vector3 WindDirection;
        public float WindStrength;
        public BoundingBox Box;

        public readonly List<Sphere> SphereColliders = new List<Sphere>();
        public readonly List<Capsule> CapsuleColliders = new List<Capsule>();

        private Cloth(ClothParticle[] particles, ClothSpring[] springs, int[] quadStream)
        {
            Particles = particles;
            Springs = springs;
            QuadStream = quadStream;
        }

        public void ApplyTransform(Matrix transform)
        {
            for (var i = 0; i < Particles.Length; i++)
            {
                var p = Particles[i];
                if (p.Pinned)
                    continue;
                Vector3.Transform(ref p.Position, ref transform, out Particles[i].Position);
                Vector3.TransformNormal(ref p.Velocity, ref transform, out Particles[i].Velocity);
            }
        }

        private const float _gravityMultiplier = 5;
        private const float _windMultiplier = 100f;

        public void Simulate()
        {
            const float dt = MyEngineConstants.UPDATE_STEP_SIZE_IN_SECONDS;
            const int steps = 10;
            var dampingApplied = (float) Math.Pow(Damping, 1.0f / steps);
            for (var i = 0; i < steps; i++)
                SimulateOne(dt / steps, dampingApplied);
            Box = CalculateInflatedBox(MyEngineConstants.UPDATE_STEPS_PER_SECOND);
        }

        public BoundingBox CalculateInflatedBox(float dt)
        {
            var tmp = BoundingBox.CreateInvalid();
            for (var i = 0; i < Particles.Length; i++)
                tmp = tmp.Include(Particles[i].Position + Particles[i].Velocity * dt);
            return tmp;
        }

        private const float _maxVelocity = 100;
        private const float _maxPosition = 1000;

        private static bool IsInvalidFloat(float f)
        {
            return float.IsNaN(f) || float.IsInfinity(f) || float.IsPositiveInfinity(f) || float.IsNegativeInfinity(f);
        }

        private void SimulateOne(float dt, float damping, bool collide = true)
        {
            // ReSharper disable once ForCanBeConvertedToForeach
            for (var i = 0; i < Springs.Length; i++)
            {
                var s = Springs[i];
                var tensile = (Particles[s.P0].Position + Particles[s.P0].Velocity * dt) - (Particles[s.P1].Position + Particles[s.P1].Velocity * dt);
                var len = tensile.Normalize();
                var ext = len - s.Length;
                var tension = s.Stiffness * ext * s.InvLength;

                tensile *= tension;
                Particles[s.P0].Tension -= tensile;
                Particles[s.P1].Tension += tensile;
            }

            for (var i = 0; i < Particles.Length; i++)
            {
                var p = Particles[i];
                if (p.Pinned) continue;
                
                var acceleration = _gravityMultiplier * Gravity + p.Tension * p.InverseMass;
                acceleration += _windMultiplier * WindDirection * WindStrength * Math.Abs(Vector3.Dot(WindDirection, p.Normal)) * p.Area;
                var nextVelocity = (p.Velocity + acceleration * dt) * damping;
                var motion = nextVelocity * dt;
                var motionDir = Vector3.Normalize(motion);
                var nextPos = p.Position + motion;
                if (collide)
                {
                    foreach (var k in SphereColliders)
                        if (Vector3.DistanceSquared(k.P0, nextPos) < k.RadiusSq)
                        {
                            var del = nextPos - k.P0;
                            del.Normalize();
                            nextPos = k.P0 + del * k.Radius;
                            nextVelocity -= del * nextVelocity.Dot(del);
                        }

                    foreach (var k in CapsuleColliders)
                    {
                        var nearPoint = k.Line.From + k.Line.Direction *
                                        MathHelper.Clamp(Vector3.Dot(nextPos - k.Line.From, k.Line.Direction), 0, k.Line.Length);
                        // ReSharper disable once InvertIf
                        if (Vector3.DistanceSquared(nearPoint, nextPos) < k.RadiusSq)
                        {
                            var del = nextPos - nearPoint;
                            del.Normalize();
                            nextPos = nearPoint + del * k.Radius;
                            nextVelocity -= del * nextVelocity.Dot(del);
                        }
                    }
                }
                    
                    
                if (IsInvalidFloat(nextVelocity.X) || Math.Abs(nextVelocity.X) > _maxVelocity)
                    nextVelocity.X = 0;
                if (IsInvalidFloat(nextVelocity.Y) || Math.Abs(nextVelocity.Y) > _maxVelocity)
                    nextVelocity.Y = 0;
                if (IsInvalidFloat(nextVelocity.Z) || Math.Abs(nextVelocity.Z) > _maxVelocity)
                    nextVelocity.Z = 0;
                if (IsInvalidFloat(nextPos.X) || Math.Abs(nextPos.X) > _maxPosition)
                    nextPos.X = 0;
                if (IsInvalidFloat(nextPos.Y) || Math.Abs(nextPos.Y) > _maxPosition)
                    nextPos.Y = 0;
                if (IsInvalidFloat(nextPos.Z) || Math.Abs(nextPos.Z) > _maxPosition)
                    nextPos.Z = 0;

                Particles[i].Position = nextPos;
                Particles[i].Velocity = nextVelocity;
                Particles[i].Tension = Vector3.Zero;
            }
        }

        public static Cloth CreateQuad(ClothQuadDefinition def)
        {
            var resX = def.ResX;
            var resY = def.ResY;

            var pts = new ClothParticle[resX * resY];
            for (var y = 0; y < resY; y++)
            {
                var fy = y / (float) (resY - 1);
                var p0 = Vector3.Lerp(def.V00, def.V01, fy);
                var p1 = Vector3.Lerp(def.V10, def.V11, fy);
                for (var x = 0; x < resX; x++)
                {
                    var fx = x / (float) (resX - 1);
                    var p = new ClothParticle {Position = Vector3.Lerp(p0, p1, fx), Velocity = Vector3.Zero, Tension = Vector3.Zero, Uv = new Vector2(fx, fy)};
                    pts[y * resX + x] = p;
                }
            }

            #region Pin Particles

            foreach (var pin in def.Pins)
                pts[pin.Y * resX + pin.X].Pinned = true;

            #endregion

            #region Calculate Particle Masses from Area

            var totalArea = 0f;
            for (var y = 0; y < resY; y++)
            for (var x = 0; x < resX; x++)
            {
                var idx = y * resX + x;
                var pos = pts[idx].Position;

                var normal = Vector3.One;
                var area = 0f;
                if (x < resX - 1 && y < resY - 1)
                    area += (normal = Vector3.Cross(pts[idx + 1].Position - pos, pts[idx + resX].Position - pos)).Length() / 2;
                if (x < resX - 1 && y > 0)
                    area += (normal = Vector3.Cross(pts[idx + 1].Position - pos, pts[idx - resX].Position - pos)).Length() / 2;
                if (x > 0 && y < resY - 1)
                    area += (normal = Vector3.Cross(pts[idx - 1].Position - pos, pts[idx + resX].Position - pos)).Length() / 2;
                if (x > 0 && y > 0)
                    area += (normal = Vector3.Cross(pts[idx - 1].Position - pos, pts[idx - resX].Position - pos)).Length() / 2;

                normal.Normalize();
                pts[idx].Normal = normal;
                pts[idx].Area = area;
                pts[idx].InverseMass = def.Mass * area;
                totalArea += area;
            }

            for (var i = 0; i < pts.Length; i++)
                pts[i].InverseMass = 1 / (pts[i].InverseMass / totalArea);

            #endregion

            var springs = new List<ClothSpring>(resX * (resY - 1) + (resX - 1) * resY + (resX - 1) * (resY - 1) + (resX - 2) * (resY - 1) +
                                                (resX - 1) * (resY - 2));

            #region Create Springs

            // + X
            for (var y = 0; y < resY; y++)
            for (var x = 0; x < resX - 1; x++)
            {
                var idx = y * resX + x;
                springs.Add(new ClothSpring
                {
                    P0 = idx,
                    P1 = idx + 1,
                    Stiffness = def.TensionStrength
                });
            }

            // + Y
            for (var y = 0; y < resY - 1; y++)
            for (var x = 0; x < resX; x++)
            {
                var idx = y * resX + x;
                springs.Add(new ClothSpring
                {
                    P0 = idx,
                    P1 = idx + resX,
                    Stiffness = def.TensionStrength
                });
            }

            // + X, +Y
            if (def.ShearStrength > 0)
                for (var y = 0; y < resY - 1; y++)
                for (var x = 0; x < resX - 1; x++)
                {
                    var idx = y * resX + x;
                    springs.Add(new ClothSpring
                    {
                        P0 = idx,
                        P1 = idx + resX + 1,
                        Stiffness = def.ShearStrength
                    });
                }

            if (def.StructuralStrength > 0)
            {
                // + 2X, +Y
                for (var y = 0; y < resY - 1; y++)
                for (var x = 0; x < resX - 2; x++)
                {
                    var idx = y * resX + x;
                    springs.Add(new ClothSpring
                    {
                        P0 = idx,
                        P1 = idx + resX + 2,
                        Stiffness = def.StructuralStrength
                    });
                }

                // +X, +2Y
                for (var y = 0; y < resY - 2; y++)
                for (var x = 0; x < resX - 1; x++)
                {
                    var idx = y * resX + x;
                    springs.Add(new ClothSpring
                    {
                        P0 = idx,
                        P1 = idx + resX * 2 + 1,
                        Stiffness = def.StructuralStrength
                    });
                }
            }

            #endregion

            #region Fill Spring Lengths

            for (var i = 0; i < springs.Count; i++)
            {
                var s = springs[i];
                var len = Vector3.Distance(pts[s.P0].Position, pts[s.P1].Position);
                s.Length = len;
                s.InvLength = 1 / len;
                springs[i] = s;
            }

            #endregion

            var quadStream = new int[4 * (resX - 1) * (resY - 1)];

            #region Create Quad Stream

            var quadOffset = 0;
            for (var y = 0; y < resY - 1; y++)
            for (var x = 0; x < resX - 1; x++)
            {
                var idx = y * resX + x;
                quadStream[quadOffset++] = idx;
                quadStream[quadOffset++] = idx + 1;
                quadStream[quadOffset++] = idx + 1 + resX;
                quadStream[quadOffset++] = idx + resX;
            }

            #endregion

            var cloth = new Cloth(pts, springs.ToArray(), quadStream) {Damping = def.Damping};
            return cloth;
        }
    }
}