using System;
using Sandbox.ModAPI;
using VRage.Components.Entity.Camera;
using VRage.Entities.Gravity;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.Input;
using VRage.Game.ModAPI;
using VRage.Input.Input;
using VRage.Utils;
using VRageMath;
using VRageRender;

namespace Equinox76561198048419394.Core.Util
{
    public interface IToolWithBuilding
    {
        MyEntity Holder { get; }

        ToolBuildingState BuildingState { get; }

        Matrix BuildingRotationBias { get; }
    }

    public class ToolBuildingState
    {
        public float DefaultDistance { get; set; } = float.PositiveInfinity;

        public float Distance { get; set; }

        public Quaternion WorldRotation { get; set; } = Quaternion.Identity;

        public BuildingRotationSnapping SnapRotation { get; set; }
    }

    public enum BuildingRotationSnapping
    {
        None,
        RotationAxis,
        AllAxes,
    }

    public static class ToolWithBuilding
    {
        private static readonly MyInputContext InputContext = new MyInputContext("Building Tool");

        private static bool TryGetLocal(out IToolWithBuilding tool, out IMyPlayer player)
        {
            player = MyAPIGateway.Session?.LocalHumanPlayer;
            tool = MyAPIGateway.Session?.ControlledObject?.GetHeldBehavior() as IToolWithBuilding;
            return tool != null && player != null;
        }

