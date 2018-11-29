using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Xml.Serialization;
using Sandbox.Definitions.Components.Entity.Stats.Effects;
using VRage;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Definitions;
using VRage.Game.Entity.UseObject;
using VRage.Library.Logging;
using VRage.ObjectBuilders;
using VRage.ObjectBuilders.Components.Entity.Stats.Definitions;
using VRage.ObjectBuilders.Definitions;
using VRage.Utils;

namespace Equinox76561198048419394.Core.Controller
{
    [MyDefinitionType(typeof(MyObjectBuilder_EquiPlayerAttachmentComponentDefinition))]
    public class EquiPlayerAttachmentComponentDefinition : MyEntityComponentDefinition
    {
        public const string LegacyAttachmentName = "legacy";

        public class ImmutableEffectOperations
        {
            public readonly MyObjectBuilder_EquiPlayerAttachmentComponentDefinition.EffectOperationsInfo.TriggerTime When;
            public readonly long DelayMs;
            public readonly long IntervalMs;

            private readonly List<MyEffectOperation> _operations;

            public ListReader<MyEffectOperation> Operations => _operations;

            public bool IsValid => _operations.Count > 0 && ((IntervalMs > 0) ==
                                                             (When == MyObjectBuilder_EquiPlayerAttachmentComponentDefinition.EffectOperationsInfo.TriggerTime
                                                                  .Continuous));

            internal bool AssertValid(MyObjectBuilder_EquiPlayerAttachmentComponentDefinition parent)
            {
                var good = true;
                if (_operations.Count == 0)
                {
                    MyDefinitionErrors.Add(parent.ModContext, $"{parent.Id} has operation group with no operations", TErrorSeverity.Error);
                    good = false;
                }

                if (When == MyObjectBuilder_EquiPlayerAttachmentComponentDefinition.EffectOperationsInfo.TriggerTime.Continuous)
                {
                    if (IntervalMs <= 0)
                    {
                        MyDefinitionErrors.Add(parent.ModContext, $"{parent.Id} has operation group with continuous trigger and zero interval",
                            TErrorSeverity.Error);
                        good = false;
                    }
                }
                else
                {
                    if (IntervalMs != 0)
                    {
                        MyDefinitionErrors.Add(parent.ModContext, $"{parent.Id} has operation group with non continuous trigger and non zero interval",
                            TErrorSeverity.Error);
                        good = false;
                    }
                }

                // ReSharper disable once InvertIf
                if (When == MyObjectBuilder_EquiPlayerAttachmentComponentDefinition.EffectOperationsInfo.TriggerTime.Leave && DelayMs != 0)
                {
                    MyDefinitionErrors.Add(parent.ModContext, $"{parent.Id} has operation group with leave trigger and non zero delay", TErrorSeverity.Error);
                    good = false;
                }

                return good;
            }

            public ImmutableEffectOperations(MyObjectBuilder_EquiPlayerAttachmentComponentDefinition.EffectOperationsInfo ob)
            {
                var tmp = new List<MyEffectOperation>();
                When = ob.When;
                DelayMs = (long) (ob.DelaySeconds * 1000);
                IntervalMs = (long) (ob.IntervalSeconds * 1000);
                if (ob.Operations == null) return;
                foreach (var input in ob.Operations)
                {
                    var op = new MyEffectOperation();
                    op.Init(input);
                    tmp.Add(op);
                }

                _operations = tmp;
            }
        }

        public class ImmutableAttachmentInfo
        {
            public string Name { get; }
            public MyPositionAndOrientation Anchor { get; }
            private readonly AnimationDesc[] _animations;

            public MyActionDescription EmptyActionDesc { get; }
            public MyActionDescription OccupiedActionDesc { get; }

            private readonly HashSet<string> _dummyNames = new HashSet<string>();

            public IReadOnlyCollection<string> Dummies => _dummyNames;

            public readonly IReadOnlyList<ImmutableEffectOperations> EffectOperations;

