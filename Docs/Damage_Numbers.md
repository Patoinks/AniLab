# Números de dano elementais — registo de alterações

Registo do trabalho feito com o agente do Manuel (Claude Code), para o outro agente e quem trabalhe no projeto
estarem a par. Unity 6000.3.15f1, cena `Assets/Scenes/KonoSubaVoid.unity`. Atualizar este ficheiro sempre que
mudar alguma coisa nesta área.

**Estado:** os 7 elementos têm números de dano próprios, ligados ao combate real (`Combatant.AnyHit`). Testado
em Play: golpes reais no boneco geram o número certo de cada elemento, sem erros na consola.
**Por decidir:** a fonte. A atual (Cambria Bold Italic) ainda parece um pouco cartoon; há uma comparação de
12 fontes em `Captures/fonts_sheet.png` (pasta local, não está no repositório).

## O que existe agora

- **`Assets/KonoSuba/Scripts/ElementDamageNumber.cs`**: um número no mundo, uma quad virada para a câmara, com
  tamanho constante no ecrã. `ElementDamageNumber.Spawn(ponto, dano, crítico, elemento)` devolve `false` para
  elementos sem estilo (Neutral), e quem chama usa o número normal.
  - Fonte: SDF gerada em runtime a partir das fontes do Windows (`FontFiles`; usa a primeira que existir).
  - Crítico: `!` no fim, maior, treme o tempo todo e os algarismos saltam em onda.
  - Movimento por elemento: sobe por defeito; água sobe e cai; terra cai com peso e dá um baque ao aterrar;
    ar é levado para o lado.
  - Layer `UI`, para não aparecer no reflexo do chão (ver PlanarReflection abaixo).
- **`Assets/KonoSuba/Shaders/ElementDamageNumber.shader`**: letras nítidas com degradê à frente, o efeito do
  elemento atrás, halo escuro entre os dois. `_Style` escolhe o elemento:

  | Estilo | Elemento | Letras | Atrás | Fim |
  |---|---|---|---|---|
  | 0 | Fire | vermelho → dourado → amarelo | chamas a subir | arde de baixo para cima |
  | 1 | Water | azul → ciano → branco, brilho molhado | gotas a escorrer | escorre |
  | 2 | Lightning | violeta → ciano → branco, pisca | raios finos a estalar | apaga-se a piscar |
  | 3 | Air | verde-água → menta → branco, reflexo a correr | rajadas de lado | levado pelo vento |
  | 4 | Earth | castanho → ocre → areia, fendas | crosta de rocha, pó | desfaz-se em blocos |
  | 5 | Ice | azul → ciano → branco, facetas | espigões e pingentes, cintilações | estilhaça |
  | 6 | Dark | violeta escuro → lavanda, a pulsar | fumo escuro a subir | dissolve-se |

- **`CombatOverlay.OnHit`**: tenta `ElementDamageNumber.Spawn` primeiro; os outros elementos (Neutral, a
  espada) continuam com os números em UI Toolkit de antes. Usa o dano já mitigado e o `critical` do
  `HitInfo`, por isso funciona igual para golpes vindos do netcode (`PresentNetEvent`).

## Outras alterações

- **Kirin (tecla 4) passou a dar dano**: `Combat.Explode` no impacto do dragão, Lightning + `Static` (4 s),
  `damage = 90` × `SpellPower`, `blastRadius = 2.5`, stagger 50. Antes não criava `HitInfo` nenhum.
- **`PlanarReflection`**: a câmara do reflexo exclui sempre a layer `UI` (os números apareciam espelhados no chão).
- **Removido `DamageNumbers.cs`** e o dano inventado (110–140) que a Bola de Fogo tinha na primeira versão:
  duplicavam o `CombatOverlay` que veio com o merge do combate.

## Teste de dano (só desenvolvimento)

- **`Abilities/ElementTestAbility.cs`**: golpe instantâneo de um elemento no alvo selecionado (ou no inimigo
  mais próximo, até 30 m), dano 40–130, 25% crítico ×1.8, com o estado do elemento (`SkillTrees.Glyph`).
- **Menu `AnimLab/Teste de dano: habilidades na hotbar`** (só em Play, `Editor/DamageTestMenu.cs`): junta 7
  destas ao Player e põe-nas nas teclas 2–8 (fogo, água, eletricidade, ar, terra, gelo, trevas), com um
  círculo na cor do elemento. Não grava nada na cena.
- **`AbilityBar`**: `CanUse` aceita `ElementTestAbility` como aceita o Dash (sem árvores), e há
  `RegisterVisual(ability, ícone, glyph)` para habilidades juntadas depois do `Start`. Se as habilidades de
  teste deixarem de ser precisas, apagar estas duas coisas e os dois ficheiros acima.

## Pendente / a saber

- Fonte por escolher (ver acima).
- Só a Bola de Fogo, a Bola de Água e o Kirin dão dano. O Raio (tecla 3), a Bola de Vento e o resto não
  criam `HitInfo`, por isso não mostram números fora do teste.
- Terra, gelo e trevas ainda não têm habilidades; os estilos estão prontos para quando tiverem.
- Não está no repositório (é local do Manuel): `Packages/manifest.json` e `packages-lock.json` com o pacote
  `com.coplaydev.unity-mcp`, o `.mcp.json` e a pasta `Captures/`.

## Histórico

| Commit | O quê |
|---|---|
| `0da3ca9` | Primeiros números de dano (fogo), Bola de Fogo com dano inventado, PlanarReflection sem UI |
| `0b27b76` | Merge com o origin/main (combate, netcode, hotbar), sem conflitos |
| `b9eeb78` | Números ligados ao CombatOverlay (dano real); fogo, água, eletricidade; dano do Kirin |
| 82a94e5 | Ar, terra, gelo, trevas; habilidades de teste na hotbar; este documento |
