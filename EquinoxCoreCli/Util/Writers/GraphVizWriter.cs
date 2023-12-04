using System;
using System.IO;
using VRageMath;

namespace Equinox76561198048419394.Core.Cli.Util.Writers
{
    public class GraphVizWriter : IDisposable
    {
        private int _vertexCount;
        private readonly StreamWriter _writer;

        public GraphVizWriter(string path)
        {
            _writer = new StreamWriter(new BufferedStream(File.Open(path, FileMode.Create, FileAccess.Write)));
            _writer.WriteLine("digraph G {");
        }

        public int WriteVertex(string label = "", Color? color = null)
        {
            var id = ++_vertexCount;
            _writer.Write("n");
            _writer.Write(id);
            using (var props = new PropsWriter(_writer))
                props.WriteCommon(label, color);
            _writer.WriteLine(";");
            return id;
        }

        public void WriteEdge(int a, int b, string label = "", Color? color = null)
        {
            _writer.Write($"n{a} -> n{b}");
            using (var props = new PropsWriter(_writer))
                props.WriteCommon(label, color);
            _writer.WriteLine(";");
        }

        public void Dispose()
        {
            _writer.WriteLine("}");
            _writer.Dispose();
        }

        private struct PropsWriter : IDisposable
        {
            private StreamWriter _writer;
            private bool _started;

            public PropsWriter(StreamWriter writer)
            {
                _writer = writer;
                _started = false;
            }

            private void WriteKey(string key)
            {
                if (_started)
                    _writer.Write(',');
                else
                {
                    _writer.Write('[');
                    _started = true;
                }
                _writer.Write(key);
                _writer.Write("=");
            }

            public void WriteCommon(string label = "", Color? color = null)
            {
                if (!string.IsNullOrEmpty(label))
                    Write("label", label);
                if (color.HasValue)
                    Write("color", color.Value);
            }

            public void Write(string key, string value)
            {
                WriteKey(key);
                _writer.Write('"');
                _writer.Write(value);
                _writer.Write('"');
            }

            public void Write(string key, Color color)
            {
                WriteKey(key);
                _writer.Write($"\"#{color.R:X2}{color.G:X2}{color.B:X2}\"");
            }

            public void Dispose()
            {
                if (_started)
                    _writer.Write("]");
            }
        }
    }
}