            public ImmutableAttachmentInfo(MyObjectBuilder_EquiPlayerAttachmentComponentDefinition @base,
                MyObjectBuilder_EquiPlayerAttachmentComponentDefinition.AttachmentInfo ob)
            {
                Name = ob.Name;
                Anchor = ob.Anchor;
                _animations = ob.Animations != null && ob.Animations.Length > 0 ? ob.Animations.Select(x => new AnimationDesc(x)).ToArray() : null;
                EmptyActionDesc = (MyActionDescription) ob.EmptyAction;
                OccupiedActionDesc = (MyActionDescription) ob.OccupiedAction;
                _dummyNames.Clear();
                if (ob.DummyNames == null) return;
                foreach (var d in ob.DummyNames)
                    if (!string.IsNullOrWhiteSpace(d))
                        _dummyNames.Add(d);

                var tmpOps = new List<ImmutableEffectOperations>();
                if (ob.EffectOperations != null)
                    foreach (var eo in ob.EffectOperations)
                    {
                        var res = new ImmutableEffectOperations(eo);
                        if (res.IsValid)
                            tmpOps.Add(res);
                        else
                            MyDefinitionErrors.Add(@base.ModContext, $"{@base.Id} has an invalid effect operation", TErrorSeverity.Error);
                    }

                EffectOperations = tmpOps;
            }

            public ImmutableAttachmentInfo(MyObjectBuilder_EquiPlayerAttachmentComponentDefinition ob)
            {
                Name = LegacyAttachmentName;
                // ReSharper disable once PossibleInvalidOperationException
                Anchor = ob.Anchor.Value;
                _animations = ob.Animations != null && ob.Animations.Length > 0 ? ob.Animations.Select(x => new AnimationDesc(x)).ToArray() : null;

                EmptyActionDesc = (MyActionDescription) ob.EmptyAction;
                OccupiedActionDesc = (MyActionDescription) ob.OccupiedAction;
                _dummyNames.Clear();
                if (ob.DummyNames == null) return;
                foreach (var d in ob.DummyNames)
                    if (!string.IsNullOrWhiteSpace(d))
                        _dummyNames.Add(d);

                EffectOperations = new List<ImmutableEffectOperations>();
            }


            public AnimationDesc? ByIndex(int index)
            {
                if (_animations == null || index < 0 || index >= _animations.Length)
                    return null;
                return _animations[index];
            }

            public AnimationDesc? SelectAnimation(MyDefinitionId controller, float rand, out int index)
            {
                index = -1;
                if (_animations == null || _animations.Length == 0)
                    return null;
                var totalWeight = 0f;
                foreach (var k in _animations)
                    if (k.Accept(controller))
                        totalWeight += k.Weight;
                var rval = totalWeight * rand;
                for (var i = 0; i < _animations.Length; i++)
                {
                    var k = _animations[i];
                    if (!k.Accept(controller))
                        continue;
                    totalWeight -= k.Weight;
                    if (rval < totalWeight) continue;
                    index = i;
                    return k;
                }

                MyLog.Default.Warning(
                    $"Failed to find animation for {controller}.  R={rand}, Opts={string.Join(", ", _animations.Select(x => x.ToString()))}");
                return null;
            }
        }

        private readonly Dictionary<string, ImmutableAttachmentInfo> _attachmentPointsByName = new Dictionary<string, ImmutableAttachmentInfo>();
        private readonly Dictionary<string, ImmutableAttachmentInfo> _attachmentPointByDummy = new Dictionary<string, ImmutableAttachmentInfo>();
        private ImmutableAttachmentInfo _wildcardAttachment;

        protected override void Init(MyObjectBuilder_DefinitionBase def)
        {
            base.Init(def);
            var ob = (MyObjectBuilder_EquiPlayerAttachmentComponentDefinition) def;
            if (ob.Anchor.HasValue)
                Register(new ImmutableAttachmentInfo(ob));
            if (ob.Attachments != null)
                foreach (var k in ob.Attachments)
                    Register(new ImmutableAttachmentInfo(ob, k));
            ImmutableAttachmentInfo wildcard = null;
            foreach (var v in _attachmentPointsByName.Values)
                if (v.Dummies.Count == 0)
                {
                    if (wildcard != null)
                        MyDefinitionErrors.Add(Context, $"Attachments {wildcard.Name} and {v.Name} both are wildcard attachments", TErrorSeverity.Critical);
                    wildcard = v;
                }

            _wildcardAttachment = wildcard;
        }

