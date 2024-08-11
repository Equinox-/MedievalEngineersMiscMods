using System;
using System.Collections.Generic;
using Equinox76561198048419394.Core.Debug;
using Sandbox.Game.Gui;
using VRage.Game;
using VRageMath;

namespace Equinox76561198048419394.Core.Inventory
{
    public class EquiInvertedVisualInventoryDebug : MyGuiScreenDebugBase
    {
        public override string GetFriendlyName() => "EquiInvertedVisualInventoryDebug";

        public EquiInvertedVisualInventoryDebug()
        {
            RecreateControls(true);
        }

        private readonly List<EquiInvertedVisualInventoryComponentDefinition> _defs = new List<EquiInvertedVisualInventoryComponentDefinition>();

        private readonly List<AttachmentInfo> _attachments = new List<AttachmentInfo>();

        private EquiInvertedVisualInventoryComponentDefinition _selectedDef;
        private AttachmentInfo _selectedAttachment;

        public override void RecreateControls(bool constructor)
        {
            base.RecreateControls(constructor);
            _defs.Clear();
            _defs.AddRange(MyDefinitionManager.GetOfType<EquiInvertedVisualInventoryComponentDefinition>());
            _defs.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Id.SubtypeName ?? "", b.Id.SubtypeName ?? ""));

            m_currentPosition = -m_size.Value / 2.0f + new Vector2(0.02f, 0.10f);
            m_currentPosition.Y += 0.01f;
            m_scale = 0.7f;

            AddCaption("Facade Editor", Color.Yellow.ToVector4());
            AddShareFocusHint();

            AddLabel("Select", Color.Yellow, 1);
            var facades = AddCombo();
            for (var i = 0; i < _defs.Count; i++)
                facades.AddItem(i, _defs[i].Id.SubtypeName);
            var attachments = AddCombo();
            facades.ItemSelected += (cb, key) =>
            {
                _selectedDef = key >= 0 && key < _defs.Count ? _defs[(int)key] : null;
                _attachments.Clear();
                if (_selectedDef != null)
                    foreach (var group in _selectedDef.AttachmentPoints.Values)
                    foreach (var attachment in group.AttachmentPoints)
                        _attachments.Add(new AttachmentInfo(attachment));

                attachments.ClearItems();
                for (var i = 0; i < _attachments.Count; i++)
                    attachments.AddItem(i, _attachments[i].Backing.Name.String);
                if (_attachments.Count > 0)
                    attachments.SelectItemByIndex(0);
            };

            m_currentPosition.Y += 0.01f;
            AddLabel("Settings", Color.Yellow, 1);

            var sync = new List<Action>();
            CreateNullableSlider("Yaw", -180, 180,
                (ref AttachmentInfo ob) => ref ob.Rotation,
                (ref Vector3 rot) => ref rot.X);
            CreateNullableSlider("Pitch", -180, 180,
                (ref AttachmentInfo ob) => ref ob.Rotation,
                (ref Vector3 rot) => ref rot.Y);
            CreateNullableSlider("Roll", -180, 180,
                (ref AttachmentInfo ob) => ref ob.Rotation,
                (ref Vector3 rot) => ref rot.Z);

            CreateNullableSlider("Offset X", -1.5f, 1.5f,
                (ref AttachmentInfo ob) => ref ob.Offset,
                (ref Vector3 rot) => ref rot.X);
            CreateNullableSlider("Offset Y", -1.5f, 1.5f,
                (ref AttachmentInfo ob) => ref ob.Offset,
                (ref Vector3 rot) => ref rot.Y);
            CreateNullableSlider("Offset Z", -1.5f, 1.5f,
                (ref AttachmentInfo ob) => ref ob.Offset,
                (ref Vector3 rot) => ref rot.Z);

            CreateBasicSlider("Scale", 0, 2, (ref AttachmentInfo ob) => ref ob.Scale);

