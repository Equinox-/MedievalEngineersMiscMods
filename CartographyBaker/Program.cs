using System;
using System.ComponentModel;
using Sandbox.Game.Replication.Components;
using Sandbox.ModAPI;

namespace CartographyBaker
{
    internal class Program
    {
        public static void Main(string[] args)
        {
            // ItemGeneratorSymbolBaker.Generate(@"C:\Users\westin\Downloads\world");
            // BendyBaker.Generate(@"C:\Users\westin\Downloads\2023-04-08-131956");
            var attr = new DefaultValueAttribute(typeof(MyPhysicsComponentReplicable), "");
            Console.WriteLine(attr.Value.GetType());
        }
    }
}