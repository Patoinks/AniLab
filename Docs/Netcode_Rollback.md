# Online P2P com rollback (estilo GGPO) — plano de implementação

Objetivo: testar o combate do AnimLab em **1v1 online P2P** com o melhor *netcode* possível. Isto quer dizer rollback determinístico: os dois PCs correm a mesma simulação, trocam só inputs, preveem os do outro e, quando chega um input atrasado, voltam atrás e ressimulam. Assim:

- não há host com vantagem;
- o parry (0,2 s), o clash e o roll ficam justos nos dois lados;
- a resposta local é imediata.

Projeto: Unity 6000.3.15f1, URP, cena `Assets/Scenes/KonoSubaVoid.unity`, código em `Assets/KonoSuba/Scripts`.

## 1. Princípios

- **Simulação determinística em C# puro**, separada do Unity: sem `UnityEngine` no núcleo, sem `float`, sem `Time`, sem `Random`, sem iterar `Dictionary`/`HashSet` e sem física do Unity. A ordem é sempre fixa (jogador 0, depois 1).
- **Os MonoBehaviours atuais passam a ser só apresentação** num jogo em rede: espada, pés, faíscas, números, placas e HUD leem o estado da simulação. O modo de um jogador continua a funcionar exatamente como hoje.
- **Código novo** em `Assets/KonoSuba/Netcode/` com `asmdef`s próprias. Nos ficheiros que já existem, só os ganchos mínimos (ver §6).
- Comentários em português, no estilo do projeto. Texto visível no jogo em inglês.

## 2. Núcleo determinístico (`AnimLab.Netcode.Sim`, asmdef sem referências ao Unity)

### 2.1 Matemática de vírgula fixa

- `Fix`: Q32.32 num `long`, com as operações aritméticas e de comparação, `Sqrt` (inteira), `Sin`, `Cos` e `Atan2` por tabela com interpolação, `Clamp`, `Lerp`, `Min` e `Max`.
- `FixVec3` com `Dot`, `Cross`, `Length`, `Normalize`, `Lerp`, `Slerp` (aproximado, determinístico) e `RotateY`.
- Quaternions só se forem precisos; senão, bases ortonormais (eixo da lâmina + eixo do gume) como em `SwordWielder.Make`.
- Conversão para `float` **só** na apresentação.
- RNG: xorshift64 com semente da partida, guardado no estado (para os críticos).

### 2.2 Tick, input e estado

- **Tick fixo a 60 Hz.**
- **Input por jogador e por tick (struct pequena, serializável em poucos bytes):**
  - direção de movimento já em espaço do mundo, quantizada (ângulo de 8 bits + magnitude de 4 bits);
  - yaw desejado para onde olhar (alvo selecionado ou câmara), 8–10 bits;
  - bits: `Attack`, `Guard`, `Jump`, `Roll` e `Run`;
  - `Attack`, `Jump` e `Roll` são flancos (*pressed*), gerados no cliente a partir do `GameInput`.
- **`MatchState`, copiável por valor ou serialização barata:**
  - número do tick e RNG;
  - 2 `PlayerState`:
    - posição, velocidade e yaw;
    - estado de movimento (Ground/Air/Roll/Plunge), tempos e invulnerabilidade;
    - estado da espada (Idle/Windup/Active/Recovery/Guard/Recoil/Plunge), golpe atual, tempo no estado e `queued`;
    - guarda (instante de início, último fim, *parry armed*);
    - postura, vida, stagger até, knockback e velocidade vertical;
    - super armor, estilo e stats derivados (constantes da partida);
    - hit-stop restante.
- **Hit-stop na simulação:** os ticks de paragem congelam a lógica dos dois corpos envolvidos, mas o tick avança. O slow-mo do parry é só visual.

### 2.3 Regras a portar (mesmos números que hoje)

Ler e portar fielmente:

| Fonte | O quê |
|---|---|
| `CapsulePlayer.cs` | walk/run, aceleração, viragem, gravidade, salto, roll (com invulnerabilidade), salto longo depois do roll, golpe a pique no ar (homing, impacto com onda em área), *land lag* |
| `SwordWielder.cs` | combo e fila de golpes, `Arc(i,u)` por estilo (Knight, Fencer, Reaper, Daggers, Greatsword), varrimento da lâmina por subpassos do arco, guarda/parry (janela + lockout), bloqueio (fração do dano, postura, ressalto, exceto o Greatsword), parry (stagger do atacante), clash (segmento–segmento), guard break do Greatsword, puxão do Reaper até à distância ideal, super armor |
| `Combatant.cs` | dano com defesa, postura e stagger, knockback com atrito e queda, invulnerabilidade |
| `CharacterStats.cs` | os derivados entram como constantes de cada jogador no início da partida |

- **Colisões:** cápsula–cápsula entre jogadores (empurrar para fora), chão `y = 0` e limite circular da arena. Os testes de golpe são segmento (lâmina) contra cápsula, como no `Combatant.ClosestPoint`.
- **Fora da primeira versão:** habilidades elementais, estados e AI. Ficam preparadas como eventos para depois.

### 2.4 Eventos

A simulação emite eventos (golpe, bloqueio, parry, clash, guard break, aterrar, pique) com `(tick, id)`. A apresentação só os mostra quando aparecem num tick e **desfaz** os que um rollback anula: faíscas e números já lançados ficam, mas nada se duplica ao ressimular. Basta guardar os `(tick, id)` já mostrados.

### 2.5 Testes EditMode (obrigatórios)

1. A mesma lista de inputs corrida duas vezes dá o mesmo checksum em todos os ticks.
2. Inputs entregues com atraso e rollback (simulado) dão o mesmo estado final que sem atraso.
3. Testes de regras: o parry dentro da janela atordoa o atacante; o bloqueio corta 80%; o clash faz ressaltar os dois; o roll evita o golpe; o Guard Breaker parte a guarda.

