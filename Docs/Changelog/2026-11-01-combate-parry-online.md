# 2026-11-01 — Combate, aviso de parry e correções online

Resumo das alterações locais incluídas neste commit, confirmado pelo diff e pela leitura dos ficheiros novos. O objetivo é tornar o combate mais legível, manter a interface ligada ao jogador local e separar as regras determinísticas da apresentação do conteúdo online.

## Jogo e combate

- **Espada como referência comum:** os dois golpes leves e o pesado da Espada passam a definir os tempos, o dano e o stagger das restantes armas. Os multiplicadores são aplicados aos golpes correspondentes, mantendo empurrão, elevação, avanço e hit-stop próprios de cada arma.

  | Arma | Tempo relativo à Espada | Dano relativo à Espada |
  |---|---:|---:|
  | Espada / Knight | 1× | 1× |
  | Greatsword | 1,2× | 2× |
  | Reaper | 1,1× | 1,5× |
  | Fencer | 0,8× | 0,75× |
  | Adagas / Daggers | 0,6× | 0,5× |

- **Mais tempo para reagir:** os tempos de preparação, atividade e recuperação destes golpes recebem ainda o multiplicador global **1,3×**, partilhado pelo offline e pelo online. O pique mantém a sua definição separada.
- **Aviso visual de parry:** a arma de quem ataca ganha um brilho dourado e uma estrela de quatro pontas na extremidade. O aviso começa um tempo de reação antes da janela de parry (antecedência predefinida de 0,15 s), intensifica-se dentro da janela e apaga ao impacto. Lê o estado do `SwordWielder`, incluindo quando este é conduzido pela simulação online, e não aparece na arma do próprio observador.
- A estrela reutiliza a malha de losango das faíscas; o novo shader cria uma casca aditiva com contorno brilhante sobre a arma.

**Ficheiros principais:** `Assets/KonoSuba/Netcode/Sim/SwordData.cs`, `Assets/KonoSuba/Scripts/SwordWielder.cs`, `Assets/KonoSuba/Scripts/ParryGlint.cs`, `Assets/KonoSuba/Scripts/SwordSparks.cs`, `Assets/KonoSuba/Shaders/ParryGlint.shader` e os novos `.meta` do componente e do shader.

## Online e netcode

- **Lançamento e recarga por habilidade:** o input envia o `AbilityId` colocado no espaço premido da hotbar, em vez do número desse espaço. A configuração guarda as habilidades conhecidas e as recargas pertencem à habilidade. Assim, reorganizar a barra durante uma partida mantém as teclas e as recargas coerentes com o que se vê.
- **Base para novos conteúdos:** as regras de lançamento e atualização de projéteis são registadas por `AbilityId`. `INetAbilityPresenter` encaminha os eventos e a sincronização visual para cada habilidade; Dash e Bola de Fogo passam a apresentar os seus próprios efeitos. `INetPresentation` mantém ativos os componentes exclusivamente visuais, como o aviso de parry.
- **Tecla mantida premida:** `PlayerInput.Held` identifica a habilidade cuja tecla continua carregada, preparando o contrato de input para cargas e canalizações. Esta estrutura não porta, por si só, as restantes habilidades para online.
- **Bola de Fogo sem dano em aliados:** tanto a colisão em voo como a explosão passam a filtrar por hostilidade, evitando interceção, dano e Burning nos companheiros de equipa.
- **Transição do offline:** entrar numa partida cancela as habilidades offline em curso e limpa a contagem de canalizações. A Bola de Água destrói a canalização ativa, evitando que continue a gastar mana offline em segundo plano.
- **Pedidos durante hit-stop:** a habilidade pedida fica guardada em `BufferedCast` e é recuperada quando o corpo volta a processar input, em vez de se perder durante a paragem de impacto.
- **Elemento no feedback de dano:** os eventos passam a transportar o elemento; o hit de Bola de Fogo identifica Fire e o `Combatant` transmite-o ao sistema de números de dano.
- **Dois danos reais do pique:** a deduplicação inclui habilidade e marca adicional do contacto. O impacto no chão recebe uma marca distinta da lâmina, permitindo apresentar ambos os danos sem repetir eventos reapresentados pelo rollback.
- **Contratos atualizados:** input de 12 bytes, `StateCodec.Version = 8`, serialização das habilidades conhecidas e do pedido guardado, bots adaptados aos IDs e fingerprint das regras atualizado.