        static ToolWithBuilding()
        {
            InputContext.RegisterAction(MyControlsGeneral.CUBE_ROTATE_VERTICAL_POSITIVE, MyInputStateFlags.Down,
                (ref MyInputContext.ActionEvent evt) => HandleRotation(evt.Flags, Vector3.Backward));
            InputContext.RegisterAction(MyControlsGeneral.CUBE_ROTATE_VERTICAL_NEGATIVE, MyInputStateFlags.Down,
                (ref MyInputContext.ActionEvent evt) => HandleRotation(evt.Flags, Vector3.Forward));
            InputContext.RegisterAction(MyControlsGeneral.CUBE_ROTATE_HORISONTAL_POSITIVE, MyInputStateFlags.Down,
                (ref MyInputContext.ActionEvent evt) => HandleRotation(evt.Flags, Vector3.Right));
            InputContext.RegisterAction(MyControlsGeneral.CUBE_ROTATE_HORISONTAL_NEGATIVE, MyInputStateFlags.Down,
                (ref MyInputContext.ActionEvent evt) => HandleRotation(evt.Flags, Vector3.Left));
            InputContext.RegisterAction(MyControlsGeneral.CUBE_ROTATE_ROLL_POSITIVE, MyInputStateFlags.Down,
                (ref MyInputContext.ActionEvent evt) => HandleRotation(evt.Flags, Vector3.Down));
            InputContext.RegisterAction(MyControlsGeneral.CUBE_ROTATE_ROLL_NEGATIVE, MyInputStateFlags.Down,
                (ref MyInputContext.ActionEvent evt) => HandleRotation(evt.Flags, Vector3.Up));
            IMyHudNotification snapRotationNotification = null;
            InputContext.RegisterAction(MyStringHash.GetOrCompute("SwitchBuildingMode"), (ref MyInputContext.ActionEvent evt) =>
            {
                if (!TryGetLocal(out var tool, out _)) return;
                var state = tool.BuildingState;
                snapRotationNotification?.Hide();
                switch (state.SnapRotation)
                {
                    case BuildingRotationSnapping.None:
                        state.SnapRotation = BuildingRotationSnapping.RotationAxis;
                        snapRotationNotification = Extensions.ApiUtilities.CreateNotification("Snap on axis of rotation");
                        break;
                    case BuildingRotationSnapping.RotationAxis:
                        state.SnapRotation = BuildingRotationSnapping.AllAxes;
                        snapRotationNotification = Extensions.ApiUtilities.CreateNotification("Snap on all axes");
                        break;
                    case BuildingRotationSnapping.AllAxes:
                        state.SnapRotation = BuildingRotationSnapping.None;
                        snapRotationNotification = Extensions.ApiUtilities.CreateNotification("Don't snap rotation");
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
                snapRotationNotification?.Show();
            });

            InputContext.RegisterAction(MyStringHash.GetOrCompute("MoveFurther"), (ref MyInputContext.ActionEvent evt) =>
            {
                if (!TryGetLocal(out var tool, out var player)) return;
                var state = tool.BuildingState;
                state.Distance = Math.Min(
                    player.BuildingDistanceLimit(),
                    state.Distance * DistanceModifier(ref evt));
            });

            InputContext.RegisterAction(MyStringHash.GetOrCompute("MoveCloser"), (ref MyInputContext.ActionEvent evt) =>
            {
                if (!TryGetLocal(out var tool, out _)) return;
                var state = tool.BuildingState;
                state.Distance = Math.Max(0, state.Distance / DistanceModifier(ref evt));
            });
            return;

            // Matches vanilla.
            float DistanceModifier(ref MyInputContext.ActionEvent evt) => (float)Math.Pow(1.1f, evt.AnalogValue / 120);
        }

        private static void HandleRotation(MyInputStateFlags flags, Vector3 cameraLocalAxis)
        {
            if (!TryGetLocal(out var tool, out var player)) return;

            var camera = (Matrix)MyCameraComponent.ActiveCamera.GetWorldMatrix();
            var up = -MyGravityProviderSystem.CalculateNaturalGravityInPoint(camera.Translation);
            var cameraSnap = Matrix.CreateWorld(Vector3.Zero, up, Vector3.Cross(camera.Right, up));
            Matrix.Invert(ref cameraSnap, out var cameraSnapInv);

            Vector3.TransformNormal(ref cameraLocalAxis, ref cameraSnap, out var worldAxis);

            var snapRotations = tool.BuildingRotationBias;
            Matrix snapRotationsInv = default;
            if (snapRotations.M44 > 0)
            {
                Matrix.Invert(ref snapRotations, out snapRotationsInv);
                Vector3.TransformNormal(ref worldAxis, ref snapRotationsInv, out var snapLocalAxis);
                snapLocalAxis = Vector3.DominantAxisProjection(snapLocalAxis);
                Vector3.TransformNormal(ref snapLocalAxis, ref snapRotations, out worldAxis);
            }

            var state = tool.BuildingState;
            var snapAllAxes = state.SnapRotation == BuildingRotationSnapping.AllAxes;

            MyRenderProxy.DebugDrawArrow3D(
                camera.Translation + camera.Forward * 5,
                camera.Translation + camera.Forward * 5 + worldAxis,
                Color.Red,
                depthRead: false
            );
            var deltaAngle = MathHelper.PiOver2 * MyEngineConstants.UPDATE_STEP_SIZE_IN_SECONDS;
            if (snapAllAxes)
                deltaAngle = (flags & MyInputStateFlags.Pressed) != 0 ? MathHelper.PiOver2 : 0;
            var deltaRotation = Quaternion.CreateFromAxisAngle(worldAxis, deltaAngle);
            var newRotation = deltaRotation * state.WorldRotation;
            if (snapAllAxes || state.SnapRotation == BuildingRotationSnapping.RotationAxis)
            {
                // Align all axes that aren't being rotated around.
                var mat = Matrix.CreateFromQuaternion(newRotation);
                if (snapRotations.M44 > 0)
                    SnapAxes(ref mat, ref snapRotations, ref snapRotationsInv, snapAllAxes);
                else
                    SnapAxes(ref mat, ref cameraSnap, ref cameraSnapInv, snapAllAxes);
                Quaternion.CreateFromRotationMatrix(in mat, out newRotation);
            }

            state.WorldRotation = newRotation;
            return;

            void SnapAxes(ref Matrix worldMatrix, ref Matrix snap, ref Matrix snapInv, bool allAxes)
            {
                Matrix.Multiply(ref worldMatrix, ref snapInv, out var localMatrix);
                var localAxis = Vector3.TransformNormal(worldAxis, ref snapInv);
                var x = localMatrix.Right;
                var y = localMatrix.Up;
                var z = localMatrix.Backward;

                var scoreX = Math.Abs(localAxis.Dot(x));
                var scoreY = Math.Abs(localAxis.Dot(y));
                var scoreZ = Math.Abs(localAxis.Dot(z));

                if (scoreX >= scoreY && scoreX >= scoreZ)
                {
                    // X axis is most aligned to rotation axis.
                    Vector3.DominantAxisProjection(ref x, out x);
                    x.Normalize();
                    Vector3.Cross(ref z, ref x, out y);
                    if (allAxes)
                        Vector3.DominantAxisProjection(ref y, out y);
                    y.Normalize();
                    Vector3.Cross(ref x, ref y, out z);
                }
                else if (scoreY >= scoreX && scoreY >= scoreZ)
                {
                    // Y is most aligned to rotation axis.
                    Vector3.DominantAxisProjection(ref y, out y);
                    y.Normalize();
                    Vector3.Cross(ref x, ref y, out z);
                    if (allAxes)
                        Vector3.DominantAxisProjection(ref z, out z);
                    z.Normalize();
                    Vector3.Cross(ref y, ref z, out x);
                }
                else
                {
                    // Z is most aligned to rotation axis.
                    Vector3.DominantAxisProjection(ref z, out z);
                    z.Normalize();
                    Vector3.Cross(ref y, ref z, out x);
                    if (allAxes)
                        Vector3.DominantAxisProjection(ref x, out x);
                    x.Normalize();
                    Vector3.Cross(ref z, ref x, out y);
                }

                localMatrix.Right = x;
                localMatrix.Up = y;
                localMatrix.Backward = z;
                Matrix.Multiply(ref localMatrix, ref snap, out worldMatrix);
            }
        }

        public static void OnActivateWithBuilding(this IToolWithBuilding tool)
        {
            if (!TryGetLocal(out var local, out var player) || local != tool)
                return;
            InputContext.Push();
            var state = tool.BuildingState;
            state.Distance = Math.Min(state.DefaultDistance, player.BuildingDistanceLimit());
        }

        public static void OnDeactivateWithBuilding(this IToolWithBuilding tool)
        {
            if (!TryGetLocal(out var local, out _) || local != tool)
                return;
            InputContext.Pop();
        }
    }
}