## 3. Sessão de rollback (`AnimLab.Netcode.Rollback`)

- **Atraso de input local** configurável (por omissão 2 ticks).
- **Previsão:** repete o último input do outro jogador, com os flancos a zero.
- **Buffer circular de estados:** 128 ticks guardados; rollback máximo de 8 ticks. Para lá disso, o jogo espera (*stall*).
- **Ao chegar um input remoto diferente do previsto:** carrega o estado desse tick e ressimula até ao presente na mesma frame.
- **Sincronização de tempo:** troca de *frame advantage*. Quem está adiantado abranda ligeiramente (saltar 1 tick a cada N), como no GGPO.
- **Checksum** do estado a cada 30 ticks confirmados, trocado com o outro PC. Uma divergência regista *desync*, guarda os dois estados e os inputs em `Logs/netcode/`, e mostra-a no ecrã.
- **Estatísticas:** ping, ticks de rollback, atraso e pacotes perdidos, num overlay (F11).

## 4. Transportes (`ITransport`)

1. **Loopback:** duas sessões no mesmo processo, com latência, jitter e perda configuráveis. É o banco de testes principal.
2. **UDP direto (IP:porta):** cada pacote leva os últimos N inputs (redundância, sem retransmissões) e os acks. Handshake, keep-alive e timeout.
3. **Steam P2P (relay, sem abrir portas):** Facepunch.Steamworks ou Steamworks.NET com o App ID de teste 480. Convite ou entrada por Lobby ID. Faz-se no fim, depois de 1 e 2 estarem sólidos.

## 5. Apresentação no Unity (`AnimLab.Netcode.Unity`)

- **`NetMatch` (MonoBehaviour):**
  - corre a sessão com um acumulador de 60 Hz;
  - lê o input local pelo `GameInput` (convertido para o espaço do mundo com o yaw da câmara);
  - aplica o estado aos dois corpos visuais;
  - interpola entre o tick anterior e o atual para o render ficar suave.
- **Corpos visuais:** o Player atual e uma cópia para o remoto, com espada e a mesma montagem do `SwordSetup`. O `CapsulePlayer` e o `SwordWielder` ganham um modo "conduzido de fora" (§6), que desliga a lógica deles e só desenha pés, corpo e pose da espada a partir do estado.
- **Efeitos:** os eventos chamam os efeitos que já existem (`SwordSparks`, `HitStop` só visual, `Combat.Callout`, números de dano do `CombatOverlay`, `WaterRipples`, `FogField`).
- **Menu "Online" em inglês, construído no HUD como as outras janelas:**
  - `Host` (porta);
  - `Join` (IP:porta) e, mais tarde, `Steam Lobby`;
  - `Local Test` (loopback com lag simulado: 0/60/120/200 ms, jitter, perda);
  - indicador do estado da ligação.
- **Teste local:** duas sessões em loopback no mesmo jogo. O jogador 2 é controlado por um bot de inputs (padrões repetíveis: atacar, guardar no momento certo, rolar) ou por um segundo conjunto de teclas. Pode haver uma vista dividida ou alternável para ver os dois lados.

## 6. Ganchos mínimos nos ficheiros existentes

- **`CapsulePlayer`:** `externalDrive` (bool) mais um método para aplicar o estado (posição, yaw, movimento, tempos do roll/pique para as poses do corpo). Com ele ligado: sem input, sem `CharacterController.Move` e sem física; os pés e o corpo continuam a animar a partir do movimento.
- **`SwordWielder`:** `externalDrive` mais um método para aplicar o estado (estado, golpe, u do arco, estilo e guarda). Com ele ligado: sem lógica de golpe e sem varrimento; só a pose da arma e a arma do estilo (reaproveita `TargetPose`/`Arc`/`ShowWeapon`).
- **`Combatant`:** sem lógica com `externalDrive`. A vida, a postura e a inclinação vêm do estado, e a placa e o HUD continuam a ler daqui.
- Nada disto muda o modo de um jogador.

## 7. Fases (cada uma termina com algo para mostrar)

1. **Vírgula fixa e movimento:** andar, correr, salto, roll, salto longo e pique na simulação, com testes de determinismo. Mostrar: os testes a passar e uma gravação de dois corpos conduzidos pela simulação (bot de inputs).
2. **Combate na simulação:** os 5 estilos, guarda, parry, clash e guard break, com testes de regras. Mostrar: os testes e uma gravação do bot contra o bot.
3. **Rollback e loopback:** sessão completa no mesmo processo, com 120 ms de latência, jitter e 2% de perda, mais o checksum. Mostrar: a gravação e as estatísticas (rollbacks por segundo, zero desyncs em 3 minutos de bot contra bot).
4. **Apresentação e menu Online:** os visuais atuais conduzidos pelo estado, interpolação, eventos sem duplicados e menu "Online".
5. **UDP direto:** duas instâncias do build na mesma máquina e em dois PCs da mesma rede.
6. **Steam P2P:** lobby e convite, com relay.

## 8. Validação e ferramentas

- **Compilar:** com o Unity fechado, usar o Unity em batch. A gravação faz-se com `Assets/KonoSuba/Editor/VFXCapture.cs` (ver as opções no topo do ficheiro: `-captureHud`, `-captureBetween`, etc.); acrescentar opções para o modo de rede se for preciso.
- **Testes:** `-runTests -testPlatform EditMode`.
- **Determinismo:** nunca dar uma fase por acabada sem os testes de determinismo verdes e um teste longo de bot contra bot sem desync.
