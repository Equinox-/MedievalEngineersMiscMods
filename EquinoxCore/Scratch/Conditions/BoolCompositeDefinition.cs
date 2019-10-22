using System.Linq;
using VRage.Game.Entity;

namespace Equinox76561198048419394.Core.Conditions
{
    public class AllCondition : CompositeCondition
    {
        public AllCondition(MyEntity container, ICondition[] children, bool invert) : base(container, children, invert)
        {
        }

        protected override bool Calculate()
        {
            foreach (var c in Children)
                if (!c.State)
                    return true;
            return false;
        }
    }

    public class AllConditionDefinition : CompositeConditionDefinition
    {
        public override ICondition Compile(MyEntity container, bool inverted)
        {
            inverted ^= Invert;
            if (Children == null || Children.Length == 0)
                return ConstantCondition.For(!inverted);
            if (Children.Length == 1)
                return Children[0].Compile(container, inverted);

            var compiled = new ICondition[Children.Length];
            for (var i = 0; i < compiled.Length; i++)
                compiled[i] = Children[i].Compile(container, false);
            if (compiled.All(x => x == ConstantCondition.True))
                return ConstantCondition.For(!inverted);
            if (compiled.Any(x => x == ConstantCondition.False))
                return ConstantCondition.For(inverted);
            return new AllCondition(container, compiled, inverted);
        }
    }

    public class AnyCondition : CompositeCondition
    {
        public AnyCondition(MyEntity container, ICondition[] children, bool invert) : base(container, children, invert)
        {
        }

        protected override bool Calculate()
        {
            foreach (var c in Children)
                if (c.State)
                    return true;
            return false;
        }
    }

    public class AnyConditionDefinition : CompositeConditionDefinition
    {
        public override ICondition Compile(MyEntity container, bool inverted)
        {
            inverted ^= Invert;
            if (Children == null || Children.Length == 0)
                return ConstantCondition.For(inverted);
            if (Children.Length == 1)
                return Children[0].Compile(container, inverted);
            var compiled = new ICondition[Children.Length];
            for (var i = 0; i < compiled.Length; i++)
                compiled[i] = Children[i].Compile(container, false);
            if (compiled.Any(x => x == ConstantCondition.True))
                return ConstantCondition.For(!inverted);
            if (compiled.All(x => x == ConstantCondition.False))
                return ConstantCondition.For(inverted);
            return new AnyCondition(container, compiled, inverted);
        }
    }
}