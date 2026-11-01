namespace AnimLab.Netcode.Sim
{
    /// <summary>Padrões repetíveis: não dependem de relógio, FPS nem Random.</summary>
    public static class BotInputs
    {
        public static PlayerInput Combat(int tick, int player, in MatchState state)
        {
            PlayerState self = player == 0 ? state.P0 : state.P1, other = player == 0 ? state.P1 : state.P0;
            FixVec3 d = (other.Position - self.Position).Flat;
            Fix yaw = Fix.Atan2(d.X,d.Z);
            long angle = yaw.Raw % Fix.TwoPi.Raw; if (angle < 0) angle += Fix.TwoPi.Raw;
            int t = (tick + player * 37) % 420;
            bool retreat = t >= 270 && t < 310;
            byte move = (byte)(angle * 256 / Fix.TwoPi.Raw + (retreat ? 128 : 0));
            return new PlayerInput { LookYaw = (ushort)(angle * 1024 / Fix.TwoPi.Raw),
                Targeted = true, TargetSlot = (byte)(player == 0 ? 2 : 1),
                MoveAngle = move, Magnitude = d.Length > Fix.Ratio(13,10) || retreat ? (byte)15 : (byte)0,
                Buttons = (t % (player == 0 ? 23 : 29) == 0 ? Buttons.Attack : 0) |
                    (t >= 120 && t < 150 || t >= 190 && t < 220 ? Buttons.Guard : 0) |
                    (t == 275 ? Buttons.Roll : 0) | (t == 330 ? Buttons.Jump : 0) | (t == 348 ? Buttons.Attack : 0) };
        }
        /// <summary>O bot de combate que também lança a Bola de Fogo e o Dash da hotbar (gravações e testes).</summary>
        public static PlayerInput Caster(int tick, int player, in MatchState state)
        {
            var input = Combat(tick, player, state);
            var s = state;
            ref PlayerState self = ref MatchState.Player(ref s, player);
            PlayerState other = MatchState.Player(ref s, player == 0 ? 1 : 0);
            int t = (tick + player * 53) % 240;
            AbilityId want = t == 40 ? AbilityId.FireBall : t == 150 ? AbilityId.Dash : AbilityId.None;
            if (want == AbilityId.None || !self.Config.Knows(want)) return input;
            input.Cast = (byte)want;
            if (want == AbilityId.Dash) { input.Magnitude = 15; input.MoveAngle = (byte)(input.MoveAngle + 64); }
            input.AimX = (short)(other.Position.X * 100).Floor; input.AimZ = (short)(other.Position.Z * 100).Floor;
            return input;
        }
        public static PlayerInput Movement(int tick, int player)
        {
            int t = (tick + player * 90) % 480;
            bool forward = (t / 65) % 2 == 0;
            return new PlayerInput {
                MoveAngle = (byte)((forward ? 64 : 192) + player * 128), Magnitude = 15,
                LookYaw = (ushort)((forward ? 256 : 768) ^ (player * 512)),
                Buttons = (t < 240 ? Buttons.Run : Buttons.None) | (t == 35 || t == 280 ? Buttons.Jump : 0)
                    | (t == 145 ? Buttons.Roll : 0) | (t == 156 ? Buttons.Jump : 0) | (t == 305 ? Buttons.Attack : 0)
            };
        }
    }
}

