using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using Havok;
using Sandbox.Game.Localization;
using VRage.FileSystem;
using VRage.Logging;
using VRage.Meta;
using VRage.ParallelWorkers;
using VRage.Systems;
using VRageRender;

namespace Equinox76561198048419394.Core.Cli
{
    public static class ProgramBootstrapped
    {
        public static int SetupEngineAndRun(SharedOptions options)
        {
            MyLog.Default = new MyLog();
            MyFileSystem.Init(Path.Combine(options.GameDirectory, "Content"), "./");
            MyLanguage.Init();
            MyRenderProxy.Initialize(new MyNullRender());
            MyLog.Default.Init("converter.log", new StringBuilder());
            Workers.Init(new WorkerConfigurationFactory()
                .AddGroup(new WorkerConfigurationFactory.Group
                {
                    Id = WorkerGroup.Background,
                    Min = 1,
                    Priority = ThreadPriority.BelowNormal,
                    Ratio = .1f
                })
                .AddGroup(new WorkerConfigurationFactory.Group
                {
                    Id = WorkerGroup.Logic,
                    Min = 1,
                    Priority = ThreadPriority.Normal,
                    Ratio = .7f
                })
                .AddGroup(new WorkerConfigurationFactory.Group
                {
                    Id = WorkerGroup.Render,
                    Min = 1,
                    Priority = ThreadPriority.AboveNormal,
                    Ratio = .2f
                })
                .SetDefault(WorkerGroup.Logic)
                .Bake(32));

            MyMetadataSystem.LoadAssemblies(new[]
            {
                "VRage",
                "VRage.Game",
                "Sandbox.Graphics",
                "Sandbox.Game",
                "MedievalEngineers.ObjectBuilders",
                "MedievalEngineers.Game"
            }.Select(Assembly.Load));

            HkBaseSystem.Init(new NamedLogger(MyLog.Default));

            var result = options.Run();

            MyLog.Default.Dispose();
            return result;
        }
    }
}