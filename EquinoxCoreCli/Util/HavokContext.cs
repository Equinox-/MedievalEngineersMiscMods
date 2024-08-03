using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Havok;
using VRage.Logging;
using VRageRender.Import;

namespace Equinox76561198048419394.Core.Cli.Util
{
    public readonly struct HavokContext : IDisposable
    {
        public readonly HkWorld World;
        public readonly HkDestructionStorage DestructionStorage;

        private HavokContext(HkWorld world, HkDestructionStorage destructionStorage)
        {
            World = world;
            DestructionStorage = destructionStorage;
        }

        public bool TryLoadModelData(IReadOnlyDictionary<string, object> model, out List<HkShape> shapes, out List<HkdBreakableShape> destruction)
        {
            shapes = new List<HkShape>();
            destruction = new List<HkdBreakableShape>();
            if (!model.TryGetValue(MyImporterConstants.TAG_HAVOK_COLLISION_GEOMETRY, out var collision))
                return false;
            HkShapeLoader.LoadShapesListFromBuffer((byte[]) collision, shapes, out _, out var primaryDestruction);
            var destructionData = primaryDestruction ? (byte[])collision :
                model.TryGetValue(MyImporterConstants.TAG_HAVOK_DESTRUCTION, out var destructionAux) ? (byte[])destructionAux : null;
            if (destructionData != null)
                destruction.AddCollection(DestructionStorage.LoadDestructionDataFromBuffer(destructionData));
            return true;
        }

        public List<HkShape> LoadShapes(byte[] buffer)
        {
            var shapes = new List<HkShape>();
            HkShapeLoader.LoadShapesListFromBuffer(buffer, shapes, out _, out _);
            return shapes;
        }

        public List<HkdBreakableShape> LoadBreakableShapes(byte[] buffer) =>
            new List<HkdBreakableShape>(DestructionStorage.LoadDestructionDataFromBuffer(buffer));

        public byte[] SaveShapes(List<HkShape> shapes)
        {
            if (shapes.Count == 1 && shapes[0].ShapeType == HkShapeType.List)
                return SaveInternal((HkListShape)shapes[0]);
            var temp = new HkListShape(shapes.ToArray(), HkReferencePolicy.None);
            try
            {
                return SaveInternal(temp);
            }
            finally
            {
                ((HkShape)temp).Delete();
            }

            byte[] SaveInternal(HkListShape list)
            {
                var tempFile = Path.GetTempFileName();
                try
                {
                    HkShapeLoader.SaveShapesListToFile(tempFile, list, false);
                    return File.ReadAllBytes(tempFile);
                }
                finally
                {
                    File.Delete(tempFile);
                }
            }
        }

        public byte[] SaveBreakableShapes(HkdBreakableShape shape)
        {
            var tempFile = Path.GetTempFileName();
            try
            {
                DestructionStorage.SaveDestructionData(shape, tempFile);
                return File.ReadAllBytes(tempFile);
            }
            finally
            {
                File.Delete(tempFile);
            }
        }

        [ThreadStatic]
        private static bool _isThreadSetup;

        public static void InitHavok(NamedLogger log)
        {
            HkBaseSystem.Init(log);
            _isThreadSetup = true;
        }

        public static HavokContext Create()
        {
            if (!_isThreadSetup)
            {
                HkBaseSystem.InitThread(Thread.CurrentThread.Name);
                _isThreadSetup = true;
            }
            var hkWorld = new HkWorld(true, 50000, float.MaxValue, false, 4, 0.6f);
            HkDestructionStorage destruction = null;
            try
            {
                hkWorld.MarkForWrite();
                hkWorld.DestructionWorld = new HkdWorld(hkWorld);
                hkWorld.UnmarkForWrite();
                destruction = new HkDestructionStorage(hkWorld.DestructionWorld);
                var ctx = new HavokContext(hkWorld, destruction);
                // Steal the world and destruction storage so they don't get closed
                hkWorld = null;
                destruction = null;
                return ctx;
            }
            finally
            {
                destruction?.Dispose();
                hkWorld?.Dispose();
            }
        }

        public void Dispose()
        {
            World?.Dispose();
            DestructionStorage?.Dispose();
        }
    }
}