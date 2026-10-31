using AnimLab.Netcode.Sim;
using AnimLab.Netcode.Rollback;
using NUnit.Framework;

namespace AnimLab.Netcode.Tests
{
    public class EventTests
    {
        [Test] public void ResimulationDoesNotDuplicateTransientEvents()
        {
            var ledger=new EventLedger(); var e=new SimEvent { Tick=90,Kind=EventKind.Hit,Source=0,Target=1 };
            Assert.IsTrue(ledger.Present(e));
            for(int i=0;i<8;i++) Assert.IsFalse(ledger.Present(e));
            // Uma correção que desloca o mesmo golpe um tick (previsto no 90, real no 91) não o mostra outra vez.
            e.Tick++; Assert.IsFalse(ledger.Present(e));
            // Outro contacto (outro alvo/tipo) aparece.
            var other=e; other.Source=1; other.Target=0; Assert.IsTrue(ledger.Present(other));
            var block=e; block.Kind=EventKind.Block; Assert.IsTrue(ledger.Present(block));
            // Fora da janela de rollback é um golpe novo.
            e.Tick=90+EventLedger.Window+1; Assert.IsTrue(ledger.Present(e));
            Assert.AreEqual(4,ledger.Shown); Assert.AreEqual(9,ledger.Suppressed);
        }
    }
}
