using AnimLab.Netcode.Sim;

namespace AnimLab.Netcode.Rollback
{
    /// <summary>Guarda flancos entre frames de render; só os consome quando o input é agendado.</summary>
    public sealed class InputLatch
    {
        const Buttons Edges=Buttons.Attack|Buttons.Jump|Buttons.Roll;
        public PlayerInput Pending { get; private set; }
        public void Sample(PlayerInput input) { input.Buttons|=Pending.Buttons&Edges; if(input.Cast==0) input.Cast=Pending.Cast; Pending=input; }
        public void Consume() { var p=Pending; p.Buttons&=~Edges; p.Cast=0; Pending=p; }
    }
}
