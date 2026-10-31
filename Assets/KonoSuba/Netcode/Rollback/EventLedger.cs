using System.Collections.Generic;
using AnimLab.Netcode.Sim;

namespace AnimLab.Netcode.Rollback
{
    /// <summary>Faíscas e números já apresentados sobrevivem; a ressimulação não os repete.</summary>
    public sealed class EventLedger
    {
        readonly int[] ticks=new int[128];
        readonly HashSet<int>[] ids=new HashSet<int>[128];
        public int Shown { get; private set; }
        public int Suppressed { get; private set; }
        public EventLedger() { for(int i=0;i<128;i++) { ticks[i]=-1; ids[i]=new HashSet<int>(); } }
        public bool Present(SimEvent ev)
        {
            int slot=ev.Tick&127;
            if(ticks[slot]!=ev.Tick) { ticks[slot]=ev.Tick; ids[slot].Clear(); }
            if(!ids[slot].Add(ev.Id)) { Suppressed++; return false; }
            Shown++; return true;
        }
    }
}