**Ficheiros principais:** `Assets/KonoSuba/Netcode/Sim/{Abilities,State,StateCodec,World,CombatRules,BotInputs,RulesFingerprint}.cs`, `Assets/KonoSuba/Netcode/Rollback/EventLedger.cs`, `Assets/KonoSuba/Netcode/Unity/{Views,NetMatch}.cs`, `Assets/KonoSuba/Scripts/CapsulePlayer.cs`, `Assets/KonoSuba/Scripts/Combatant.cs` e `Assets/KonoSuba/Scripts/Abilities/{Ability,AbilityBar,DashAbility,FireBallAbility,WaterBallAbility}.cs`.

## Interface

- **Ficha C, árvores V e hotbar do jogador local:** as referências passam a resolver o objeto `Player`, evitando selecionar a cópia `NetRemote` do adversário.
- **C e V durante partidas online:** as janelas continuam disponíveis. A partida mantém a ficha capturada no início; as alterações locais são reaplicadas ao terminar a partida e ficam disponíveis para a seguinte. A reorganização da hotbar usa o lançamento por ID descrito acima.
- **Números elementais em Plays sucessivos:** as referências estáticas de fontes e materiais são reiniciadas ao entrar em Play, permitindo recarregar recursos quando o domain reload está desativado.
- **Prioridade das teclas atribuídas:** F9 e F11 só executam as funções de diagnóstico quando não estão atribuídas a uma ação do jogo.

**Ficheiros principais:** `Assets/KonoSuba/Scripts/{GameHud,StatsWindow,SkillTreeWindow,GameInput,ElementDamageNumber}.cs` e `Assets/KonoSuba/Netcode/Unity/NetMatch.cs`.

## Documentação

- **`Docs/Online_Rules.md` novo:** define a separação entre simulação determinística, parâmetros do Inspector, apresentação e adaptadores Unity. Inclui receitas para habilidades, armas, IA e estados, além dos requisitos de input, snapshots, testes e fingerprint.
- **`Docs/MMO_Architecture.md` completado:** atualiza o inventário do núcleo existente e acrescenta serviços AWS, contas, persistência transacional, party, chat, operação, estimativas e comparação de custos, reaproveitamento ficheiro a ficheiro, fases demonstráveis, riscos, decisões pendentes e referências. É um plano: as capacidades, custos e fases futuras não são apresentados como implementação ou medições concluídas.
- Este changelog relaciona as alterações com os problemas descritos em `Logs/cap/terra_test_report.md` e identifica os limites da validação disponível.

**Ficheiros principais:** `Docs/Online_Rules.md`, `Docs/MMO_Architecture.md` e `Docs/Changelog/2026-11-01-combate-parry-online.md`. O relatório em `Logs/cap/terra_test_report.md` foi consultado como contexto; não consta das alterações pendentes do Git.

## Testes e estado de validação

- **Testes existentes adaptados:** `AbilityTests.cs` aprende e lança por `AbilityId`; `CombatTests.cs` calcula a vida esperada no guard break a partir do dano atual da Greatsword; `AiTests.cs` ajusta o limiar de ações defensivas de mais de três para mais de uma, mantendo as verificações de ataque, guarda e determinismo.
- **Evidência anterior às correções:** o relatório Terra regista oito problemas reproduzidos e o conflito F9/F11 identificado por leitura estática. Regista também 58 testes EditMode aprovados, zero falhados e dois Steam inicialmente ignorados, seguidos de 15/15 testes Steam aprovados com uma conta local. Estes resultados pertencem à ronda descrita no relatório e não validam o diff atual.
- **Verificação desta publicação:** leitura do diff, dos ficheiros novos e do relatório. `git diff --check` não encontrou erros nas alterações já seguidas; após incluir os ficheiros novos, `git diff --cached --check` assinalou apenas seis espaços finais nos campos vazios dos dois novos `.meta` do Unity, preservados tal como estavam. Não foram executados novos testes Unity, builds ou verificações visuais nesta tarefa de changelog e publicação.
- **Por validar após as correções:** repetir os cenários do relatório, observar o brilho de parry em execução e confirmar a integração entre clientes. O relatório anterior não prova dois PCs, duas contas Steam, conectividade real pela internet nem o aspeto visual dos efeitos.

**Ficheiros principais:** `Assets/KonoSuba/Netcode/Tests/{AbilityTests,AiTests,CombatTests}.cs`. Evidência histórica consultada: `Logs/cap/terra_test_report.md` e os resultados aí referenciados.
