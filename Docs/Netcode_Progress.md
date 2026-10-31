# Netcode rollback — progresso

Projeto confirmado: `C:\Users\Martim\AnimLab`, Unity 6000.3.15f1. Implementação por fases do §7 de `Netcode_Rollback.md`. Sem commits; alterações anteriores preservadas.

**Estado atual:** fases 1–4 validadas. Fase 5 validada com sockets reais e dois processos Windows nesta máquina; falta o teste em dois PCs da LAN. Fase 6 implementada no âmbito simplificado pedido (Host + lista + Join por Steam); validações locais descritas abaixo. A ligação real entre duas contas em dois PCs continua por verificar.

**Validação atual:** **54/54 testes EditMode verdes, zero ignorados**, em `Logs/netcode_steam_tests_final.xml`; codec versão 3; checksum CoreCLR preservado `31001B6ADE1176F8`. Steam Networking Sockets nativo em loopback: 10 800 ticks, 360 checksums por lado, hash `3A984D838203671F`, zero desync. Build: `Builds/Netcode/AnimLab.exe`. Os resultados das fases anteriores são históricos e estão discriminados abaixo.

## Fase 1 — validada

- Leitura do plano, movimento, combate, stats, montagem das armas e captura concluída.
- Editor do projeto fechado no início. Validação prevista em batch, sem reconstruir nem guardar a cena existente.
- Núcleo novo sem Unity; ligação aos scripts existentes por interfaces de apresentação.
- Feitos: Q32.32 com intermediários inteiros largos, raiz inteira, tabelas canónicas com interpolação, vetores, input de 5 bytes, snapshots e checksum FNV-1a explícito. Tick de 60 Hz, movimento e colisão de cápsulas/arena, salto, roll, salto longo, homing/pique e land lag.
- Ganchos de apresentação em CapsulePlayer e Combatant; lógica original mantida quando externalDrive está desligado.
- Testes: **5/5 EditMode verdes**, incluindo 10 800 ticks (3 minutos simulados), igualdade em todos os ticks, restauro/ressimulação e round-trip de inputs/snapshot. Resultado: `Logs/netcode_phase1_tests.xml`.
- Gravação: `Logs/cap/netcode_phase1.mp4` (6 s, 180 frames, 1080p). Frames 75 e 150 inspecionados: dois corpos, salto longo, movimento e poses conduzidos pela simulação; diagnóstico no HUD.
- Repetir: Unity fechado, `-batchmode -projectPath C:/Users/Martim/AnimLab -runTests -testPlatform EditMode -testResults Logs/netcode_phase1_tests.xml`. Captura: método existente `AnimLab.KonoSuba.EditorTools.VFXCapture.Run`, `-captureBuild 0 -captureCall AnimLab.Netcode.Unity.NetcodeCapture.Movement -captureTarget Player -captureBetween NetRemote -captureDistance 12 -captureHeight 6 -captureLook 0.8 -captureFov 60 -captureFrames 180 -captureWarmup 10 -captureHud 1 -captureOut Logs/cap/netcode_phase1`.
- Limitações desta fase: sem dano/guarda nem rede; bots programados; a onda do pique é portada na fase 2. Cena existente não reconstruída nem guardada. O uso de BigInteger garante intermediários corretos; o custo será acompanhado nos testes longos.

## Fase 2 — validada

- Portados os cinco estilos (Iaido reservado), combos/fila, arcos e bases da lâmina, varrimento por subpassos, contacto segmento–cápsula e segmento–segmento, guarda frontal, parry/janela/lockout, clash, defesa, postura/stagger, knockback/atrito/queda, super armor, puxão do Reaper e Guard Breaker. Críticos usam xorshift64 guardado no snapshot. O pique inclui onda em área e redução até 40% na borda.
- Hit-stop congela os corpos e os seus temporizadores; o tick global avança. Eventos têm identidade estável por tick/tipo/jogador.
- SwordWielder ganhou apenas o modo de apresentação externa; usa as armas e poses atuais. Efeitos reaproveitados por Combatant.
- Testes: **21/21 verdes**, em `Logs/netcode_phase2_tests.xml`. Incluem parry na fronteira inclusiva e fora da janela, bloqueio de 80%, lockout, clash, roll, guard break, super armor, puxão e hit-stop.
- Teste adicional compara todas as constantes dos combos e amostras dos arcos com o SwordWielder atual por reflexão. Três minutos por estilo, sem divergências em qualquer tick: Knight 134, Fencer 356, Reaper 163, Daggers 381 e Greatsword 149 contactos reais.
- Gravação: `Logs/cap/netcode_phase2.mp4` (10 s, 300 frames). Greatsword × Daggers; frame 150 inspecionado, armas/poses e perda de vida presentes.
- Capturar: mesmos argumentos da fase 1, trocando por `-captureCall AnimLab.Netcode.Unity.NetcodeCapture.Combat -netStyle0 Greatsword -netStyle1 Daggers -captureDistance 6 -captureHeight 2.8 -captureFov 50 -captureFrames 300 -captureOut Logs/cap/netcode_phase2`. `-netStyle0/-netStyle1` aceitam Knight, Fencer, Reaper, Daggers e Greatsword.
- Limitações: ainda sem transporte/rollback; interações elementais e AI original excluídas conforme plano. Os testes longos usam vida elevada para manter os contactos durante 3 minutos; a gravação usa os valores normais. Aproximação determinística por tabela na curva de aceleração do arco (potência 2.2).