        private void Register(ImmutableAttachmentInfo attachment)
        {
            if (_attachmentPointsByName.ContainsKey(attachment.Name))
            {
                MyDefinitionErrors.Add(Context, $"Can't register {attachment.Name} twice", TErrorSeverity.Critical);
                return;
            }

            _attachmentPointsByName.Add(attachment.Name, attachment);
            foreach (var dum in attachment.Dummies)
            {
                if (_attachmentPointByDummy.ContainsKey(dum))
                {
                    MyDefinitionErrors.Add(Context, $"Can't register attachment for dummy {dum} twice", TErrorSeverity.Critical);
                    continue;
                }

                _attachmentPointByDummy.Add(dum, attachment);
            }
        }

        public IReadOnlyCollection<ImmutableAttachmentInfo> Attachments => _attachmentPointsByName.Values;

        public ImmutableAttachmentInfo AttachmentForDummy(string dummy)
        {
            ImmutableAttachmentInfo pt;
            // ReSharper disable once ConvertIfStatementToReturnStatement
            if (_attachmentPointByDummy.TryGetValue(dummy, out pt))
                return pt;
            return _wildcardAttachment;
        }

        public struct AnimationDesc
        {
            public readonly MyStringId Start;
            public readonly MyStringId Stop;
            public readonly float Weight;
            public readonly bool Whitelist;
            public readonly HashSet<MyDefinitionId> CharacterFilter;

            public AnimationDesc(MyObjectBuilder_EquiPlayerAttachmentComponentDefinition.AnimationDesc desc)
            {
                Start = MyStringId.GetOrCompute(desc.Start);
                Stop = MyStringId.GetOrCompute(desc.Stop);
                Weight = desc.Weight;
                Whitelist = desc.Whitelist;
                if (desc.CharacterFilter == null || desc.CharacterFilter.Length == 0)
                {
                    CharacterFilter = null;
                    return;
                }

                CharacterFilter = new HashSet<MyDefinitionId>();
                foreach (var k in desc.CharacterFilter)
                    CharacterFilter.Add(k);
            }

            public bool Accept(MyDefinitionId id)
            {
                var has = CharacterFilter != null && CharacterFilter.Contains(id);
                return Whitelist ? has : !has;
            }

            public override string ToString()
            {
                var cf = CharacterFilter != null ? string.Join("||", CharacterFilter) : "empty";
                return $"AD[{Start.String}=>{Stop.String}, W={Weight}, W={Whitelist}, Fil={cf}]";
            }
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiPlayerAttachmentComponentDefinition : MyObjectBuilder_EntityComponentDefinition
    {
        public struct AnimationDesc
        {
            [XmlAttribute]
            public string Start;

            [XmlAttribute]
            public string Stop;

            [XmlAttribute]
            [DefaultValue(1)]
            public float Weight;

            [XmlAttribute]
            public bool Whitelist;

            public SerializableDefinitionId[] CharacterFilter;
        }

        public struct ActionDesc
        {
            [XmlAttribute]
            public string Text;

            [XmlAttribute]
            public string Icon;

            public static explicit operator MyActionDescription(ActionDesc d)
            {
                return new MyActionDescription
                {
                    Text = string.IsNullOrWhiteSpace(d.Text)
                        ? MyStringId.GetOrCompute("Use")
                        : MyStringId.GetOrCompute(d.Text),
                    Icon = d.Icon
                };
            }
        }

        public class EffectOperationsInfo
        {
            public enum TriggerTime
            {
                Enter,
                Continuous,
                Leave
            }

            [XmlAttribute]
            public TriggerTime When;

            [XmlAttribute]
            public float DelaySeconds;

            [XmlAttribute]
            public float IntervalSeconds;

            [XmlElement("Operation")]
            public SerializableEffectOperation[] Operations;
        }

        public class AttachmentInfo
        {
            [XmlAttribute]
            public string Name;

            [XmlElement("DummyName")]
            public string[] DummyNames;

            public MyPositionAndOrientation Anchor;
            public AnimationDesc[] Animations;

            public ActionDesc EmptyAction, OccupiedAction;

            [XmlElement("Effects")]
            public EffectOperationsInfo[] EffectOperations;
        }

        [XmlElement("Attachment")]
        public AttachmentInfo[] Attachments;

        #region Legacy

        [XmlElement("DummyName")]
        public string[] DummyNames;

        public MyPositionAndOrientation? Anchor;
        public AnimationDesc[] Animations;

        public ActionDesc EmptyAction, OccupiedAction;

        #endregion
    }
}