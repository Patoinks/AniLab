using AnimLab.Netcode.Sim;
using AnimLab.Netcode.Rollback;
using NUnit.Framework;

namespace AnimLab.Netcode.Tests
{
    public class EventTests
    {
        [Test] public void ResimulationDoesNotDuplicateTransientEvents()
        {
            var ledger=new EventLedger(); var e=new SimEvent { Tick=90,Id=123,Kind=EventKind.Hit };
            Assert.IsTrue(ledger.Present(e));
            for(int i=0;i<8;i++) Assert.IsFalse(ledger.Present(e));
            e.Id++; Assert.IsTrue(ledger.Present(e)); e.Tick++; Assert.IsTrue(ledger.Present(e));
            Assert.AreEqual(3,ledger.Shown); Assert.AreEqual(8,ledger.Suppressed);
            e.Tick+=128; Assert.IsTrue(ledger.Present(e));
        }
    }
}