## Fase 3 — validada

- Feitos: sessões P2P simétricas, atraso local configurável (2 ticks), previsão sem flancos, buffer circular de 128 snapshots, correção/ressimulação na mesma frame, profundidade máxima 8 e stall. Pacing por vantagem de frames corrigida pela estimativa de uma via.
- Transporte loopback configurável; protocolo com 24 inputs redundantes, acks, ping por echo e checksums a cada 30 ticks confirmados. Acks dos checksums garantem repetição até confirmação. Pacotes malformados/fora dos limites são rejeitados.
- Divergências congelam a sessão; há pedido/resposta do estado remoto e dumps dos dois estados binários, hashes e inputs CSV em `Logs/netcode/`. O teste de desync provocado fica em `Logs/netcode/injected_test/`.
- Ledger impede duplicação de faíscas/números durante a ressimulação; vida/postura/poses são reaplicadas do estado corrigido.
- **30/30 testes EditMode verdes**, regressão completa, `Logs/netcode_phase3_tests.xml`.
- Teste 120 ms **de uma via**, jitter ±20 ms, 2% perda, 10 800 ticks: 481 datagramas perdidos; 633/773 rollbacks; 3594/4386 ticks ressimulados; máximo 8/8; 360/360 checksums verificados; **zero desync**; estado final igual ao oracle sem atraso. Relatório: `Logs/netcode/loopback_120_20_2.txt`.
- Stress adicional: 200 ms de uma via, ±45 ms e 10% perda passa com stalls e sem desync. Teste sem atraso também passa.
- Mesmo código compilado para CoreCLR/.NET 8 fora do Unity: checksum final **F1379DCC62B87815**, idêntico ao Mono do Unity, 360/360 verificações e zero desync. Repetir: `dotnet run --project Assets/KonoSuba/Netcode/Tools~/NetcodeVerifier.csproj -c Release`.
- Gravação: `Logs/cap/netcode_phase3.mp4` (10 s). Prova de 3 minutos no Unity: `Logs/cap/netcode_phase3_180s/f_0001.png`, 180.58 s, ticks 10833/10833, rollbacks 634/776, 360/360 checksums, 478 perdas, **zero desync**. Frame inspecionado.
- Captura curta: `-captureCall AnimLab.Netcode.Unity.NetcodeCapture.Rollback`; restantes argumentos como a fase 2. Soak: `-captureCall AnimLab.Netcode.Unity.NetcodeCapture.Soak -netStyle0 Reaper -netStyle1 Daggers -captureFrames 2 -captureWarmup 5410 -captureOut Logs/cap/netcode_phase3_180s`. Dois frames são necessários para o capturador existente compor o HUD.
- F11 alterna estatísticas; F9 alterna o peer observado; F10 continua reservado ao Settings. O MP4 curto mais antigo ainda mostra F10 no rótulo; a tecla atual é F9. A taxa de rollbacks/s foi ajustada para usar o tempo da sessão também em capturas aceleradas.
- Limitações: ainda sem menu/input humano ou sockets. O soak usa vida elevada para os bots continuarem o combate. Pacotes perdidos são contados exatamente no loopback; lacunas de sequência do protocolo incluem reordenação.

## Fase 4 — validada

