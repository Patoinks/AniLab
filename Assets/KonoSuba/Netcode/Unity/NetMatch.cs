using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using AnimLab.Netcode.Sim;
using AnimLab.Netcode.Rollback;
using AnimLab.Netcode.Transport;
using UnityEngine;
using UnityEngine.UIElements;

namespace AnimLab.Netcode.Unity
{
    public enum LocalControl { HumanVsBot, Bots, TwoKeyboards }
    [DefaultExecutionOrder(-40)]
    public sealed class NetMatch : MonoBehaviour
    {
        public bool MovementDemo = true;
        public Style FirstStyle = Style.Knight, SecondStyle = Style.Knight;
        public bool UseRollback;
        public bool Endurance;
        public bool CaptureSceneConfig, ShowMenuOnStart, Interpolate = true;
        public int MenuCloseAfterTicks = -1;
        public LocalControl Controls = LocalControl.Bots;
        public int InputDelay = 2;
        /// <summary>Junta à partida a IA da cena (duelista e boneco), como lutadores da simulação.</summary>
        public bool WithAI = true;
        /// <summary>Os dois jogadores na mesma equipa, contra a IA.</summary>
        public bool CoOp;
        /// <summary>Os bots também lançam habilidades (gravações).</summary>
        public bool BotsCast;
        public int LocalPlayer;
        public bool Ready { get; private set; }
        public GameObject LocalActor => actors[LocalPlayer];
        public PlayerState LocalState { get { var s = State; return MatchState.Player(ref s, LocalPlayer); } }
        public GameObject Actor(int id) => actors[id];
        public int LatencyMs = 120, JitterMs = 20, LossPercent = 2;
        public int ViewPeer;
        public LoopbackMatch Loopback { get; private set; }
        public RollbackPeer DirectPeer;
        public MatchState InitialState;
        public bool HasInitialState;
        public int TickLimit = int.MaxValue;
        public string ReportPath;
        public string ConnectionName="Direct UDP";
        public Action Ended;
        public bool? RestoreBackground;
        bool previousBackground, backgroundCaptured;
        public MatchState State;
        public MatchState Previous;
        readonly EventBuffer events = new EventBuffer();
        readonly EventLedger ledger = new EventLedger();
        readonly InputLatch firstInput = new InputLatch(), secondInput = new InputLatch();
        readonly List<Behaviour> disabled = new List<Behaviour>();
        readonly INetMovementView[] movers = new INetMovementView[MatchState.Capacity];
        readonly INetCombatView[] bodies = new INetCombatView[MatchState.Capacity];
        readonly INetSwordView[] swords = new INetSwordView[MatchState.Capacity];
        readonly INetAbilityView[] casters = new INetAbilityView[MatchState.Capacity];
        readonly int[] projectileOwner = new int[MatchState.ProjectileCapacity];
        GameObject remote;
        readonly GameObject[] actors = new GameObject[MatchState.Capacity];
        readonly List<GameObject> boundAI = new List<GameObject>();
        readonly List<(Transform t, Vector3 p, Quaternion r)> poses = new List<(Transform, Vector3, Quaternion)>();
        bool stopped;
        readonly List<GameObject> hidden = new List<GameObject>();
        Label diagnostics;
        readonly List<CharacterController> controllers = new List<CharacterController>();
        double accumulator;
        long networkSteps;
        float statsAt;
        int lastRollbacks;
        float rollbackRate;
        bool statsVisible = true;
        public static NetMatch Active { get; private set; }
        public static bool InputBlocked;
        /// <summary>A tecla está atribuída a uma ação do jogo (o HUD liga isto ao GameInput); as teclas de diagnóstico cedem.</summary>
        public static Func<KeyCode, bool> KeyTaken;
        public int EventsPresented { get; private set; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Active=null; InputBlocked=false; }
        void Awake() { if (Application.isPlaying) { if (Active != null && Active != this) Active.Stop(); Active = this; } }
        public void SwitchView() { if (Loopback != null) { ViewPeer=1-ViewPeer; statsAt=networkSteps/60f; lastRollbacks=(ViewPeer==0?Loopback.A:Loopback.B).Session.Rollbacks; } }

