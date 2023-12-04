using System;
using System.IO;
using VRageMath;

namespace Equinox76561198048419394.Core.Cli.Util.Writers
{
    public class WavefrontObjWriter : IDisposable
    {
        private int _vertexCount;
        private readonly StreamWriter _writer;

        public WavefrontObjWriter(string path) => _writer = new StreamWriter(File.Open(path, FileMode.Create, FileAccess.Write));

        public void Comment(string comment)
        {
            if (string.IsNullOrEmpty(comment))
                return;
            _writer.Write("# ");
            _writer.WriteLine(comment);
        }

        public int WriteVertex(Vector3 pt, string comment = "")
        {
            Comment(comment);
            _writer.WriteLine($"v {pt.X} {pt.Y} {pt.Z}");
            return ++_vertexCount;
        }

        public void WriteLine(int a, int b, string comment = "")
        {
            Comment(comment);
            _writer.WriteLine($"l {a} {b}");
        }

        public void WriteTriangle(int a, int b, int c, string comment = "")
        {
            Comment(comment);
            _writer.WriteLine($"f {a} {b} {c}");
        }

        public void WriteCapsule(Capsule capsule, string comment = "")
        {
            Comment(comment);
            const int spread = 8;

            var up = Vector3.Normalize(capsule.P1 - capsule.P0);
            var left = Vector3.CalculatePerpendicularVector(up);
            var forward = Vector3.Cross(up, left);

            var t0 = WriteVertex(capsule.P0 - up * capsule.Radius);
            var t1 = WriteVertex(capsule.P1 + up * capsule.Radius);
            var rings = new int[spread, 2];
            for (var i = 0; i < spread; i++)
            {
                var th = i * MathHelper.TwoPi / spread;
                var x = (float)Math.Cos(th);
                var y = (float)Math.Sin(th);
                var offset = (x * left + y * forward) * capsule.Radius;
                rings[i, 0] = WriteVertex(capsule.P0 + offset);
                rings[i, 1] = WriteVertex(capsule.P1 + offset);
            }

            for (var i = 0; i < spread; i++)
            {
                var j = (i + 1) % spread;
                WriteLine(rings[i, 0], rings[j, 0]);
                WriteLine(rings[i, 1], rings[j, 1]);
                WriteLine(rings[i, 0], rings[i, 1]);
                WriteLine(rings[i, 0], t0);
                WriteLine(rings[i, 1], t1);
            }
        }

        public void Dispose()
        {
            _writer?.Dispose();
        }
    }
}