- Menu Online no UIDocument existente, com Local Test, escolha de estilos/controlos, latência 0/60/120/200 ms de uma via, jitter, perda, atraso de input e interpolação. Host/Join preparados; ativação com o transporte da fase 5.
- Input humano lido pelo GameInput atual, com movimento em espaço do mundo, yaw de 10 bits e alvo selecionado num bit livre (o pacote continua com 5 bytes). Flancos ficam retidos entre frames de render até serem agendados, e são consumidos uma única vez.
- Modos You vs bot, Bot vs bot e Two keyboards. F8/botão Online abre a janela; F9 alterna o peer local; F11 alterna estatísticas. F10 mantém Settings e as teclas configuráveis.
- Se F8 já estiver associado a uma ação do jogo, essa ação mantém a tecla; nesse caso abre-se Online pelo botão do HUD.
- P1: teclas configuradas (por omissão WASD, L-Shift, LMB, F, Space, Q). P2: setas, J ataque, K guarda, R-Shift salto, R-Ctrl roll, Enter corrida.
- Os valores de movimento, salto/roll/pique, stats derivados e dimensões da cápsula são capturados no arranque e ficam constantes durante a partida. O raio da cápsula real é 0.35; a colisão agora considera também a separação vertical dos eixos das cápsulas. Codec de snapshot versão 2.
- Apresentação interpola posição, yaw, velocidade e tempos das poses. Pés congelam durante hit-stop; espada reaproveita TargetPose/Arc/ShowWeapon. Adversário selecionável com placa de vida. HUD lê a vida da simulação. Janela de stats/árvores fica fechada durante a partida.
- Reutilizados SwordSparks, HitStop/Then, Combat.Callout, CombatOverlay, WaterRipples, WaterSim e FogField. HitStop/slow-motion só afetam a apresentação: a sessão usa tempo não escalado. Ao sair, o efeito de tempo termina e o estado offline é restaurado.
- **34/34 testes verdes**, `Logs/netcode_phase4_tests.xml`. Inclui teste da cena em play mode: abertura do menu, início de sessão, visuais, checksums, troca de peer, restauro exato de posição/rotação/vida/estilo/flags enabled e retorno dos NPCs. Também verifica timeScale=1 e remoção da cópia remota.
- Testes longos e oracle continuam verdes. Checksum atual Mono/CoreCLR: **7519B9C526D03F9C**, 10 800 ticks, 360/360 verificações, zero desync, rollbacks 619/762. Os hashes anteriores das fases 1–3 são históricos dessas versões.
- Gravação final: `Logs/cap/netcode_phase4.mp4` (12 s). Menu no início, depois combate Knight × Reaper com efeitos/HUD. Frames 10 e 180 inspecionados; etiquetas do formulário corrigidas para contraste.
- Repetir gravação: argumentos da fase 2 com `-captureCall AnimLab.Netcode.Unity.NetcodeCapture.Online -netStyle0 Knight -netStyle1 Reaper -captureFrames 360 -captureOut Logs/cap/netcode_phase4`. Fecha automaticamente a janela após 150 ticks para mostrar o combate.
- Alterações existentes limitadas a CapsulePlayer, SwordWielder, Combatant e GameHud; nenhum outro ficheiro pré-existente foi editado por esta implementação. Alterações anteriores e cena preservadas; sem commits.
- Limitações: esta fase valida duas sessões no mesmo processo. Botões físicos não foram premidos manualmente; quantização/latch e o ciclo de input são cobertos por testes. Sem habilidades elementais/estados na simulação, conforme plano. O menu Host/Join depende da fase seguinte.

## Fase 5 — implementada, validação local concluída; LAN pendente