        void Start()
        {
            previousBackground = RestoreBackground ?? Application.runInBackground;
            backgroundCaptured = true;
            Application.runInBackground = true;
            Active = this;
            State = MatchState.Create(); Previous = State;
            State.P0.Config.Style = FirstStyle; State.P1.Config.Style = SecondStyle; Previous = State;
            if (Endurance) { State.P0.Config.MaxHealth = State.P1.Config.MaxHealth = 100000; State.P0.Health = State.P1.Health = 100000; Previous = State; }
            var player = GameObject.Find("Player");
            if (player == null) { Debug.LogError("NetMatch: Player missing"); enabled = false; return; }
            if (CaptureSceneConfig)
            {
                PlayerConfig c = CaptureConfig(player);
                State.P0 = MatchState.CreatePlayer(c,State.P0.Position,State.P0.Yaw);
                State.P1 = MatchState.CreatePlayer(c,State.P1.Position,State.P1.Yaw);
                State.P0.Config.Style = FirstStyle; State.P1.Config.Style = SecondStyle;
                Previous = State;
            }
            if (!HasInitialState)
            {
                State.Abilities = CaptureTable(player);
                // Gravações com bots lançadores: hotbar com o Dash e a Bola de Fogo, mesmo sem árvores aprendidas.
                if (BotsCast) for (int i = 0; i < 2; i++) { ref PlayerState b = ref MatchState.Player(ref State, i); b.Config.Learn(AbilityId.Dash); b.Config.Learn(AbilityId.FireBall); }
                State.P0.Team = State.P1.Team = CoOp ? (byte)1 : (byte)0;
                if (WithAI) foreach (var f in SceneFighters(State.Rng)) MatchState.Add(ref State, f);
                Previous = State;
            }
            if (HasInitialState)
            {
                State = InitialState; Previous = State;
                FirstStyle = State.P0.Config.Style; SecondStyle = State.P1.Config.Style; ViewPeer = LocalPlayer;
            }
            // Os pés do CapsulePlayer vivem fora da hierarquia: a cópia precisa de pés próprios.
            player.SetActive(false);
            remote = Instantiate(player); remote.name = "NetRemote";
            actors[LocalPlayer] = player; actors[1-LocalPlayer] = remote;
            Type targetType = player.GetComponent<MonoBehaviour>().GetType().Assembly.GetType("AnimLab.KonoSuba.LockOnTarget");
            if (targetType != null)
            {
                var target = remote.GetComponent(targetType) ?? remote.AddComponent(targetType);
                targetType.GetField("displayName")?.SetValue(target,"Opponent");
                targetType.GetField("lockMarker")?.SetValue(target,null);
            }
            foreach (var component in remote.GetComponents<MonoBehaviour>())
            {
                if (!(component is INetMovementView)) continue;
                foreach (string field in new[] { "leftFoot", "rightFoot" })
                {
                    var f = component.GetType().GetField(field);
                    var foot = f?.GetValue(component) as Transform;
                    if (foot != null) f.SetValue(component, Instantiate(foot));
                }
            }
            foreach (var component in remote.GetComponents<MonoBehaviour>())
                if (component is INetCombatView) component.GetType().GetField("showNameplate")?.SetValue(component,true);
            Bind(player, LocalPlayer); Bind(remote, 1-LocalPlayer);
            // IA da cena conduzida pela simulação (o host juntou-a ao estado inicial).
            for (int i = 2; i < State.Count; i++)
            {
                var kind = MatchState.Player(ref State, i).Kind;
                var go = GameObject.Find(kind == FighterKind.Duelist ? "Duelist" : "DummyEnemy");
                if (go == null || boundAI.Contains(go)) continue;
                boundAI.Add(go); actors[i] = go; poses.Add((go.transform, go.transform.position, go.transform.rotation)); Bind(go, i);
            }
            player.SetActive(true); remote.SetActive(true);
            if (UseRollback)
            {
                MovementDemo = false;
                if (DirectPeer != null)
                {
                    DirectPeer.Session.EventGenerated += ev => Present(ev,LocalPlayer);
                    DirectPeer.Desynced += _ => DirectPeer.WriteDesync(Path.Combine("Logs","netcode"));
                }
                else
                {
                    Loopback = new LoopbackMatch(State,LatencyMs,JitterMs,LossPercent,InputDelay) { BotsCast = BotsCast };
                    Loopback.A.Session.EventGenerated += ev => Present(ev,0);
                    Loopback.B.Session.EventGenerated += ev => Present(ev,1);
                    Loopback.A.Desynced += _ => Loopback.A.WriteDesync(Path.Combine("Logs","netcode"));
                    Loopback.B.Desynced += _ => Loopback.B.WriteDesync(Path.Combine("Logs","netcode"));
                }
            }
            foreach (string name in new[] { "Duelist", "DummyEnemy" })
            {
                var other = GameObject.Find(name);
                if (other != null && !boundAI.Contains(other))
                {
                    foreach (var component in other.GetComponents<MonoBehaviour>())
                        if (component is INetMovementView)
                            foreach (string field in new[] { "leftFoot", "rightFoot" })
                            {
                                var foot = component.GetType().GetField(field)?.GetValue(component) as Transform;
                                if (foot != null && foot.gameObject.activeSelf) { hidden.Add(foot.gameObject); foot.gameObject.SetActive(false); }
                            }
                    hidden.Add(other); other.SetActive(false);
                }
            }
            var document = FindFirstObjectByType<UIDocument>();
            if (document != null)
            {
                diagnostics = new Label();
                diagnostics.style.position = Position.Absolute; diagnostics.style.left = 20; diagnostics.style.top = 110;
                diagnostics.style.color = Color.white; diagnostics.style.fontSize = 18;
                diagnostics.style.backgroundColor = new Color(0,0,0,0.7f); diagnostics.style.paddingLeft = 12;
                diagnostics.style.paddingRight = 12; diagnostics.style.paddingTop = 8; diagnostics.style.paddingBottom = 8;
                diagnostics.pickingMode = PickingMode.Ignore;
                document.rootVisualElement.Add(diagnostics);
            }
            foreach (var c in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                if (c.gameObject != player && c.gameObject != remote && !boundAI.Contains(c.gameObject) && (c.GetType().Name == "DuelistAI" || c.GetType().Name == "SwordWielder"))
                    Disable(c);
            Apply(1f);
            Ready = true;
            if (ShowMenuOnStart) { OnlineMenu.Current?.ShowMatchOptions(this); OnlineMenu.Current?.SetOpen(true); }
        }
        /// <summary>Lutadores da IA da cena (Duelist, DummyEnemy) com a configuração do Inspector, na equipa 2.</summary>
        public static PlayerState[] SceneFighters(ulong seed)
        {
            var list = new List<PlayerState>();
            foreach (var (name, kind) in new[] { ("Duelist", FighterKind.Duelist), ("DummyEnemy", FighterKind.Dummy) })
            {
                var go = GameObject.Find(name);
                if (go == null) continue;
                PlayerConfig c = CaptureConfig(go);
                if (kind == FighterKind.Dummy) { c.Walk = c.Run = 0; c.Style = Style.Knight; }
                Vector3 at = go.transform.position;
                var f = MatchState.CreatePlayer(c, new FixVec3(ViewConvert.Fixed(at.x), Fix.Zero, ViewConvert.Fixed(at.z)),
                    ViewConvert.Fixed(go.transform.eulerAngles.y * Mathf.Deg2Rad));
                f.Kind = kind; f.Team = 2;
                if (kind == FighterKind.Duelist)
                {
                    f.Ai = AiState.Duelist(seed * 0x9E3779B97F4A7C15UL + (ulong)list.Count + 1);
                    foreach (var b in go.GetComponents<MonoBehaviour>()) if (b is INetBrainView brain) brain.CaptureNetBrain(ref f.Ai);
                    f.Ai.NoiseSeed = Fix.FromInt(list.Count * 37 + 11);
                }
                list.Add(f);
            }
            return list.ToArray();
        }
        public static PlayerConfig CaptureConfig(GameObject actor)
        {
            PlayerConfig config = PlayerConfig.Default;
            foreach (var component in actor.GetComponents<MonoBehaviour>())
            {
                if (component is INetMovementView m) m.CaptureNetConfig(ref config);
                else if (component is INetCombatView b) b.CaptureNetConfig(ref config);
                else if (component is INetSwordView w) w.CaptureNetConfig(ref config);
                if (component is INetConfigSource source) source.CaptureNetConfig(ref config);
            }
            return config;
        }
        /// <summary>Parâmetros das habilidades lidos dos componentes do jogador (a mesma fonte do offline).</summary>
        public static AbilityTable CaptureTable(GameObject actor)
        {
            AbilityTable table = AbilityTable.Default;
            if (actor != null)
                foreach (var component in actor.GetComponents<MonoBehaviour>())
                    if (component is INetAbilitySource source) source.CaptureNetAbility(ref table);
            return table;
        }
        /// <summary>Índice do lutador conduzido por este objeto (-1 se não está na partida).</summary>
        public static int FighterIndexOf(GameObject go)
        {
            var m = Active;
            if (m == null || go == null) return -1;
            for (int i = 0; i < m.actors.Length; i++) if (m.actors[i] == go) return i;
            return -1;
        }
        void Disable(Behaviour b) { if (b.enabled) { disabled.Add(b); b.enabled = false; } }
        void Bind(GameObject go, int id)
        {
            // O collider continua selecionável; nenhum script o move ou resolve a simulação por ele.
            foreach (var c in go.GetComponents<MonoBehaviour>())
            {
                if (c is INetMovementView m) { movers[id] = m; m.ExternalDrive = true; }
                else if (c is INetCombatView b) { bodies[id] = b; b.ExternalDrive = true; }
                else if (c is INetSwordView w) { swords[id] = w; w.ExternalDrive = true; }
                else if (c is INetAbilityView a) { casters[id] = a; a.ExternalDrive = true; }
                else if (c is INetPresentation) { }
                else if (c.GetType().Name != "LockOnTarget" && !(id == LocalPlayer && c.GetType().Name == "LockOn")) Disable(c);
            }
        }
        void Update()
        {
            if (movers[0] == null) return;
            if (DirectPeer != null)
            {
                DirectPeer.Pump(NetworkClock.Now);
                State = DirectPeer.Session.State; Previous = DirectPeer.Session.Previous;
                bool completedTest = TickLimit != int.MaxValue && State.Tick >= TickLimit &&
                    DirectPeer.Session.ConfirmedTick >= TickLimit && DirectPeer.ChecksumsVerified >= TickLimit/30;
                if (DirectPeer.Link != LinkState.Connected && !completedTest) { OnlineMenu.Current?.ShowMessage(DirectPeer.Status,true); Stop(); return; }
            }
            if (Controls != LocalControl.Bots)
            {
                firstInput.Sample(movers[LocalPlayer].SampleNetInput());
                if (Controls == LocalControl.TwoKeyboards)
                    secondInput.Sample(InputBlocked ? default : InputSampling.SecondKeyboard(actors[1].transform,actors[0].transform));
            }
            if (Input.GetKeyDown(KeyCode.F9) && UseRollback && !(KeyTaken?.Invoke(KeyCode.F9) ?? false)) SwitchView();
            if (Input.GetKeyDown(KeyCode.F11) && !(KeyTaken?.Invoke(KeyCode.F11) ?? false)) statsVisible = !statsVisible;
            accumulator += Time.captureFramerate > 0 ? 1.0 / Time.captureFramerate : Math.Min(Time.unscaledDeltaTime, 0.25);
            while (accumulator >= 1.0 / 60)
            {
                if (UseRollback)
                {
                    if (DirectPeer != null)
                    {
                        var direct = DirectPeer.Session;
                        DirectPeer.Pump(NetworkClock.Now);
                        if (direct.State.Tick < TickLimit && DirectPeer.Link == LinkState.Connected)
                        {
                            PlayerInput input = Controls == LocalControl.Bots ? BotInputs.Combat(direct.State.Tick,LocalPlayer,direct.State) : firstInput.Pending;
                            if (direct.QueueLocal(input)) firstInput.Consume();
                            direct.TryAdvance();
                        }
                        DirectPeer.SendInputs(NetworkClock.Now); State = direct.State; Previous = direct.Previous;
                    }
                    else
                    {
                        Loopback.Tick(networkSteps*1000/60,int.MaxValue,Controls == LocalControl.Bots ? (PlayerInput?)null : firstInput.Pending,
                            Controls == LocalControl.TwoKeyboards ? secondInput.Pending : (PlayerInput?)null);
                        if (Loopback.FirstQueued) firstInput.Consume();
                        if (Loopback.SecondQueued) secondInput.Consume();
                        var session = ViewPeer == 0 ? Loopback.A.Session : Loopback.B.Session;
                        State = session.State; Previous = session.Previous;
                    }
                    networkSteps++;
                }
                else
                {
                    Previous = State;
                    PlayerInput a = MovementDemo ? BotInputs.Movement(State.Tick,0) : BotInputs.Combat(State.Tick,0,State);
                    PlayerInput b = MovementDemo ? BotInputs.Movement(State.Tick,1) : BotInputs.Combat(State.Tick,1,State);
                    World.Step(ref State, a, b, events);
                    for (int i = 0; i < events.Count; i++) Present(events[i],0);
                }
                accumulator -= 1.0 / 60;
            }
            Apply(Interpolate ? (float)(accumulator * 60) : 1f);
            ApplyProjectiles(Interpolate ? (float)(accumulator * 60) : 1f);
            if (MenuCloseAfterTicks >= 0 && networkSteps >= MenuCloseAfterTicks)
            {
                OnlineMenu.Current?.SetOpen(false); MenuCloseAfterTicks = -1;
            }
            if (diagnostics != null) diagnostics.text = $"{(MovementDemo ? "DETERMINISTIC MOVEMENT" : "DETERMINISTIC COMBAT")} — 60 Hz\nTick {State.Tick}   Checksum {StateCodec.Checksum(State):X16}\nHealth {ViewConvert.Number(State.P0.Health):F0} / {ViewConvert.Number(State.P1.Health):F0}   Events {EventsPresented}";
            if (UseRollback && diagnostics != null)
            {
                var peer = DirectPeer ?? (ViewPeer == 0 ? Loopback.A : Loopback.B);
                var session = peer.Session;
                float seconds = networkSteps/60f;
                if (seconds-statsAt>=1f) { rollbackRate=(session.Rollbacks-lastRollbacks)/(seconds-statsAt); lastRollbacks=session.Rollbacks; statsAt=seconds; }
                string connection = DirectPeer == null ? $"LOOPBACK — peer {ViewPeer} (F9)\nOne-way {LatencyMs} ms ± {JitterMs} ms   Loss {LossPercent}%" : $"DIRECT UDP — player {LocalPlayer+1}\nPackets sent {peer.PacketsSent}   Sequence gaps {peer.PacketGaps}";
                diagnostics.text = $"ROLLBACK {connection}   Ping {peer.PingMs} ms\nElapsed {networkSteps/60f:F1}s   Tick {State.Tick}   Confirmed {session.ConfirmedTick}\nRollbacks {session.Rollbacks} ({rollbackRate:F1}/s)   Resim {session.RolledBackTicks} ticks   Max {session.MaximumRollback}/8\nDelay {session.InputDelay}   Stalls {session.Stalls}   Dropped {(Loopback != null ? Loopback.Network.Dropped : peer.PacketGaps)}\nVerified {peer.ChecksumsVerified}   Desync {(peer.Desync == null ? 0 : 1)}   {peer.Status}\nEvents {EventsPresented}   Duplicates suppressed {ledger.Suppressed}";
            }
            if (diagnostics != null) diagnostics.style.display = statsVisible || DirectPeer?.Desync != null || Loopback?.A.Desync != null || Loopback?.B.Desync != null ? DisplayStyle.Flex : DisplayStyle.None;
            OnlineMenu.Current?.BringToFront();
        }
        void Present(SimEvent ev,int peer)
        {
            if (peer != ViewPeer || !ledger.Present(ev)) return;
            EventsPresented++;
            // Lançamentos, impactos e projéteis: a vista de habilidades de quem lançou.
            if (ev.Kind == EventKind.Cast || ev.Kind == EventKind.Impact || ev.Kind == EventKind.Fizzle)
            {
                if (ev.Source >= 0 && ev.Source < casters.Length)
                    casters[ev.Source]?.PresentNetAbility(ev, ev.Other >= 0 && ev.Other < actors.Length ? actors[ev.Other] : null);
                return;
            }
            bodies[ev.Target]?.PresentNetEvent(ev, bodies[ev.Source] as Component);
        }
        void Apply(float alpha)
        {
            var now = State; var before = Previous;
            for (int i = 0; i < now.Count; i++)
            {
                PlayerState p = MatchState.Player(ref now, i), old = i < before.Count ? MatchState.Player(ref before, i) : p;
                PlayerState visual = p;
                Fix blend = ViewConvert.Fixed(alpha);
                if (p.Motion == old.Motion) visual.MotionTime = Fix.Lerp(old.MotionTime,p.MotionTime,blend);
                if (p.Weapon == old.Weapon && p.Swing == old.Swing) visual.WeaponTime = Fix.Lerp(old.WeaponTime,p.WeaponTime,blend);
                visual.Velocity = FixVec3.Lerp(old.Velocity,p.Velocity,blend);
                Vector3 position = Vector3.Lerp(ViewConvert.Vector(old.Position), ViewConvert.Vector(p.Position), alpha);
                float yaw = Mathf.LerpAngle(ViewConvert.Number(old.Yaw) * Mathf.Rad2Deg, ViewConvert.Number(p.Yaw) * Mathf.Rad2Deg, alpha);
                if (movers[i] != null) movers[i].ApplyNetState(visual, position, yaw);
                else if (actors[i] != null) actors[i].transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
                bodies[i]?.ApplyNetState(p);
                casters[i]?.ApplyNetState(p, ViewConvert.Number(Fix.FromInt(now.Tick) * World.Dt));
                swords[i]?.ApplyNetState(visual);
            }
        }
        // Projéteis: cada posição do estado é desenhada pela vista de quem a lançou (refeita se um rollback a muda).
        void ApplyProjectiles(float alpha)
        {
            var now = State; var before = Previous;
            for (int i = 0; i < MatchState.ProjectileCapacity; i++)
            {
                ProjectileState b = MatchState.Projectile(ref now, i), old = MatchState.Projectile(ref before, i);
                int owner = b.Kind == AbilityId.None ? -1 : b.Owner;
                if (projectileOwner[i] != owner + 1 && projectileOwner[i] > 0) casters[projectileOwner[i] - 1]?.ClearProjectile(i);
                projectileOwner[i] = owner + 1;
                if (owner < 0) continue;
                if (old.Kind != b.Kind || old.Owner != b.Owner) old = b;
                casters[owner]?.SyncProjectile(i, b, old, alpha, Time.unscaledDeltaTime);
            }
        }
        void OnDestroy()
        {
            Shutdown();
        }
        public void Stop()
        {
            enabled = false; Shutdown(); Destroy(gameObject);
        }
        void Shutdown()
        {
            if (stopped) return;
            stopped = true; Ready = false;
            if (backgroundCaptured) Application.runInBackground = previousBackground;
            // Termina efeitos de tempo da apresentação antes de retomar o jogador offline.
            foreach (var component in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                if (component.GetType().FullName == "AnimLab.KonoSuba.HitStop")
                {
                    Type type = component.GetType();
                    foreach (string field in new[] { "remaining", "slowRemaining" })
                        type.GetField(field,BindingFlags.Instance|BindingFlags.NonPublic)?.SetValue(component,0f);
                    foreach (string field in new[] { "scale", "slowScale" })
                        type.GetField(field,BindingFlags.Instance|BindingFlags.NonPublic)?.SetValue(component,1f);
                }
            Time.timeScale = 1f;
            if (DirectPeer != null) { WriteReport(); DirectPeer.Dispose(); }
            var ended=Ended; Ended=null; ended?.Invoke();
            if (Loopback != null)
            {
                Directory.CreateDirectory("Logs/netcode");
                var a = Loopback.A; var b = Loopback.B;
                File.WriteAllText("Logs/netcode/capture_summary.txt",$"wall_seconds={networkSteps/60f:F2}; ticks={a.Session.State.Tick}/{b.Session.State.Tick}; rollbacks={a.Session.Rollbacks}/{b.Session.Rollbacks}; resimulated={a.Session.RolledBackTicks}/{b.Session.RolledBackTicks}; max_depth={a.Session.MaximumRollback}/{b.Session.MaximumRollback}; verified={a.ChecksumsVerified}/{b.ChecksumsVerified}; dropped={Loopback.Network.Dropped}; desync={(a.Desync == null && b.Desync == null ? 0 : 1)}");
                Loopback.Dispose();
            }
            for (int i = 0; i < MatchState.ProjectileCapacity; i++)
                if (projectileOwner[i] > 0) { casters[projectileOwner[i] - 1]?.ClearProjectile(i); projectileOwner[i] = 0; }
            for (int i = 0; i < MatchState.Capacity; i++) {
                if ((casters[i] as UnityEngine.Object) != null) casters[i].ExternalDrive = false;
                if ((movers[i] as UnityEngine.Object) != null) movers[i].ExternalDrive = false;
                if ((bodies[i] as UnityEngine.Object) != null) bodies[i].ExternalDrive = false;
                if ((swords[i] as UnityEngine.Object) != null) swords[i].ExternalDrive = false;
            }
            foreach (var b in disabled) if (b != null && (remote == null || b.gameObject != remote)) b.enabled = true;
            foreach (var cc in controllers) if (cc != null && (remote == null || cc.gameObject != remote)) cc.enabled = true;
            if (remote != null) Destroy(remote);
            foreach (var (t, p, r) in poses) if (t != null) t.SetPositionAndRotation(p, r);
            foreach (var go in hidden) if (go != null) go.SetActive(true);
            diagnostics?.RemoveFromHierarchy();
            if (Active == this) Active = null;
        }
        public void WriteReport()
        {
            if (DirectPeer == null || string.IsNullOrEmpty(ReportPath)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(ReportPath)));
            var s = DirectPeer.Session;
            File.WriteAllText(ReportPath,$"player={LocalPlayer}; tick={s.State.Tick}; confirmed={s.ConfirmedTick}; verified={DirectPeer.ChecksumsVerified}; hash={StateCodec.Checksum(s.State):X16}; desync={(DirectPeer.Desync == null ? 0 : 1)}; rollbacks={s.Rollbacks}; resimulated={s.RolledBackTicks}; max_depth={s.MaximumRollback}; link={DirectPeer.Link}");
        }
    }

