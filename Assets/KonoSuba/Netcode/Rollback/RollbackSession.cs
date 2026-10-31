using System;
using System.Text;
using AnimLab.Netcode.Sim;

namespace AnimLab.Netcode.Rollback
{
    public sealed class Checkpoint
    {
        public int Tick;
        public ulong Hash;
        public byte[] State;
    }

    /// <summary>Snapshots antes do input t; estado Tick=t significa que t ainda não foi simulado.</summary>
    public sealed class RollbackSession
    {
        public const int Capacity=128, MaxRollback=8;
        struct Frame { public int Tick; public PlayerInput Value; }
        struct Snapshot { public int Tick; public MatchState State; }
        readonly Frame[] local=new Frame[Capacity],remote=new Frame[Capacity],used=new Frame[Capacity];
        readonly Snapshot[] history=new Snapshot[Capacity];
        readonly Checkpoint[] checkpoints=new Checkpoint[Capacity];
        readonly EventBuffer events=new EventBuffer();
        readonly PlayerInput neutralRemote;
        int dirty=int.MaxValue,nextCheckpoint=30,attempts;
        public MatchState State;
        public MatchState Previous { get; private set; }
        public int LocalPlayer { get; }
        public int InputDelay { get; }
        public int LocalLatest { get; private set; }=-1;
        public int RemoteConfirmedInput { get; private set; }=-1;
        public int ConfirmedTick => Math.Min(State.Tick,Math.Min(LocalLatest,RemoteConfirmedInput)+1);
        public int RemoteTick { get; set; }=-1;
        public bool TimeSync { get; set; }=true;
        public bool Faulted { get; set; }
        public string Error { get; private set; }="";
        public void Fail(string message) { Error=message; Faulted=true; }
        public int Rollbacks { get; private set; }
        public int RolledBackTicks { get; private set; }
        public int LastRollback { get; private set; }
        public int MaximumRollback { get; private set; }
        public int Stalls { get; private set; }
        public int TimingSkips { get; private set; }
        public event Action<SimEvent> EventGenerated;
        public event Action<int,int> Rewound;