- UDP IPv4 não bloqueante, um adversário, handshake idempotente Hello/Welcome/Ready/Start com tokens de sessão, validação de parâmetros e estado inicial canónico. Cada jogador fornece os seus stats/estilo; ambos recebem o mesmo estado inicial/semente e o maior atraso de input proposto pelos dois.
- Mantém o mesmo protocolo de 24 inputs redundantes/acks/checksums. Keep-alive de 1 s, timeout de 10 s, saída explícita e rejeição de pacotes inválidos/de outra sessão. Lacunas de sequência são corrigidas quando um pacote reordenado chega; só a perda que não foi recuperada continua contada.
- Host/Join ativos no menu. Host abre a porta indicada e espera; Join recebe IPv4:porta. Leave cancela também uma ligação pendente. O modo direto usa os controlos GameInput de cada PC; a escolha de dois teclados/bots da janela destina-se ao Local Test.
- Execução em segundo plano durante a sessão, com restauro ao sair. Correções recebidas são reaplicadas na mesma frame de render mesmo quando não há novo tick.
- Corrigida a retenção de Attack/Jump/Roll durante hit-stop: os flancos ficam no snapshot e são tratados quando o corpo volta a avançar. Teste de combo específico acrescentado. Snapshot atual: **versão 3**.
- **39/39 testes verdes**, incluindo regras/paridade dos cinco estilos, três minutos por estilo, replay/oracle, 120 ms ±20 ms/2% perda, stress a 200 ms/10%, dumps dos dois estados, reordenação, sockets UDP e ciclo da cena/restauro offline. `Logs/netcode_final_tests.xml`.
- Loopback final: 10 800 ticks, 620/763 rollbacks, 3520/4327 ticks ressimulados, máximo 8/8, 360/360 checksums, 481 perdas, zero desync. Mesmo resultado no CoreCLR: **31001B6ADE1176F8**.
- UDP real EditMode: 10 800 ticks, 360/360 checksums, zero rejeições e zero desync, hash **3A984D838203671F**. `Logs/netcode/udp_editmode.txt`. A semente deste cenário é diferente do loopback.
- Build Windows Development gerado com sucesso (cerca de 168 MB de conteúdo). `Assets/KonoSuba/Netcode/Editor/NetcodeBuild.cs` conserva os shaders usados pelos efeitos em runtime num asset novo de Resources; nenhum asset de configuração existente foi alterado. Foi sincronizado novamente após proteger o atalho F8 contra colisões com teclas configuradas; essa alteração de UI também passou a regressão completa de 39 testes.
- **Dois processos reais do build, três minutos:** Reaper × Daggers, 10 800 ticks confirmados em ambos, 360 checksums em cada lado, hash final **86234819F43C69B2**, zero desync, zero perdas/stalls/rollbacks neste teste local. Ambos saíram com código 0. A latência local ficou dentro do atraso de 2 ticks; a recuperação com rollback é exercitada pelos testes de lag/perda acima.
- Relatórios: `Logs/netcode/build_host.txt` e `build_join.txt`; logs: `Logs/netcode_build_host.log` e `netcode_build_join.log`. O host pode registar Disconnected no encerramento porque o outro processo já terminou a sua verificação; ambos confirmaram o mesmo estado final.
- Captura final inspecionada: **`Logs/cap/netcode_phase5_180s.png`**, 1280×720, 181.5 s no overlay (180 s simulados mais espera de encerramento), dois corpos atuais, HUD e 360 verificações/zero desync.
- Aviso conhecido do projeto: carregamento/síntese da fonte Cambria no arranque; texto foi renderizado nas capturas. Sem erros de código/shader/netcode no build final.
- **Por verificar:** dois PCs físicos na mesma LAN, ligação pela internet/NAT e input físico manual em ambos. O teste nesta máquina usa os bots de inputs; não usa injeção de teclado/rato.

### Como usar UDP

1. Abrir o projeto ou `Builds/Netcode/AnimLab.exe`. Numa máquina, F8/Online → escolher Your style → porta 7777 → Host.
2. Na outra, abrir o mesmo build completo, escolher Your style e escrever o IPv4 do host seguido de `:7777` → Join.
3. Usar os controlos configurados no Settings. F11 mostra ping/rollback/checksums; Leave regressa ao modo offline. F9 só alterna vistas no Local Test.
4. Para outro PC, copiar a pasta **Builds/Netcode inteira**, incluindo AnimLab_Data, UnityPlayer.dll e MonoBleedingEdge. O executável isolado não chega. A permissão de rede/UDP do Windows tem de permitir a ligação na LAN; não foram alteradas regras de firewall nesta tarefa.

Reconstruir, com o Editor fechado:

```powershell
& "C:/Program Files/Unity/Hub/Editor/6000.3.15f1/Editor/Unity.exe" -batchmode -quit -projectPath C:/Users/Martim/AnimLab -executeMethod AnimLab.Netcode.Editor.NetcodeBuild.Run -logFile Logs/netcode_phase5_build.log
```

Teste automático em dois terminais, a partir da raiz do projeto:

```powershell
& .\Builds\Netcode\AnimLab.exe -batchmode -netHost 7779 -netBot 1 -netEndurance 1 -netStyle Reaper -netTicks 10800 -netReport Logs/netcode/build_host.txt -netPng Logs/cap/netcode_phase5_180s.png -logFile Logs/netcode_build_host.log
```

