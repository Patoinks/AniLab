# 2026-11-01 — Ritmo de combate, stamina e ferramentas de teste

Resumo das alterações pendentes, confirmado pelo diff e pela leitura dos ficheiros novos. O objetivo é reduzir o spam de ataques, criar oportunidades de resposta e tornar a gestão de esforço relevante, com as mesmas regras no combate offline e na simulação online.

## Combate

- **Flinch ao acertar:** um golpe que entra interrompe o ataque do alvo e deixa-o cerca de **0,25 s sem agir**. O super armor da Greatsword aguenta enquanto a postura não partir; o pique já cravado no chão não é interrompido.
- **Stamina como recurso de combate:** golpes, roll, dash e pique passam a gastar stamina. A base é 100, com recuperação de 30 por segundo após 0,6 s sem gastar; a zero, a pausa inicial é de 1,2 s. Enquanto guarda, recupera a metade. Roll custa 20 e dash 15; o custo dos golpes depende do estilo, usando a média dos multiplicadores de tempo e dano da arma.
- **Ataques cansados:** se a stamina disponível não chega para pagar o golpe, ele continua a sair, mas demora **mais 30%**. A penalização fica associada ao golpe desde o início, evitando mudanças de ritmo a meio da animação.
- **Bloqueio e quebra de guarda:** bloquear consome stamina em vez de postura. Quando a stamina chega a zero, a guarda parte, criando uma resposta à defesa mantida sem pausa.
- **Compromisso ao falhar:** um golpe que não toca em ninguém cumpre a recuperação inteira, sem a cortar para guardar ou iniciar o golpe seguinte.
- **Ritmo dos combos:** depois do último golpe há uma pausa de **0,35 s** antes de recomeçar. O clique para encadear só conta a partir de metade da fase ativa, ou durante a recuperação; cliques anteriores perdem-se.
- **Parry menos punitivo:** o stun do atacante passa de **1,1 s para 0,6 s**.
- **Clash com timing:** nos golpes normais, só acontece quando as lâminas começam a cortar com uma diferença de **até 0,07 s**; o pique mantém a exceção temporal. O empurrão ganha velocidade e travagem próprias para produzir um encontrão seco, aproximadamente 1 m em 0,25 s. O corpo inclina-se para trás e os pés acompanham o recuo com passos mais curtos e rápidos.
- **Greatsword com identidade pesada:** recebe **0,6×** do desgaste de postura e da duração do flinch, além do super armor ao preparar e desferir. Ganha 0,22 m de alcance no braço e mais avanço nos três golpes.
- **Duelista adaptado:** tanto a IA offline como a determinística ajustam o comprimento dos combos à stamina, evitam continuar esgotadas, recuam para recuperar e respeitam a janela de encadeamento e a pausa final.

**Ficheiros principais:** `Assets/KonoSuba/Netcode/Sim/{SwordData,CombatRules,World,Abilities,DuelistBrain}.cs`, `Assets/KonoSuba/Scripts/{SwordWielder,Combatant,CapsulePlayer,DuelistAI}.cs` e `Assets/KonoSuba/Scripts/Abilities/DashAbility.cs`. As constantes de ritmo e os dados dos golpes ficam em `SwordData.cs`, também consumidos pelo offline.

## Online e netcode

- **Snapshots atualizados:** `StateCodec.Version` passa de 8 para **10**, incluindo stamina atual, parâmetros de recuperação, último gasto, limite do flinch e indicação de golpe cansado. Assim, estes estados acompanham os snapshots e o rollback.
- **Compatibilidade das regras:** a fingerprint foi atualizada para `0x8BF4B1D6C83392F7`. O handshake já compara a versão das regras e a fingerprint; passa também a validar os limites de stamina máxima e regeneração. Quem joga junto precisa da mesma versão do projeto.
- **Apresentação sincronizada:** os componentes Unity recebem da simulação a stamina, o estado de flinch, o golpe cansado e o knockback, para apresentar o mesmo resultado online.

**Ficheiros principais:** `Assets/KonoSuba/Netcode/Sim/{State,StateCodec,RulesFingerprint}.cs`, `Assets/KonoSuba/Netcode/Rollback/MatchHandshake.cs` e `Assets/KonoSuba/Scripts/{Combatant,SwordWielder,CapsulePlayer}.cs`.

## Interface

