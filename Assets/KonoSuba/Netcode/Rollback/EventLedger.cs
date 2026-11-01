using AnimLab.Netcode.Sim;

namespace AnimLab.Netcode.Rollback
{
    /// <summary>
    /// Faíscas e números já apresentados sobrevivem; a ressimulação não os repete.
    /// O mesmo contacto (tipo, origem, alvo, habilidade, marca) que uma correção desloca alguns ticks conta como o mesmo
    /// evento; contactos diferentes no mesmo alvo (lâmina e onda do pique, golpe e explosão) mostram-se os dois.
    /// </summary>
    public sealed class EventLedger
    {
        // Janela de deduplicação: um rollback nunca desloca um evento mais do que isto.
        public const int Window = RollbackSession.MaxRollback;
        struct Entry { public int Tick, Kind, Source, Target, Ability, Other; }
        readonly Entry[] shown = new Entry[64];
        int next;
        public int Shown { get; private set; }
        public int Suppressed { get; private set; }
        public EventLedger() { for (int i = 0; i < shown.Length; i++) shown[i].Tick = int.MinValue; }
        public bool Present(SimEvent ev)
        {
            for (int i = 0; i < shown.Length; i++)
            {
                Entry e = shown[i];
                if (e.Kind == (int)ev.Kind && e.Source == ev.Source && e.Target == ev.Target && e.Ability == (int)ev.Ability && e.Other == ev.Other &&
                    e.Tick != int.MinValue && System.Math.Abs(e.Tick - ev.Tick) <= Window) { Suppressed++; return false; }
            }
            shown[next] = new Entry { Tick = ev.Tick, Kind = (int)ev.Kind, Source = ev.Source, Target = ev.Target, Ability = (int)ev.Ability, Other = ev.Other };
            next = (next + 1) % shown.Length; Shown++;
            return true;
        }
    }
}
