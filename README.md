# RevitSlopePlugin — Inclinação Automática para Esgoto Sanitário

Plugin profissional para **Autodesk Revit 2025** que automatiza a aplicação de inclinação (declividade) em redes hidráulicas de esgoto sanitário.

O projetista modela a rede sem inclinação, seleciona o **ponto fixo** (elemento terminal/jusante), define a **inclinação desejada** (%), e o plugin aplica automaticamente a declividade crescente em toda a rede conectada — mantendo o ponto base imóvel.

---

## 📁 Estrutura de Pastas

```
RevitSlopePlugin/
├── Commands/
│   └── ApplySlopeCommand.cs      # Comando principal (IExternalCommand)
├── Services/
│   ├── NetworkNavigationService.cs   # Percurso BFS pela rede MEP
│   ├── SlopeCalculationService.cs    # Cálculo de offset vertical
│   ├── GeometryService.cs           # Movimentação geométrica dos elementos
│   ├── BoxPipeSettingsService.cs    # Configuracoes do tubo entre caixas
│   └── ValidationService.cs         # Validações de elementos e rede
├── Models/
│   └── NetworkElement.cs            # Modelo de dados para elementos da rede
├── UI/
│   ├── SlopeInputWindow.xaml        # Interface WPF (XAML)
│   ├── BoxPipeSettingsWindow.xaml   # Configurar tubo entre caixas
│   └── SlopeInputWindow.xaml.cs     # Code-behind da janela WPF
├── Utils/
│   ├── Logger.cs                    # Logging básico (arquivo + TaskDialog)
│   └── UnitUtils.cs                 # Conversão de unidades (ft ↔ m)
├── RevitSlopePlugin.csproj          # Projeto .NET 8
├── RevitSlopePlugin.sln             # Solução Visual Studio
├── RevitSlopePlugin.addin           # Manifesto para o Revit carregar o plugin
└── README.md                        # Este arquivo
```

---

## ⚙️ Requisitos

| Requisito | Versão |
|-----------|--------|
| Autodesk Revit | **2025** |
| .NET SDK | **8.0** ou superior |
| Visual Studio | **2022** (17.8+) com workload ".NET desktop development" |
| Windows | **10/11** (64-bit) |

---

## 📚 Referências da API Revit 2025

O projeto referencia as seguintes DLLs da API do Revit (não distribuídas com o plugin):

| DLL | Caminho Padrão |
|-----|----------------|
| `RevitAPI.dll` | `C:\Program Files\Autodesk\Revit 2025\RevitAPI.dll` |
| `RevitAPIUI.dll` | `C:\Program Files\Autodesk\Revit 2025\RevitAPIUI.dll` |

> **Importante:** Essas DLLs devem ter `Copy Local = false` (já configurado no `.csproj`), pois o Revit as carrega em memória.

Se o Revit estiver instalado em outro local, ajuste os `<HintPath>` no arquivo `RevitSlopePlugin.csproj`.

---

## 🔨 Como Compilar

### Via Visual Studio