- **Barra de stamina no HUD:** uma barra fina sob a mana mostra o recurso do jogador local, offline ou online. Abaixo de 25% muda de cor para sinalizar o risco de quebra de guarda.
- **Consola de admin no F6:** permite `kill tornados`, `kill ai`, `kill all`, `kill <nome>` e `help`, com botões e modo de clique para matar. Preserva o jogador, a câmara, a interface e a rede. Online, a limpeza fica limitada aos efeitos locais de tornados; os lutadores pertencem à simulação. O atalho respeita ações já atribuídas a F6 e a consola participa no bloqueio de input do HUD.

**Ficheiros principais:** `Assets/KonoSuba/Scripts/GameHud.cs`, o novo `Assets/KonoSuba/Scripts/AdminConsole.cs` e o respetivo `.meta`.

## Efeitos visuais

- **Rasto de cleave:** um crescente aditivo acompanha o arco real da espada durante a fase ativa e desvanece depois. A malha é calculada pelo arco do golpe, reduzindo a dependência das posições entre frames.
- **Cores da mana:** usa o gradiente das árvores escolhidas quando disponível para o lutador local; sem essas cores, e nos lutadores remotos, usa aço claro. O componente integra a apresentação online e é adicionado pelo `SwordWielder`.

**Ficheiros principais:** os novos `Assets/KonoSuba/Scripts/CleaveTrail.cs` e `Assets/KonoSuba/Shaders/CleaveTrail.shader`, os respetivos `.meta` e `Assets/KonoSuba/Scripts/SwordWielder.cs`.

## Ferramentas

- **Capturas preservam o progresso:** `VFXCapture` guarda as PlayerPrefs da hotbar, árvores, pontos, habilidades aprendidas e estilo antes da preparação. No fim repõe os valores anteriores e remove apenas as chaves que originalmente não existiam, em vez de limpar o progresso do jogador.
- **Alvo local correto:** os cenários de árvores e estilo passam a resolver explicitamente o jogador local.
- **Admin automatizado por frame:** o novo argumento `-captureAdmin`, por exemplo `"30:kill ai|60:kill dummy"`, abre a consola e executa os comandos nos frames indicados, facilitando capturas reproduzíveis.

**Ficheiro principal:** `Assets/KonoSuba/Editor/VFXCapture.cs`.

## Testes e validação

- **Cobertura EditMode ampliada:** novos testes verificam flinch e interrupção, resistência e alcance da Greatsword, super armor, gasto e recuperação de stamina, lentidão dos golpes cansados, cliques antecipados e válidos, recuperação de golpes falhados, pausa final, timing de clash e quebra de guarda por stamina. As expectativas de parry, empurrão e input durante hit-stop também foram ajustadas.
- **Resultado existente confirmado:** `Logs/editmode_results.xml`, da execução de **2026-11-01, 10:09:59–10:10:10 UTC**, regista **68 aprovados, 0 falhados e 2 ignorados**, num total de 70. Os dois ignorados são testes Steam com a razão `Steam is not running`.
- **Âmbito desta publicação:** foram lidos o diff, os ficheiros novos e o resultado XML existente. Não foram repetidos testes Unity, builds, verificações visuais ou sessões entre clientes nesta tarefa de changelog e publicação; a contagem acima pertence à execução registada.
- **Verificação do conteúdo preparado:** `git diff --check` passou nas alterações já seguidas. `git diff --cached --check` assinalou apenas seis espaços finais nos campos vazios dos dois novos `.meta` de cleave, preservados como estavam. A comparação dos hashes dos 24 ficheiros originalmente pendentes confirmou que esta tarefa não alterou o seu conteúdo.

**Ficheiro principal:** `Assets/KonoSuba/Netcode/Tests/CombatTests.cs`. O XML em `Logs` é evidência local consultada e não faz parte das alterações pendentes do Git.

## Documentação

- **Regras online atualizadas:** documenta as constantes e a aplicação partilhada de flinch, stamina, Greatsword, clash, parry, recuperação de golpes falhados, pausa de combo e janela de encadeamento, para orientar futuras alterações sem divergir entre offline e simulação.
- Este changelog reúne as mudanças e as razões de cada tema, identifica os ficheiros principais e distingue o resultado de testes existente das verificações desta publicação.

**Ficheiros principais:** `Docs/Online_Rules.md` e `Docs/Changelog/2026-11-01-combate-ritmo-stamina.md`.