        public RollbackSession(MatchState initial,int localPlayer,int inputDelay=2)
        {
            if(localPlayer<0 || localPlayer>1 || inputDelay<0 || inputDelay>8 || initial.Tick!=0) throw new ArgumentOutOfRangeException();
            LocalPlayer=localPlayer; InputDelay=inputDelay; State=Previous=initial;
            PlayerInput neutralLocal=PlayerInput.Neutral(localPlayer==0?initial.P0.Yaw:initial.P1.Yaw);
            neutralRemote=PlayerInput.Neutral(localPlayer==0?initial.P1.Yaw:initial.P0.Yaw);
            for(int i=0;i<Capacity;i++) { local[i].Tick=remote[i].Tick=used[i].Tick=history[i].Tick=-1; }
            Save();
            for(int i=0;i<inputDelay;i++) { Store(local,i,neutralLocal); Store(remote,i,neutralRemote); }
            LocalLatest=RemoteConfirmedInput=inputDelay-1;
        }
        static void Store(Frame[] frames,int tick,PlayerInput input) { frames[tick&(Capacity-1)]=new Frame { Tick=tick,Value=input }; }
        static bool Get(Frame[] frames,int tick,out PlayerInput value)
        {
            Frame f=frames[tick&(Capacity-1)]; value=f.Value; return f.Tick==tick;
        }
        public bool QueueLocal(PlayerInput input)
        {
            int tick=State.Tick+InputDelay;
            if(Get(local,tick,out _)) return false;
            Store(local,tick,input);
            while(Get(local,LocalLatest+1,out _)) LocalLatest++;
            return true;
        }
        public bool TryLocalInput(int tick,out PlayerInput input) => Get(local,tick,out input);
        public bool ReceiveRemote(int tick,PlayerInput input)
        {
            if(tick<0 || tick<State.Tick-Capacity+1 || tick>State.Tick+64) return false;
            if(Get(remote,tick,out PlayerInput known)) {
                if(!known.Equals(input)) { Error="Remote input changed"; Faulted=true; }
                return !Faulted;
            }
            Store(remote,tick,input);
            if(Get(used,tick,out PlayerInput predicted) && !predicted.Equals(input)) dirty=Math.Min(dirty,tick);
            while(Get(remote,RemoteConfirmedInput+1,out _)) RemoteConfirmedInput++;
            return true;
        }
        PlayerInput ForRemote(int tick)
        {
            if(Get(remote,tick,out PlayerInput actual)) return actual;
            for(int t=tick-1;t>=Math.Max(0,tick-Capacity+1);t--)
                if(Get(remote,t,out PlayerInput last)) return last.Predicted;
            return neutralRemote;
        }
        void Save() { history[State.Tick&(Capacity-1)]=new Snapshot { Tick=State.Tick,State=State }; }
        public bool TryState(int tick,out MatchState state)
        {
            Snapshot s=history[tick&(Capacity-1)]; state=s.State; return s.Tick==tick;
        }
        void Simulate()
        {
            int tick=State.Tick;
            if(!Get(local,tick,out PlayerInput mine)) throw new InvalidOperationException("Local input missing");
            PlayerInput theirs=ForRemote(tick); Store(used,tick,theirs); Previous=State;
            World.Step(ref State,LocalPlayer==0?mine:theirs,LocalPlayer==0?theirs:mine,events);
            Save();
            for(int i=0;i<events.Count;i++) EventGenerated?.Invoke(events[i]);
        }
        public void Reconcile()
        {
            if(Faulted) return;
            if(dirty!=int.MaxValue)
            {
                int from=dirty,to=State.Tick; dirty=int.MaxValue;
                if(to-from>MaxRollback || !TryState(from,out MatchState restored)) {
                    Error="Rollback history exceeded"; Faulted=true; return;
                }
                State=restored; Rollbacks++; LastRollback=to-from; RolledBackTicks+=LastRollback;
                MaximumRollback=Math.Max(MaximumRollback,LastRollback); Rewound?.Invoke(from,to);
                while(State.Tick<to) Simulate();
            }
            ConfirmCheckpoints();
        }
        public bool TryAdvance()
        {
            Reconcile();
            if(Faulted) return false;
            if(TimeSync && RemoteTick>=0 && State.Tick-RemoteTick>2 && (++attempts%4)==0) { TimingSkips++; return false; }
            if(!Get(local,State.Tick,out _) || State.Tick-(RemoteConfirmedInput+1)>=MaxRollback) { Stalls++; return false; }
            Simulate(); ConfirmCheckpoints(); return true;
        }
        void ConfirmCheckpoints()
        {
            while(nextCheckpoint<=ConfirmedTick) {
                if(!TryState(nextCheckpoint,out MatchState state)) { Error="Confirmed history expired"; Faulted=true; return; }
                checkpoints[(nextCheckpoint/30)&(Capacity-1)]=new Checkpoint { Tick=nextCheckpoint,Hash=StateCodec.Checksum(state),State=StateCodec.Encode(state) };
                nextCheckpoint+=30;
            }
        }
        public bool TryCheckpoint(int tick,out Checkpoint checkpoint) {
            checkpoint=checkpoints[(tick/30)&(Capacity-1)]; return checkpoint!=null && checkpoint.Tick==tick;
        }
        public string InputHistory()
        {
            var text=new StringBuilder("tick,local_player,local_known,local_angle,local_magnitude,local_yaw,local_buttons,local_targeted,remote_known,remote_angle,remote_magnitude,remote_yaw,remote_buttons,remote_targeted,used_angle,used_magnitude,used_yaw,used_buttons,used_targeted\n");
            for(int t=Math.Max(0,State.Tick-Capacity+1);t<=LocalLatest;t++) {
                bool l=Get(local,t,out PlayerInput a),r=Get(remote,t,out PlayerInput b); Get(used,t,out PlayerInput u);
                text.Append(t).Append(',').Append(LocalPlayer).Append(',').Append(l).Append(',').Append(a.MoveAngle).Append(',').Append(a.Magnitude).Append(',').Append(a.LookYaw).Append(',').Append((byte)a.Buttons)
                    .Append(',').Append(a.Targeted).Append(',').Append(r).Append(',').Append(b.MoveAngle).Append(',').Append(b.Magnitude).Append(',').Append(b.LookYaw).Append(',').Append((byte)b.Buttons).Append(',').Append(b.Targeted)
                    .Append(',').Append(u.MoveAngle).Append(',').Append(u.Magnitude).Append(',').Append(u.LookYaw).Append(',').Append((byte)u.Buttons).Append(',').Append(u.Targeted).Append('\n');
            }
            return text.ToString();
        }
    }
}