1. Abra `RevitSlopePlugin.sln` no **Visual Studio 2022**
2. Verifique se as referências `RevitAPI` e `RevitAPIUI` estão resolvidas
   - Se não estiverem, clique com botão direito em "References" → "Add Reference" → Browse
   - Navegue até `C:\Program Files\Autodesk\Revit 2025\`
   - Selecione `RevitAPI.dll` e `RevitAPIUI.dll`
3. Selecione a configuração **Release** (ou Debug para desenvolvimento)
4. Menu **Build → Build Solution** (Ctrl+Shift+B)
5. A DLL será gerada em `bin\Release\RevitSlopePlugin.dll`

### Via Linha de Comando (.NET CLI)

```bash
# Na raiz do projeto
dotnet build -c Release
```

---

## Instalação em outros computadores

O arquivo `.addin` precisa apontar para o caminho real da DLL no computador onde o Revit vai rodar. Se ele for copiado com um caminho absoluto da sua máquina, por exemplo `C:\Teste Revit\...`, o Revit vai exibir o erro "Add-In Assembly Not Found".

Depois de compilar, copie a pasta do projeto ou a pasta com os arquivos gerados para o outro computador e execute:

```powershell
.\Scripts\Install-OfficeHidraulica.ps1 -RevitYear 2025 -SourcePath .\bin\Release
```

Se estiver na raiz do projeto e ja tiver compilado em `bin\Release`, pode executar sem `-SourcePath`:

```powershell
.\Scripts\Install-OfficeHidraulica.ps1 -RevitYear 2025
```

Sem `-SourcePath`, o instalador procura primeiro `bin\Release` e usa `bin\OfficeHidraulica` apenas como fallback.

Por padrão a instalação é feita só para o usuário atual:

- DLLs: `%LOCALAPPDATA%\OfficeHidraulica\RevitSlopePlugin\2025`
- Manifesto `.addin`: `%APPDATA%\Autodesk\Revit\Addins\2025`

Para instalar para todos os usuários da máquina, abra o PowerShell como administrador e use:

```powershell
.\Scripts\Install-OfficeHidraulica.ps1 -RevitYear 2025 -AllUsers
```

---

## 📦 Instalação no Revit 2025

### Passo 1: Copiar a DLL

Copie a pasta de saída (ou apenas a DLL) para um local fixo, por exemplo:

```
C:\RevitPlugins\RevitSlopePlugin\RevitSlopePlugin.dll
```

### Passo 2: Registrar o Plugin

Copie o arquivo `RevitSlopePlugin.addin` para:

```
%ProgramData%\Autodesk\Revit\Addins\2025\
```

> Caminho completo típico: `C:\ProgramData\Autodesk\Revit\Addins\2025\`

### Passo 3: Ajustar Caminho da DLL

Abra o arquivo `.addin` copiado e ajuste a tag `<Assembly>` para apontar para o local real da DLL:

```xml
<Assembly>C:\RevitPlugins\RevitSlopePlugin\RevitSlopePlugin.dll</Assembly>
```

### Passo 4: Reiniciar o Revit

Feche e reabra o Revit 2025. O plugin aparecerá em:

**Add-Ins → External Tools → Aplicar Inclinação (Esgoto)**

---

## 🚀 Como Usar

### Aplicar inclinacao em rede existente

1. **Abra um projeto** com rede de esgoto modelada (Pipe System)
2. **Opcionalmente**, pré-selecione o elemento base (pipe, fitting ou acessório)
3. Acesse **Add-Ins → External Tools → Aplicar Inclinação (Esgoto)**
4. Se nenhum elemento estiver selecionado, o plugin pedirá para **clicar no elemento base**
   - O elemento base é o ponto fixo (jusante) que **não será movido**
   - Normalmente é o tubo ou conexão mais baixa da rede (saída para coletor)
5. O plugin perguntara se voce deseja selecionar outros pontos fixos de altura
6. Na janela que aparece, **digite a inclinação desejada** em porcentagem
   - Valores típicos para esgoto sanitário: **1% a 5%**
   - Valor padrão: **2%**
7. Clique em **✓ Aplicar**
8. O plugin:
   - Percorre a rede conectada mantendo o mesmo sistema MEP do elemento base
   - Calcula a cota pelos conectores, com cota diferente em cada ponta do pipe
   - Move os elementos no eixo Z (montante sobe)
   - Aplica o parâmetro Slope nos pipes
   - Desfaz a operacao se detectar conectividade quebrada apos a movimentacao
9. **Resultado**: Todos os elementos a montante ficam progressivamente mais altos
10. Use **Ctrl+Z** para desfazer se necessário

### Criar tubo entre caixas sem conector

1. Acesse **Office - Hidraulica → Configurações → Configurações**
2. Configure tipo de tubo, sistema, quantidade de tubos, diametro em mm, folgas, limites de Altura e nome do parametro de altura
3. Acesse **Tubo entre Caixas**
4. Selecione a caixa de origem
5. Selecione a caixa de destino
6. Informe a inclinacao desejada em porcentagem
7. Confira o preview com quantidade/diametro, cota de nascimento, cota de chegada e nova Altura da caixa destino
8. Confirme para ajustar a caixa destino e criar o tubo, ou os tubos paralelos configurados

O calculo usa a cota real do topo de cada caixa individualmente, via geometria da familia, e o parametro configurado de altura. A caixa de origem permanece fixa; apenas a caixa de destino tem o parametro Altura ajustado.

Quando a quantidade for maior que 1, o plugin cria tubos paralelos lado a lado, centralizados na linha entre as caixas. O afastamento automatico entre eixos usa o diametro configurado mais 5 cm.

Formula usada:

```text
Cota_nascimento = Topo_caixa1 - Altura_caixa1 + Folga_nascimento
Desnivel         = Distancia_horizontal * (Inclinacao / 100)
Cota_chegada    = Cota_nascimento - Desnivel
Nova_Altura_2   = Topo_caixa2 - (Cota_chegada - Folga_chegada)
```

Se o sentido configurado for **Destino para origem**, a chegada e calculada subindo em relacao ao ponto de nascimento.

### Criar tubos entre varias caixas

1. Acesse **Office - Hidraulica → Esgoto Sanitario → Tubos em Sequencia**
2. Selecione as caixas uma a uma na ordem do fluxo
3. Depois da segunda caixa, pressione ESC quando terminar a sequencia
4. Informe a inclinacao desejada
5. Confira o preview de todos os trechos
6. Confirme para ajustar as caixas de destino e criar todos os tubos consecutivos

Cada par consecutivo usa a mesma regra do comando **Tubo entre Caixas**. Em uma sequencia `Caixa 1 -> Caixa 2 -> Caixa 3`, a `Caixa 2` e ajustada pelo primeiro trecho e esse novo valor de Altura e usado como origem do segundo trecho.

---

## 🐛 Como Debugar no Visual Studio

### Configurar Debug

1. Abra as **propriedades do projeto** (botão direito no projeto → Properties)
2. Vá em **Debug → General → Open debug launch profiles UI**
3. Crie um novo perfil do tipo **Executable**
4. Configure:
   - **Executable**: `C:\Program Files\Autodesk\Revit 2025\Revit.exe`
   - **Working Directory**: `C:\Program Files\Autodesk\Revit 2025\`
5. Certifique-se de que o `.addin` aponta para a DLL de **Debug**:
   ```xml
   <Assembly>C:\...\RevitSlopePlugin\bin\Debug\RevitSlopePlugin.dll</Assembly>
   ```

### Executar Debug

1. Coloque breakpoints no código (ex: início do `Execute()` em `ApplySlopeCommand.cs`)
2. Pressione **F5** para iniciar o debug
3. O Revit 2025 abrirá automaticamente
4. Abra um projeto e execute o comando do plugin
5. O Visual Studio pausará nos breakpoints

### Logs

O plugin grava logs em:
```
%LocalAppData%\RevitSlopePlugin\RevitSlopePlugin.log
```

---

## 🏗️ Arquitetura e Design

### Padrão de Projeto

O plugin segue uma **arquitetura modular baseada em serviços**:

```
ApplySlopeCommand (Orquestrador)
    ├── ValidationService      → Validações
    ├── NetworkNavigationService → Leitura da rede (BFS)
    ├── SlopeCalculationService  → Cálculos
    ├── GeometryService          → Movimentação
    └── SlopeInputWindow         → Interface do usuário
