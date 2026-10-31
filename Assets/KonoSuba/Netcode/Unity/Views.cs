using AnimLab.Netcode.Sim;
using UnityEngine;

namespace AnimLab.Netcode.Unity
{
    public static class ViewConvert
    {
        public static float Number(Fix v) => (float)((double)v.Raw / Fix.Unit);
        public static Vector3 Vector(FixVec3 v) => new Vector3(Number(v.X), Number(v.Y), Number(v.Z));
        public static Fix Fixed(float v) => Fix.FromRaw((long)System.Math.Round((double)v * Fix.Unit));
    }

    public interface INetMovementView
    {
        bool ExternalDrive { get; set; }
        void CaptureNetConfig(ref PlayerConfig config);
        PlayerInput SampleNetInput();
        void ApplyNetState(PlayerState state, Vector3 position, float yaw);
    }
    public interface INetCombatView
    {
        bool ExternalDrive { get; set; }
        void CaptureNetConfig(ref PlayerConfig config);
        void ApplyNetState(PlayerState state);
        void PresentNetEvent(SimEvent ev);
    }
    public interface INetSwordView
    {
        bool ExternalDrive { get; set; }
        void CaptureNetConfig(ref PlayerConfig config);
        void ApplyNetState(PlayerState state);
    }
}
