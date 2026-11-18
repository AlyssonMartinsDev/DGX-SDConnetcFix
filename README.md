# DGX - SD Connect Fix

Aplicativo Windows Forms em C# e .NET 10 para aplicar a correção validada no XENTRY Diagnosis OpenShell 38.8.2.1. No caso diagnosticado, o XENTRY lia o VIN, mas retornava `eCOM_NO_CONNECTION (0xEA10)` ao abrir o diagnóstico. O inicializador copiava o perfil Bosch e o diagnóstico procurava uma interface `I+ME ACTIA GmbH`, declarada no perfil SDconnect.

## Senha de acesso

A janela principal é liberada após a validação da senha fixa, inclusive na primeira execução. O aplicativo incorpora um verificador PBKDF2 com salt aleatório. A elevação do Windows continua sendo solicitada antes da tela de senha.

## Uso

1. Feche o XENTRY, abra o DGX e desbloqueie o aplicativo.
2. Use **Install SD Connect Toolkit** para substituir as quatro DLLs em System32 e SysWOW64. O DGX guarda os originais em C:\Windows\DGX-Toolkit-Backup, executa o instalador incorporado 2.1.14.0 e aguarda a conclusão. Em seguida, chama TkServer.exe /regserver pelo CMD com a pasta INSTALLDIR\SDconnect Toolkit\bin como diretório de trabalho. O DGX descarta o backup após o instalador e o registro retornarem sucesso. Se uma dessas etapas falhar, tenta restaurar as quatro DLLs ao estado anterior e conserva as cópias para recuperação.
3. Use **Fix SD Connect Toolkit** para aplicar a correção. O destino é identificado automaticamente pelo INSTALLDIR do Registro. As referências PDU64 dos perfis seguem a pasta da instalação, inclusive em D:. As referências de ProgramData são preservadas. Depois, reabra o XENTRY e entre no diagnóstico.

Em XENTRY Cars, o ajuste usa o perfil SDconnect da própria instalação, valida a DLL ACTIA e preserva o perfil Bosch original em pdu_api_root_bosch.xml.bak. No Daimler Trucks 07/2026 instalado em C:\Program Files (x86)\Daimler-Truck, o DGX faz backup, sobrescreve cinco configurações, executa o instalador original do Link C4 e verifica os 166 arquivos esperados do PDU64. A comunicação com o veículo é confirmada no XENTRY.

## Progresso e registro técnico

A janela exibe a barra de progresso e mensagens genéricas. O registro técnico é gravado em %ProgramData%\DGX - SD Connect Fix\diagnostico.log. A pasta recebe o atributo Oculto no primeiro uso.

## Reversão

Para XENTRY Cars, feche o XENTRY e rode no CMD como administrador.

```cmd
cd /d "C:\Program Files (x86)\Mercedes-Benz\Xentry\bin\PDU64" && copy /y "pdu_api_root_bosch.xml.bak" "pdu_api_root_bosch.xml"
```

Reabra o XENTRY pelo atalho habitual. Se houver um backup e o perfil atual ainda precisar de ajuste, o DGX interrompe a cópia e preserva os arquivos existentes.

## Estrutura e compilação

- `DGX - SD Connect Fix.slnx` abre a solução no Visual Studio com suporte a .NET 10.
- `src/DGX - SD Connect Fix/MainForm.cs` implementa a interface.
- `src/DGX - SD Connect Fix/ProfileFix.cs` valida os perfis e aplica a substituição com backup.

Com o SDK do .NET 10, rode na raiz do projeto.

```powershell
dotnet build "DGX - SD Connect Fix.slnx" -c Release
dotnet publish "src/DGX - SD Connect Fix/DGX - SD Connect Fix.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -o dist/win-x64
```



## Coleta de diagnóstico em preparação

A pasta de código Collection contém a detecção de instalações e a coleta para diagnóstico. O detector consulta INSTALLDIR e DAI_APPTYPE na visão de 32 bits do Registro e verifica os diretórios padrão em C: e D:. A origem de ProgramData é C:\ProgramData\Mercedes-Benz. A coleta copia os arquivos existentes para uma pasta datada na Área de Trabalho e registra as origens, ausências e falhas em Resumo.txt.

Após autenticar, a tela inicial mostra a edição detectada e a versão DAI_VERSION. S_DTPID e SDPID correspondem a Daimler Trucks. XDOS corresponde a XENTRY Cars. A janela assume a instalação válida indicada pelo Registro. Sem um registro válido, aceita apenas um destino confirmado entre as pastas padrão. A coleta de diagnóstico continua disponível no código.

## Pacote offline do Link C4

O recurso Link-C4-07-2026.dgxp incorpora o instalador original, sua credencial, as cinco configurações coletadas e o manifesto dos 166 arquivos PDU64 observados. O DGX usa AES-256-GCM e deriva a chave da senha de acesso. O registro técnico preserva a linha do tempo sem exibir o argumento da senha. O instalador recebe a credencial pela linha de comando.

Antes do EXE, o DGX copia os originais para Xentry\bin\DGX-Backup\Link-C4-... e sobrescreve ReadFIN.cfg, GlobalSystemSettings_install.properties, XentryServices.xml, XD_DT\bin\CAL.ini e DAS\bin\cal.ini. Em seguida, executa o instalador silencioso. Após o código de saída 0, confere a presença dos 166 arquivos e o perfil Bosch com a entrada ACTIA. Em falha, tenta restaurar as configurações e os arquivos PDU64 rastreados. As chaves prévias do SmartSuite e os arquivos de função desconhecida em ProgramData ficam fora do fluxo do DGX.
