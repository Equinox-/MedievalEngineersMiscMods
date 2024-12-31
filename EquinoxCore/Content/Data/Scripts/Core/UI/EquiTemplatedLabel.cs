using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Medieval.GUI.ContextMenu;
using Medieval.GUI.ContextMenu.DataSources;
using Sandbox.Graphics.GUI;
using Sandbox.Gui.Layouts;
using VRage;
using VRage.Collections;
using VRage.Library.Collections;
using VRageMath;
using MyGuiControlLabel = Sandbox.Graphics.GUI.MyGuiControlLabel;

namespace Equinox76561198048419394.Core.UI
{
    internal sealed class TemplatedLabel
    {
        private delegate bool DelParameterBinding(ref object val);

        private readonly TemplatedLabelFactory _factory;
        private readonly object[] _parameters;
        private readonly DelParameterBinding[] _bindings;

        internal TemplatedLabel(MyContextMenuController ctl, TemplatedLabelFactory factory)
        {
            _factory = factory;
            var count = _factory.Bindings.Count;
            _parameters = count == 0 ? Array.Empty<object>() : new object[count];
            _bindings = count == 0 ? Array.Empty<DelParameterBinding>() : new DelParameterBinding[count];
            for (var i = 0; i < count; i++)
                _bindings[i] = Bind(ctl, factory.Bindings[i]);
        }

        private static DelParameterBinding Bind(MyContextMenuController ctl, DataSourceReference dsr)
        {
            var accessor = new DataSourceAccessor<IMyContextMenuDataSource>(ctl, dsr.Id);
            var binding = InnerBind<string>();
            binding = binding ?? InnerBind<int>();
            binding = binding ?? InnerBind<uint>();
            binding = binding ?? InnerBind<long>();
            binding = binding ?? InnerBind<ulong>();
            binding = binding ?? InnerBind<float>();
            binding = binding ?? InnerBind<double>();
            return binding;

            DelParameterBinding InnerBind<T>() where T : IEquatable<T>
            {
                switch (accessor.DataSource)
                {
                    case IMySingleValueDataSource<T> svd:
                        return (ref object val) =>
                        {
                            var curr = svd.GetData();
                            if (val is T oldT && oldT.Equals(curr)) return false;
                            val = curr;
                            return true;
                        };
                    case IMyArrayDataSource<T> avd:
                        var index = dsr.Index;
                        return (ref object val) =>
                        {
                            var curr = avd.GetData(index);
                            if (val is T oldT && oldT.Equals(curr)) return false;
                            val = curr;
                            return true;
                        };
                    default:
                        return null;
                }
            }
        }

        private MyGuiControlLabel _label;
        private List<MyGuiControlLabel> _labels;

        public void BindTo(MyGuiControlLabel label)
        {
            label.Text = _factory.Template;
            if (_parameters.Length == 0) return;

            if (_label != null)
                (_labels ?? (_labels = new List<MyGuiControlLabel>())).Add(label);
            else
                _label = label;
        }

        public void UpdateBound()
        {
            if (_label == null && !(_labels?.Count > 0)) return;

            var changed = false;
            for (var i = 0; i<_bindings.Length; i++)
                changed |= _bindings[i](ref _parameters[i]);

            if (!changed) return;
            _label?.UpdateFormatParams(_parameters);

            if (_labels != null)
                foreach (var label in _labels)
                    label.UpdateFormatParams(_parameters);
        }
    }

    internal sealed class TemplatedLabelFactory
    {
        private static readonly Regex Binding = new Regex("{([^:}]+?)(?:|@([0-9]+))(|:[^}]*)}");
        public readonly string Template;
        public readonly ListReader<DataSourceReference> Bindings;

        public TemplatedLabelFactory(string template, DataSourceReference[] positionalBindings = null)
        {
            if (string.IsNullOrEmpty(template) || !template.Contains("{"))
            {
                Template = template;
                Bindings = ListReader<DataSourceReference>.Empty;
                return;
            }

            var bindings = new List<DataSourceReference>();
            if (positionalBindings != null) bindings.AddRange(positionalBindings);
            using (PoolManager.Get(out Dictionary<MyTuple<string, int>, string> lut))
            {
                Template = Binding.Replace(template, m =>
                {
                    var dataSource = m.Groups[1].Value;
                    var index = m.Groups[2].Value;
                    var format = m.Groups[3].Value;
                    return $"{{{BindDataSource(dataSource, index)}{format}}}";
                });

                string BindDataSource(string dataSource, string index)
                {
                    if (IsValidPositionalBinding(dataSource, index))
                        return dataSource;
                    if (!int.TryParse(index, out var indexInt)) indexInt = 0;
                    var key = MyTuple.Create(dataSource, indexInt);
                    if (lut.TryGetValue(key, out var ds)) return ds;
                    var dsr = new DataSourceReference { IdForXml = dataSource, Index = indexInt };
                    var parameter = bindings.Count.ToString();
                    bindings.Add(dsr);
                    lut.Add(key, parameter);
                    return parameter;
                }

                bool IsValidPositionalBinding(string dataSource, string index)
                {
                    if (positionalBindings == null || positionalBindings.Length == 0) return false;
                    if (!string.IsNullOrEmpty(index)) return false;
                    if (!int.TryParse(dataSource, out var position)) return false;
                    return position >= 0 && position < positionalBindings.Length;
                }
            }

            Bindings = bindings;
        }

        public TemplatedLabel Create(MyContextMenuController ctl) => new TemplatedLabel(ctl, this);
    }
}