    public static class NetcodeCapture
    {
        public static void Movement()
        {
            var go = new GameObject("NetMatch");
            go.AddComponent<NetMatch>().MovementDemo = true;
        }
        public static void Combat()
        {
            var go = new GameObject("NetMatch");
            var match = go.AddComponent<NetMatch>(); match.MovementDemo = false;
            string[] args = Environment.GetCommandLineArgs();
            int a = Array.IndexOf(args,"-netStyle0"), b = Array.IndexOf(args,"-netStyle1");
            if (a >= 0 && a+1 < args.Length && Enum.TryParse(args[a+1],true,out Style first)) match.FirstStyle = first;
            if (b >= 0 && b+1 < args.Length && Enum.TryParse(args[b+1],true,out Style second)) match.SecondStyle = second;
        }
        public static void Rollback()
        {
            Combat();
            var match = UnityEngine.Object.FindFirstObjectByType<NetMatch>();
            match.UseRollback = true;
        }
        public static void Soak()
        {
            Rollback();
            UnityEngine.Object.FindFirstObjectByType<NetMatch>().Endurance = true;
        }
        public static void Online()
        {
            Rollback();
            var match = UnityEngine.Object.FindFirstObjectByType<NetMatch>();
            match.CaptureSceneConfig = true;
            match.ShowMenuOnStart = true;
            match.MenuCloseAfterTicks = 150;
            match.BotsCast = OnlineConnection.Arg("-netCast", "0") == "1";
        }
    }
}