```powershell
& .\Builds\Netcode\AnimLab.exe -batchmode -netJoin 127.0.0.1:7779 -netBot 1 -netEndurance 1 -netStyle Daggers -netTicks 10800 -netReport Logs/netcode/build_join.txt -logFile Logs/netcode_build_join.log
```

Os argumentos `-netHost/-netJoin` ativam o banco de teste com saída automática após os ticks pedidos e confirmação dos checksums. `-netEndurance 1` aumenta a vida apenas nesse teste. O menu normal mantém os stats reais e a sessão aberta até Leave. Não usar `-nographics` para a captura PNG.

## Fase 6 — Steam, lista de lobbies e relay

### Como jogar, em casas diferentes

1. Cada jogador abre a Steam **na sua própria conta**, com sessão online, e abre a mesma versão do projeto no Unity 6000.3.15f1. Aguardar a importação dos pacotes no primeiro arranque.
2. Ambos carregam **Play → F8 / Online** e escolhem **Your style**.
3. Um carrega **Host** na secção **Steam Lobbies**. O seu nome Steam, estilo e estado aparecem na lista.
4. O outro espera pela atualização da lista (cada 4 segundos) ou carrega **Refresh**, e carrega **Join** no lobby do amigo. O combate começa depois do handshake.
5. **Leave** termina a sessão e regressa ao modo de um jogador. Sair do Play ou fechar o jogo também sai do lobby. Se o amigo sair, a sessão termina nos dois lados. **F11** mostra as estatísticas.

Não é preciso escrever IPs, escolher portas nem configurar o router. O host espera sem limite enquanto está sozinho. Se F8 estiver associado a outra ação, usar o botão **Online** do HUD.

O mesmo fluxo está no executável `Builds/Netcode/AnimLab.exe`. Copiar a pasta **Builds/Netcode inteira** para o outro PC; manter o `steam_appid.txt` e as DLLs fornecidas. Ambos usam a mesma versão. Não abrir Editor e executável em simultâneo na mesma conta para tentar simular dois jogadores.

Se a Steam não estiver aberta, o menu mostra **Steam is not running**. **Direct IP (UDP)** e **Local test & advanced settings** continuam disponíveis. O jogo de um jogador não inicializa a Steam; a inicialização é feita ao abrir Online.

### Implementação