            attachments.ItemSelected += (cb, key) =>
            {
                _selectedAttachment = key >= 0 && key < _attachments.Count ? _attachments[(int)key] : null;
                foreach (var act in sync)
                    act();
            };
            return;

            void CreateNullableSlider<T, TValue>(string name, float min, float max,
                DelGetRef<AttachmentInfo, T?> getRef,
                DelGetRef<T, TValue> getComponent) where T : struct
            {
                var suppress = new bool[1];
                var slider = AddSlider(name, min, max, Get, value =>
                {
                    if (_selectedAttachment == null) return;
                    ref var val = ref getRef(ref _selectedAttachment);
                    if (!val.HasValue) return;
                    var unwrapped = val.Value;
                    ref var comp = ref getComponent(ref unwrapped);
                    if (Math.Abs(Convert.ToSingle(comp) - value) < 1e-3f) return;
                    comp = (TValue)Convert.ChangeType(value, typeof(TValue));
                    val = unwrapped;
                    if (!suppress[0])
                        InvokeChanged();
                });
                sync.Add(() =>
                {
                    var prev = suppress[0];
                    suppress[0] = true;
                    slider.Value = Get();
                    suppress[0] = prev;
                });
                return;

                float Get()
                {
                    if (_selectedAttachment == null) return 0;
                    ref var val = ref getRef(ref _selectedAttachment);
                    if (!val.HasValue) return 0;
                    var unwrapped = val.Value;
                    return Convert.ToSingle(getComponent(ref unwrapped));
                }
            }

            void CreateBasicSlider<TValue>(string name, float min, float max, DelGetRef<AttachmentInfo, TValue?> getRef) where TValue : struct
            {
                var suppress = new bool[1];
                var slider = AddSlider(name, min, max, Get, value =>
                {
                    if (_selectedAttachment == null) return;
                    ref var val = ref getRef(ref _selectedAttachment);
                    if (!val.HasValue) return;
                    var unwrapped = val.Value;
                    if (Math.Abs(Convert.ToSingle(unwrapped) - value) < 1e-3f) return;
                    unwrapped = (TValue)Convert.ChangeType(value, typeof(TValue));
                    val = unwrapped;
                    if (!suppress[0])
                        InvokeChanged();
                });
                sync.Add(() =>
                {
                    var prev = suppress[0];
                    suppress[0] = true;
                    slider.Value = Get();
                    suppress[0] = prev;
                });
                return;

                float Get()
                {
                    if (_selectedAttachment == null) return 0;
                    ref var val = ref getRef(ref _selectedAttachment);
                    if (!val.HasValue) return 0;
                    return Convert.ToSingle(val.Value);
                }
            }
        }

        private void InvokeChanged()
        {
            if (_selectedDef == null || _selectedAttachment == null) return;
            _selectedAttachment.Sync();
            Changed?.Invoke(_selectedDef, _selectedAttachment.Backing);
        }

        public static event Action<EquiInvertedVisualInventoryComponentDefinition, EquiInvertedVisualInventoryComponentDefinition.Attachment> Changed;

        private delegate ref TOut DelGetRef<TIn, TOut>(ref TIn input);

        private sealed class AttachmentInfo
        {
            public readonly EquiInvertedVisualInventoryComponentDefinition.Attachment Backing;

            public Vector3? Offset;
            public Vector3? Rotation;
            public float? Scale;

            public AttachmentInfo(EquiInvertedVisualInventoryComponentDefinition.Attachment backing)
            {
                Backing = backing;
                FacadeEditorDebug.DeconstructMatrix(backing.Transform, out var offset, out var rotation, out var scale);
                Offset = offset;
                Rotation = rotation;
                Scale = scale;
            }

            public void Sync()
            {
                Backing.Transform = FacadeEditorDebug.ConstructMatrix(Offset ?? default, Rotation ?? default, Scale ?? 1);
            }
        }
    }
}