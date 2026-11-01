# Regras para conteúdo online (habilidades, armas, inimigos, estados)

Tudo o que muda o resultado de um combate tem de correr **igual, bit a bit**, nos dois PCs (rollback) e, no futuro, no servidor (MMO). Estas regras dizem onde vive cada coisa e como se liga. Seguidas à risca, uma habilidade nova fica online sem retrabalho.

## 1. Onde vive cada coisa

| O quê | Onde | Porquê |
|---|---|---|
| Regras (dano, tempos, colisões, mana, recargas, estados, IA) | `Assets/KonoSuba/Netcode/Sim` (C# puro, sem Unity) | Determinístico; corre também no servidor (.NET) |
| Parâmetros afináveis (dano, velocidade, raio, custo…) | Campos do componente na cena (Inspector) | Fonte única: o offline lê-os e o online copia-os no início da partida |
| Aspeto (VFX, som, câmara, UI) | `Assets/KonoSuba/Scripts` | Só lê estado e eventos; nunca decide o resultado |
| Ligação entre os dois | `Netcode/Unity` (NetMatch, vistas `INet*`) | Converte estado → transformações, eventos → efeitos |

## 2. Regras do código de simulação (`Sim`)

1. **Números**: só `Fix` / `FixVec3`. Nada de `float`, `double`, `Mathf`, `Vector3`, `Time`, `Random`, `UnityEngine`.
2. **Aleatoriedade**: só o RNG guardado no estado (`MatchState.Rng`, ou um RNG próprio no estado do lutador, como a IA).
3. **Tempo**: só ticks de 60 Hz (`World.Dt`). Tempo global = `Tick × Dt`; tempo do corpo = `BodyTime` (pára no hit-stop).
4. **Estado**: só structs de tamanho fixo dentro do `MatchState` (lutadores P0..P7, projéteis Q0..Q15). Nada de listas, dicionários, classes ou referências. Tudo o que muda durante a partida entra no `StateCodec` (e a versão sobe).
5. **Ordem**: ciclos sempre por índice crescente; nunca iterar dicionários/hash sets.
6. **Constantes**: `static readonly` ou `Fix.Ratio`; nada calculado com `double` em runtime.
7. **Passagem**: `MatchState` e `PlayerState` passam sempre por `ref`/`in` (o Mono não passa structs grandes por valor).
8. **Eventos**: tudo o que o jogador deve ver (golpe, impacto, lançamento, estado aplicado…) sai como `SimEvent` com `(tick, tipo, origem, alvo)` estável. A apresentação deduplica por isso; o rollback pode refazer o evento.
9. **Zero alocações por tick** (o benchmark mede).

## 3. Receita: uma habilidade nova online

1. **Id**: acrescentar ao fim de `AbilityId` (`Sim/Abilities.cs`). Nunca reordenar (os valores viajam na rede).
2. **Parâmetros**: um bloco na `AbilityTable` com os campos que a regra usa (`Sim/Abilities.cs`), escritos no `StateCodec` e validados no `MatchHandshake.ValidateTable`.
3. **Captura**: o componente da habilidade (`Scripts/Abilities/XxxAbility.cs`) implementa `INetAbilitySource.CaptureNetAbility` e copia os seus campos para a tabela. Assim o Inspector continua a ser a fonte única.
4. **Regra**: uma regra em `Sim/Abilities` com `Cast` (gasto, validações, criação de projétil/área/efeito no lutador) e, se tiver vida própria, `Tick` (voo, colisões, impacto). Regista-se no `AbilityRules` (despacho por `AbilityId`).
5. **Input**: lançar usa a tecla da hotbar (`PlayerInput.Cast`); mirar usa `Aim`/`TargetSlot`; manter carregado usa `Held` (canalizações). Não inventar campos novos sem atualizar `PlayerInput.Read/Write/Equals/Predicted`.
6. **Apresentação**: o componente implementa `INetAbilityPresenter` (`Present` para eventos, `Sync`/`Clear` para projéteis). Reutiliza os VFX do offline com os mesmos parâmetros; o aspeto tem de ficar igual.
7. **Testes** (`Netcode/Tests`): regra (dano, mana, recarga, estado), determinismo (mesmos inputs → mesmo hash) e rollback em loopback com atraso/perda (zero desync, igual ao oráculo sem atraso).
8. **Impressão digital**: abrir o editor (ou correr os testes) atualiza `RulesFingerprint.cs`; fazer commit dele junto.

## 4. Receita: arma / estilo de espada

- Golpes em `Sim/SwordData.cs`: cada arma = golpes da Espada × (`TimeRatio`, `DamageRatio`) × `Slowdown`. O carácter (empurrão, salto, avanço, hit-stop) é por golpe.
- Arcos em `SwordGeometry.Arc` (o `SwordWielder` offline usa o mesmo arco).
- Feedback de contacto em `SwordWielder.Show*` (o mesmo offline e online).

## 5. Receita: inimigo / IA

- O cérebro vive em `Sim` (como `DuelistBrain`): lê o estado no início do tick e devolve um `PlayerInput`, com memória e RNG em `AiState`.
- Os parâmetros vêm do componente da cena (`INetBrainView.CaptureNetBrain`).
- O host junta os lutadores da IA ao estado inicial (`NetMatch.SceneFighters`).

## 6. Estados (Wet, Burning…)

- Duração e resistência em `World.ApplyStatus`; o efeito mecânico de cada estado (se tiver) entra nas regras do `Sim`, nunca na apresentação.
- A placa e os popups leem `StatusLeft/StatusDuration` e o evento `StatusApplied`.

## 7. Antes de fazer commit

- Testes EditMode verdes; benchmark sem alocações; rasto 1v1 igual quando a mudança não devia mexer no combate.
- Nada de lógica de jogo nos VFX; nada de VFX no `Sim`.
