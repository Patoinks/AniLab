using System;
using System.IO;
using AnimLab.Netcode.Sim;
using AnimLab.Netcode.Rollback;

// Verificação fora do Unity: o mesmo núcleo, compilado para CoreCLR.
internal static class Program
{
    static void Main()
    {
        const int ticks=10800;
        var initial=MatchState.Create();
        initial.P0.Health=initial.P1.Health=100000;
        initial.P0.Config.MaxHealth=initial.P1.Config.MaxHealth=100000;
        initial.P0.Config.Style=Style.Reaper; initial.P1.Config.Style=Style.Daggers;
        var p=new PlayerInput[ticks+16]; var q=new PlayerInput[ticks+16];
        using(var match=new LoopbackMatch(initial,120,20,2)) {
            for(int i=0;i<2;i++) { match.A.Session.TryLocalInput(i,out p[i]); match.B.Session.TryLocalInput(i,out q[i]); }
            int attempts=0; long now=0;
            while((match.A.Session.State.Tick<ticks || match.B.Session.State.Tick<ticks) && attempts<ticks*4+1000) {
                now=attempts*1000L/60; match.Pump(now);
                Schedule(match.A.Session,p,ticks); Schedule(match.B.Session,q,ticks);
                match.Send(now); match.Pump(now); attempts++;
                if(match.A.Session.Faulted || match.B.Session.Faulted) throw new Exception(match.A.Status+" / "+match.B.Status);
            }
            for(int i=0;i<80;i++) { now+=17; match.Pump(now); match.Send(now); }
            var oracle=initial; var events=new EventBuffer();
            for(int i=0;i<ticks;i++) World.Step(ref oracle,p[i],q[i],events);
            ulong hash=StateCodec.Checksum(oracle);
            if(match.A.Session.State.Tick!=ticks || match.B.Session.State.Tick!=ticks ||
                StateCodec.Checksum(match.A.Session.State)!=hash || StateCodec.Checksum(match.B.Session.State)!=hash ||
                match.A.Desync!=null || match.B.Desync!=null || match.A.ChecksumsVerified!=360 || match.B.ChecksumsVerified!=360)
                throw new Exception("Determinism or convergence failed");
            string result=$"CoreCLR ticks={ticks}; hash={hash:X16}; verified={match.A.ChecksumsVerified}/{match.B.ChecksumsVerified}; rollbacks={match.A.Session.Rollbacks}/{match.B.Session.Rollbacks}; desync=0";
            Directory.CreateDirectory("Logs/netcode"); File.WriteAllText("Logs/netcode/coreclr.txt",result);
            Console.WriteLine(result);
        }
    }
    static void Schedule(RollbackSession s,PlayerInput[] inputs,int limit)
    {
        if(s.State.Tick>=limit) return;
        int tick=s.State.Tick+s.InputDelay; var input=BotInputs.Combat(s.State.Tick,s.LocalPlayer,s.State);
        if(s.QueueLocal(input)) inputs[tick]=input;
        s.TryAdvance();
    }
}