- Escolhido **Steamworks.NET 2025.165.0**: expõe diretamente as APIs de lobby e Networking Sockets da Valve, inclui plugins nativos para Unity e pode ser instalado por UPM sem copiar um SDK manualmente. Dependência fixa em `Packages/manifest.json`; `packages-lock.json` fixa o commit `ea51b029a5dbcb47a99d3c1fac930b811672ed32`. O outro PC recebe a configuração pelo git. [Versão oficial](https://github.com/rlabrecque/Steamworks.NET/releases/tag/2025.165.0).
- App de teste **480 / Spacewar**, com `steam_appid.txt` na raiz para o Editor. `NetcodeBuild` copia-o para junto do executável e verifica os ficheiros Steam do build: `steam_api64.dll` e `com.rlabrecque.steamworks.net.dll` ao lado do `.exe`; o wrapper também fica no diretório Managed normal do Unity. O instalador oficial do pacote acrescenta automaticamente `STEAMWORKS_NET` aos símbolos Standalone em `ProjectSettings/ProjectSettings.asset`.
- Lobby público com exatamente **2 lugares**. Filtros no pedido à Steam e nova validação dos resultados e do Join: chave exclusiva `AnimLab.7c92810e6a1b4b949532e6e01e597dac`, protocolo `wire1.rules1.state3`, build `rollback-steam-6.1`, Steam ID do dono, nome do host, estilo válido e estado. A pesquisa usa distância mundial. Lobbies de outros jogos que usam 480 e versões incompatíveis não entram na lista.
- A lista mostra nome, estilo, estado e ocupação; atualiza a cada 4 segundos enquanto Online está aberto, com um único pedido pendente. O nosso lobby mantém uma linha identificada como **Your lobby**. A Steam omite dos resultados públicos os lobbies cheios ou fechados a entradas. [API de lobbies](https://partner.steamgames.com/doc/api/ISteamMatchmaking).
- `SteamTransport : ITransport` usa **Steam Networking Sockets / SDR**, com ICE desativado por opção da ligação (sem alterar preferências da conta). Os datagramas usam `UnreliableNoDelay` (sem Nagle, retransmissão ou espera por inputs). Só se aceita uma ligação de outro membro do lobby. [API de sockets](https://partner.steamgames.com/doc/api/ISteamNetworkingSockets).
- O handshake anterior foi extraído para `DatagramSession`, partilhado por UDP e Steam: Hello/Welcome/Ready/Start, tokens, parâmetros canónicos, keep-alive, timeout e saída. `MatchHandshake`, `RollbackPeer`, protocolo de inputs, checksums e simulação são reutilizados. O tempo de espera sem adversário é ilimitado apenas no host Steam.
- `SteamOnline` trata callbacks no thread principal e fecha sessão/lobby/API no Leave, saída do peer, fecho, Stop e recompilação do Editor. Há reinicialização por Play com domain reload desligado. Criações/entradas canceladas deixam também o lobby quando chega a resposta assíncrona. Não há migração automática de host.
- A versão de compatibilidade está em `SteamLobbyInfo.Build`: incrementá-la ao alterar simulação ou protocolo. Os dois jogadores devem receber os mesmos ficheiros. Convites Steam não fazem parte desta fase simplificada; a entrada faz-se pela lista.
- Só foram alterados ficheiros de netcode, a configuração dos pacotes, o símbolo Standalone acrescentado pelo pacote, este relatório e o novo `steam_appid.txt`. Cena, scripts do combate offline, sistema, firewall e conta Steam preservados. Sem commits.

### Validação local

- **54/54 EditMode verdes, zero falhas e zero ignorados**, com o Unity fechado antes do runner. `Logs/netcode_steam_tests_final.xml` e `Logs/netcode_steam_tests_final.log`. Inclui toda a regressão das fases 1–5 (determinismo, oracle, lag/perda, paridade dos estilos, UDP real e restauro offline) e 15 casos Steam.
- Teste nativo com `ISteamNetworkingSockets.CreateSocketPair`, **network loopback real**, sem segunda conta: usa `SteamTransport`, o handshake partilhado e `RollbackPeer`; 10 800 ticks, **360/360 checksums**, hash **`3A984D838203671F`**, zero desync e zero datagramas rejeitados. Flags de envio **5 / UnreliableNoDelay**. `Logs/netcode/steam_editmode.txt`.
- Editor, **dois Plays com domain reload desligado**: inicialização real, cancelamento de criação pendente, criação de lobby público, presença na resposta real da pesquisa, metadados, Leave, novo lobby deixado aberto ao Stop, shutdown e novo Play sem estado residual. `Logs/netcode/steam_editor_lifecycle.txt`. A regressão existente também entra em Play com a configuração normal.
- Repetido o verificador CoreCLR sem Unity: 10 800 ticks, hash preservado **`31001B6ADE1176F8`**, 360/360 checksums e zero desync. O núcleo da simulação não foi alterado nesta fase.
- Build Windows Development reconstruído por **NetcodeBuild**, resultado **Succeeded**, 169 359 982 bytes de conteúdo. `Logs/netcode_steam_build_final.log`. `steam_appid.txt` contém 480; DLL nativa ao lado do `.exe` idêntica por SHA-256 à do pacote; wrapper ao lado do `.exe` idêntico ao do diretório Managed do player.
- **Executável com a Steam real aberta:** inicialização App 480, socket pair nativo com os mesmos 10 800 ticks/360 checksums por lado/zero desync, criação do lobby público **Martin · Reaper · Waiting · 1/2**, presença no resultado bruto da pesquisa da Steam com todos os metadados, Leave, shutdown e nova inicialização, tudo concluído. A disponibilidade SDR reportou `k_ESteamNetworkingAvailability_Current`; isso não valida uma rota entre duas casas. `Logs/netcode/steam_smoke.txt` e `Logs/netcode_steam_smoke.log`, `success=True`.
- **Steam indisponível simulada**, sem fechar a Steam nem alterar a conta: mensagem exata **Steam is not running**, Local Test com 180 ticks e 5 checksums, sem desync, e retorno à posição offline original. `Logs/netcode/steam_unavailable.txt` e `Logs/netcode_steam_unavailable.log`, `success=True`. O caminho real com o cliente Steam encerrado não foi exercitado neste PC.
- **Capturas finais inspecionadas, 1280×720:** `Logs/cap/netcode_steam_lobbies.png` (lista com o lobby real e metadados visíveis) e `Logs/cap/netcode_steam_unavailable.png` (mensagem sem Steam). Corrigido o contraste das opções expansíveis de UDP/Local Test; a janela tem scroll para os formulários abertos. A captura usa render do próprio jogo; os testes chamam os métodos normais do menu, sem injeção de teclado/rato.
- **50 ficheiros de netcode com `.meta` verificados:** nenhum em falta, nenhum GUID inválido ou duplicado; `git diff --check` verde. Sem erros nem exceções de Steam/código nos logs runtime; permanece o aviso de síntese da fonte Cambria já documentado na fase 5.

Repetir, com o Editor fechado:

```powershell
& "C:/Program Files/Unity/Hub/Editor/6000.3.15f1/Editor/Unity.exe" -batchmode -projectPath C:/Users/Martim/AnimLab -runTests -testPlatform EditMode -testResults Logs/netcode_steam_tests_final.xml -logFile Logs/netcode_steam_tests_final.log
& "C:/Program Files/Unity/Hub/Editor/6000.3.15f1/Editor/Unity.exe" -batchmode -quit -projectPath C:/Users/Martim/AnimLab -executeMethod AnimLab.Netcode.Editor.NetcodeBuild.Run -logFile Logs/netcode_steam_build_final.log
```

Teste automático no executável, com uma conta Steam aberta (cria e deixa um lobby de teste):

```powershell
& .\Builds\Netcode\AnimLab.exe -batchmode -screen-width 1280 -screen-height 720 -netSteamSmoke 1 -netSteamPng Logs/cap/netcode_steam_lobbies.png -netSteamReport Logs/netcode/steam_smoke.txt -logFile Logs/netcode_steam_smoke.log
```

Para simular indisponibilidade da Steam sem fechar o cliente nem alterar a conta, acrescentar `-netSteamDisabled 1` e usar outros caminhos de relatório/captura. Esse teste verifica a mensagem, o Local Test e o restauro offline; a Steam real não é encerrada. Os argumentos de teste não são necessários no uso normal.

**Por verificar:** ligação real entre **duas contas Steam em dois PCs, em casas diferentes**, incluindo a rota SDR efetiva, input humano dos dois lados e saída remota real. Um socket pair local não prova conectividade pela internet. O teste em dois PCs da LAN da fase 5 também continua pendente.

## Ficheiros e âmbito

Criados em `Assets/KonoSuba/Netcode/`, com os respetivos .meta:

- `Sim/`: asmdef sem Unity, Fix, FixTables, FixVec3, State, StateCodec, World, SwordData, CombatRules e BotInputs.
- `Rollback/`: asmdef sem Unity, RollbackSession, RollbackPeer, MatchHandshake, LoopbackMatch, InputLatch e EventLedger.
- `Transport/`: asmdef sem Unity, ITransport, Loopback e UdpTransport.
- Fase 6: `Transport/DatagramSession`, `Steam/` (cliente/lobbies, catálogo e compatibilidade, transporte e verificador nativo), `Unity/SteamOnline`, `Unity/SteamSmokeRun`, `Editor/SteamEditorLifetime` e `Tests/SteamTests`, todos com `.meta`.
- `Unity/`: asmdef, Views/interfaces, InputSampling, NetMatch/NetcodeCapture, OnlineMenu, OnlineConnection/NetworkClock, NetBuildRun, ShaderReferences e link.xml.
- `Editor/`: asmdef e NetcodeBuild.
- `Tests/`: asmdef e MovementTests, CombatTests, RollbackTests, EventTests, PresentationTests e UdpTests.
- `Resources/NetcodeShaders.asset`: referências aos shaders dinâmicos do projeto.
- `Tools~/`: NetcodeVerifier.csproj, Program.cs e .gitignore; verificação independente com .NET 8, sem Unity e sem pacotes externos.
- `Docs/Netcode_Progress.md`: este relatório. O plano original foi apenas lido.

Ficheiros existentes alterados nesta tarefa:

- `Assets/KonoSuba/Scripts/CapsulePlayer.cs`: condução externa, input/configuração e restauro do estado.
- `Assets/KonoSuba/Scripts/SwordWielder.cs`: condução externa, poses/armas, configuração e restauro.
- `Assets/KonoSuba/Scripts/Combatant.cs`: estado externo, efeitos/HUD, configuração e restauro.
- `Assets/KonoSuba/Scripts/GameHud.cs`: menu Online, suspensão partilhada, vida e bloqueio de progressão durante a sessão.

O diff dos restantes ficheiros sujos mantém as contagens iniciais, incluindo SwordSetup, VFXCapture, CharacterStats, AbilityBar, DuelistAI, SkillTrees, SkillTreeWindow e KonoSubaVoid.unity. Não foram revertidos, reconstruídos ou guardados por esta implementação. `git diff --check` passa nos quatro scripts alterados; o check global encontra apenas espaços finais já existentes no diff da cena, que foi preservado.

Gravações por fase: `Logs/cap/netcode_phase1.mp4`, `netcode_phase2.mp4`, `netcode_phase3.mp4`, `netcode_phase4.mp4`; prova longa da fase 3 em `netcode_phase3_180s/f_0001.png`; prova do UDP em `netcode_phase5_180s.png`. Os artefactos antigos representam a versão da fase em que foram capturados. O menu atual também está em `Logs/cap/netcode_online_menu/f_0001.png`.

Limitações de âmbito mantidas: apenas dois jogadores, arena plana/circular, cinco estilos, sem habilidades elementais/AI original/estados na simulação. Efeitos transitórios já lançados permanecem; poses, vida e postura são corrigidas a partir do snapshot, e a ressimulação não volta a emitir os mesmos efeitos.

## Fase 7 — jogo todo online (Claude, em curso)

Feito e validado (EditMode 57/57; CoreCLR sem desync):

1. **Vírgula fixa sem BigInteger** (`Sim/Fix.cs`): 128 bits em `ulong`, bit a bit igual à versão anterior (3 pares de estilos × 3 min e 2 M operações aleatórias com o mesmo hash). Tick médio 125 → 6 µs, p99 980 → 59 µs, 116 KB → 0 B por tick.
2. **Golpes e feedback numa só fonte**: o `SwordWielder` lê os golpes de `SwordData` e os arcos de `SwordGeometry.Arc`; PARRY/BLOCK/CLASH/GUARD BREAK/golpe usam `SwordWielder.Show*` no offline e no online (`Combatant.PresentNetEvent`). Pose externa com as mesmas taxas e o mesmo estremecer.
3. **Impressão digital das regras** (`Sim/RulesFingerprint.cs`, gerado por `Editor/RulesFingerprintGenerator.cs` a partir de Sim/Rollback/Transport): entra no handshake ("Game version differs … git pull"), no filtro do lobby Steam e no rodapé do menu.
4. **Efeitos sem duplicados**: `EventLedger` deduplica o mesmo (tipo, origem, alvo) numa janela de ±8 ticks.
5. **Simulação para N lutadores**: `MatchState` com P0..P7 (`Count`), equipas (0 = hostil a todos), alvo por input (`TargetSlot`) ou o hostil mais próximo, `StruckMask` por alvo, codec v5. O 1v1 dá exatamente o mesmo rasto por tick (32 400 ticks comparados).
6. **IA na simulação**: `Sim/DuelistBrain.cs` (porte do `DuelistAI`, RNG e memória no estado), bonecos (`FighterKind.Dummy`), recarga de vida do `Combatant` (`Refill`), DEFEATED/renascer. O host junta a IA da cena ao estado inicial (handshake comprimido, `Rollback/Packing.cs`). Menu Online: "AI enemies" e "Co-op vs AI".

7. **Habilidades na simulação** (`Sim/Abilities.cs`): input com tecla da hotbar e ponto de mira (11 bytes), mana, recargas por espaço, dano mágico com resistências, estados com duração e resistência, projéteis Q0..Q15. **Dash** e **Bola de Fogo** portados (parâmetros lidos dos componentes da cena para a `AbilityTable`); VFX do offline conduzidos pelos eventos e pelas posições simuladas (`AbilityBar` como vista). O `MatchState` passa sempre por referência (`in`): o Mono não passa structs de ~12 KB por valor. Testes 60/60, incluindo bots a lançar com rollback 120 ms (123 lançamentos, 49 impactos, sem desync).

Por fazer: restantes habilidades (WaterBall, WindBall, Lightning, Thunder, Kirin, Summon), interações elementais, VFX das habilidades conduzidos pela simulação, rede para 3–4 jogadores (malha Steam), números de dano de golpes previstos que não aconteceram.