```

### Algoritmo Principal

1. **BFS (Breadth-First Search)** percorre a rede via `ConnectorManager`
2. `HashSet<ElementId>` evita loops infinitos em redes com múltiplas conexões
3. O percurso fica restrito ao mesmo sistema MEP do elemento base quando os parametros de sistema existem
4. Distância horizontal acumulada é calculada por conector
5. Offset vertical = `distância × (inclinação / 100)`
6. Pipes recebem cota por extremidade; fittings e acessorios recebem uma cota de referencia
7. Trechos verticais (>80° com horizontal) são ignorados automaticamente
8. A transacao e revertida se a validacao final detectar conexoes quebradas

### Categorias MEP Aceitas

| Categoria | BuiltInCategory |
|-----------|----------------|
| Tubulações | `OST_PipeCurves` |
| Conexões | `OST_PipeFitting` |
| Acessórios | `OST_PipeAccessory` |

---

## 🔮 Evolução Futura

Funcionalidades planejadas para próximas versões:

- [ ] **Múltiplos pontos fixos** — Definir mais de um ponto de ancoragem
- [ ] **Inclinação por norma** — Calcular inclinação mínima pela NBR 8160 baseado no diâmetro
- [ ] **Diferentes sistemas** — Suportar água fria, água quente, pluvial
- [ ] **Detecção de interferências** — Verificar colisões com outros sistemas
- [ ] **Ribbon personalizado** — Botão com ícone na aba do Revit
- [ ] **Preview visual** — Mostrar preview da inclinação antes de aplicar
- [ ] **Relatório exportável** — Gerar relatório em Excel/PDF com os cálculos
- [ ] **Configurações salvas** — Lembrar última inclinação usada por projeto
- [ ] **Suporte a redes ramificadas** — Tratamento especial para derivações
- [ ] **Undo granular** — Desfazer por trecho em vez de toda a rede

---

## 📄 Licença

Este projeto é de uso livre para fins educacionais e profissionais.

---

## 🤝 Contribuindo

1. Fork o repositório
2. Crie uma branch para sua feature (`git checkout -b feature/minha-feature`)
3. Commit suas mudanças (`git commit -m 'Add: minha feature'`)
4. Push para a branch (`git push origin feature/minha-feature`)
5. Abra um Pull Request
