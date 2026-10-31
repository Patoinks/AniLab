# Números de dano elementais — registo de alterações

Registo do trabalho feito com o agente do Manuel (Claude Code), para o outro agente e quem trabalhe no projeto
estarem a par. Unity 6000.3.15f1, cena `Assets/Scenes/KonoSubaVoid.unity`. Atualizar este ficheiro sempre que
mudar alguma coisa nesta área.

**Estado:** os 7 elementos têm números de dano próprios, ligados ao combate real (`Combatant.AnyHit`). O estilo
padrão é a **variante 9**: algarismos a pincel (fonte do projeto) em creme com borda laranja, brilho laranja suave
e duas marcas a pincel (`'` antes, `,` depois), réplica de uma referência que o Manuel escolheu. Testado em Play,
sem erros na consola.

## O que existe agora

- **`Assets/KonoSuba/Scripts/ElementDamageNumber.cs`**: um número no mundo, uma quad virada para a câmara, com
  tamanho constante no ecrã. `ElementDamageNumber.Spawn(ponto, dano, crítico, elemento)` devolve `false` para
  elementos sem estilo (Neutral), e quem chama usa o número normal.
  - **Fonte:** `Assets/KonoSuba/Resources/Fonts/AnimLab-Brush-Numeros.ttf` (só algarismos 0–9), carregada com
    `Resources.Load` e convertida em SDF em runtime. Sem ela, cai para Cambria/Palatino/Times do Windows.
  - **Duas versões da fonte:** SDF curto (padding 10, ~0.11 em; os efeitos dos elementos, variantes 0–8, estão
    afinados para isto) e SDF largo (padding 54, ~0.6 em; só a variante 9, cujo brilho vai até ~0.75 da altura).
  - `_SdfPerEm` vai no `MaterialPropertyBlock` de cada número, não no material: não está nas Properties do
    shader e um reimport do shader apagava-o do material.
  - **`Variant`** (estático) escolhe o aspeto; cada número guarda o que estava ativo quando nasceu.
  - **Animação** (variantes 7 e 9): aparece a 80%, pico aos 0.10 s (106%, crítico 112%), sobe e desaparece
    (0.75 s; crítico 0.9 s). Crítico maior (`CritPx` 52 vs `NormalPx` 38), a tremer e com os algarismos a saltar
    em onda. Sem `!` em nenhum número.
  - Layer `UI`, para não aparecer no reflexo do chão (ver PlanarReflection abaixo).
- **`Assets/KonoSuba/Shaders/ElementDamageNumber.shader`**: passe com `LightMode = "AnimLabOverlay"`, desenhado
  pelo `AnimLabRenderFeature` **depois do tonemap e do grading** (como o HUD): as cores saem exatamente como estão
  no shader, em linear 0..1. O tonemap da cena cortava os brancos em ~211 e as letras nunca chegavam ao creme.
  - `_Fx` (variante):

    | Fx | Aspeto |
    |---|---|
    | 0 | Efeito completo do elemento (tabela abaixo) |
    | 1–6 | Minimalistas: brilho, contorno + sombra, subtil, faíscas, risco de luz, plano + sombra |
    | 7 | "Metin": núcleo creme, borda na cor do elemento, brilho, riscos compridos dos lados |
    | 8 | Só a fonte, branco liso (para ver a forma dos algarismos) |
    | **9** | **Padrão.** Creme + borda laranja, brilho laranja (duas exponenciais medidas na referência), traço alisado e mais grosso que a fonte, marcas a pincel (`Lens`) em cima à esquerda e em baixo à direita. Laranja em todos os elementos, por agora. |

  - `_Style` (elemento), usado pelo efeito completo (Fx 0):

    | Estilo | Elemento | Letras | Atrás | Fim |
    |---|---|---|---|---|
    | 0 | Fire | vermelho → dourado → amarelo | chamas a subir | arde de baixo para cima |
    | 1 | Water | azul → ciano → branco, brilho molhado | gotas a escorrer | escorre |
    | 2 | Lightning | violeta → ciano → branco, pisca | raios finos a estalar | apaga-se a piscar |
    | 3 | Air | verde-água → menta → branco, reflexo a correr | rajadas de lado | levado pelo vento |
    | 4 | Earth | castanho → ocre → areia, fendas | crosta de rocha, pó | desfaz-se em blocos |
    | 5 | Ice | azul → ciano → branco, facetas | espigões e pingentes, cintilações | estilhaça |
    | 6 | Dark | violeta escuro → lavanda, a pulsar | fumo escuro a subir | dissolve-se |

- **`AnimLabRenderFeature`**: passe novo `OverlayPass` ("Sobreposição AnimLab"), o último, depois do tonemap e da
  composição do fogo. Desenha os renderers com o passe `AnimLabOverlay`. Sem esta feature no renderer, os números
  não aparecem.
- **`CombatOverlay.OnHit`**: tenta `ElementDamageNumber.Spawn` primeiro; os outros elementos (Neutral, a espada)
  continuam com os números em UI Toolkit de antes. Usa o dano já mitigado e o `critical` do `HitInfo`, por isso
  funciona igual para golpes vindos do netcode (`PresentNetEvent`).

## Outras alterações

- **Kirin (tecla 4) passou a dar dano**: `Combat.Explode` no impacto do dragão, Lightning + `Static` (4 s),
  `damage = 90` × `SpellPower`, `blastRadius = 2.5`, stagger 50. Antes não criava `HitInfo` nenhum.
- **`PlanarReflection`**: a câmara do reflexo exclui sempre a layer `UI` (os números apareciam espelhados no chão).
- **Removido `DamageNumbers.cs`** e o dano inventado (110–140) que a Bola de Fogo tinha na primeira versão:
  duplicavam o `CombatOverlay` que veio com o merge do combate.
- **Sem `!` nos críticos**, também nos números antigos do `CombatOverlay`.

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

- A variante 9 usa laranja em todos os elementos; falta decidir as cores por elemento neste estilo.
- As variantes 0–8 ficam no código para comparar; quando o estilo estiver fechado, apagar as que não se usam.
  Com o passe depois do tonemap, as cores delas (afinadas para antes do tonemap) ficam um pouco mais vivas.
- Só a Bola de Fogo, a Bola de Água e o Kirin dão dano. O Raio (tecla 3), a Bola de Vento e o resto não
  criam `HitInfo`, por isso não mostram números fora do teste.
- Terra, gelo e trevas ainda não têm habilidades; os estilos estão prontos para quando tiverem.
- Não está no repositório (é local do Manuel): a linha `com.coplaydev.unity-mcp` no `Packages/manifest.json`,
  o `.mcp.json` e a pasta `Captures/`.

## Histórico

| Commit | O quê |
|---|---|
| `0da3ca9` | Primeiros números de dano (fogo), Bola de Fogo com dano inventado, PlanarReflection sem UI |
| `0b27b76` | Merge com o origin/main (combate, netcode, hotbar), sem conflitos |
| `b9eeb78` | Números ligados ao CombatOverlay (dano real); fogo, água, eletricidade; dano do Kirin |
| `1321e31` | Ar, terra, gelo, trevas; habilidades de teste na hotbar; este documento |
| a seguir | Fonte a pincel, variantes 1–9 (9 padrão), passe depois do tonemap, críticos sem `!` |
