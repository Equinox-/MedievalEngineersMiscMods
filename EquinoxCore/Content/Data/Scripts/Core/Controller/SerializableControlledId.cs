using System;
using System.Xml.Serialization;

namespace Equinox76561198048419394.Core.Controller
{
    public struct ControlledId : IEquatable<ControlledId>, IEquatable<EquiPlayerAttachmentComponent.Slot>
    {
        [XmlAttribute]
        public long Entity;

        [XmlAttribute]
        public string Slot;

        public ControlledId(long entity, string slot)
        {
            Entity = entity;
            Slot = slot;
        }

        public ControlledId(EquiPlayerAttachmentComponent.Slot slot)
        {
            Entity = slot.Controllable.Entity.EntityId;
            Slot = slot.Definition.Name;
        }

        public bool Equals(ControlledId other)
        {
            return Entity == other.Entity && string.Equals(Slot, other.Slot);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            return obj is ControlledId && Equals((ControlledId) obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (Entity.GetHashCode() * 397) ^ (Slot != null ? Slot.GetHashCode() : 0);
            }
        }

        public bool Equals(EquiPlayerAttachmentComponent.Slot other)
        {
            if (other?.Controllable?.Entity == null)
                return false;
            return other.Controllable.Entity.EntityId == Entity && other.Definition.Name.Equals(Slot);
        }

        public override string ToString()
        {
            return $"[{nameof(Entity)}: {Entity}, {nameof(Slot)}: {Slot}]";
        }
    }
}