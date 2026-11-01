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
        void ApplyNetState(in PlayerState state, Vector3 position, float yaw);
    }
    public interface INetCombatView
    {
        bool ExternalDrive { get; set; }
        void CaptureNetConfig(ref PlayerConfig config);
        void ApplyNetState(in PlayerState state);
        /// <param name="source">O corpo de quem causou o evento (a cor das faíscas é a da arma dele).</param>
        void PresentNetEvent(SimEvent ev, Component source);
    }
    /// <summary>Componente só de aspeto (lê o estado, não decide nada): continua ligado numa partida online.</summary>
    public interface INetPresentation { }
    /// <summary>Componentes que contribuem para a configuração do lutador (hotbar, mana, ficha).</summary>
    public interface INetConfigSource
    {
        void CaptureNetConfig(ref PlayerConfig config);
    }
    /// <summary>Habilidade da cena que dá os seus parâmetros à tabela da partida.</summary>
    public interface INetAbilitySource
    {
        void CaptureNetAbility(ref AbilityTable table);
    }
    /// <summary>Habilidades do lutador conduzidas pela simulação: VFX dos lançamentos, impactos e projéteis.</summary>
    public interface INetAbilityView
    {
        bool ExternalDrive { get; set; }
        void PresentNetAbility(SimEvent ev, GameObject other);
        void SyncProjectile(int slot, ProjectileState now, ProjectileState before, float alpha, float dt);
        void ClearProjectile(int slot);
        void ApplyNetState(in PlayerState state, float now);
    }
    /// <summary>
    /// O aspeto online de uma habilidade (no componente dela): eventos de lançamento/impacto e projéteis simulados.
    /// A AbilityBar encaminha para o apresentador com o mesmo AbilityId.
    /// </summary>
    public interface INetAbilityPresenter
    {
        AbilityId NetAbility { get; }
        void Present(SimEvent ev, GameObject other);
        void Sync(int slot, in ProjectileState now, in ProjectileState before, float alpha, float dt);
        void Clear(int slot);
    }
    /// <summary>A IA do lutador (DuelistAI): passa os parâmetros do Inspector para a IA da simulação.</summary>
    public interface INetBrainView
    {
        void CaptureNetBrain(ref AiState ai);
    }
    public interface INetSwordView
    {
        bool ExternalDrive { get; set; }
        void CaptureNetConfig(ref PlayerConfig config);
        void ApplyNetState(in PlayerState state);
    }
}
