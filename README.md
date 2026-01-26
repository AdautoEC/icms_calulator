# App Integrador CSV — Windows (.NET)

Aplicativo desktop para **importar 2× XML + 1× TXT**, **cruzar os dados**, calcular **distâncias (rota viária com fallback Haversine)** e **gerar uma planilha Excel (XLSX) consolidada** para uso do time de negócios.

---

## Sumário
- [Visão geral](#visão-geral)
- [Funcionalidades](#funcionalidades)
- [Arquitetura (visão rápida)](#arquitetura-visão-rápida)
- [Estrutura de pastas](#estrutura-de-pastas)
- [Requisitos](#requisitos)
- [Como executar](#como-executar)
- [Configuração](#configuração)
- [Uso](#uso)
- [Especificação da planilha de saída](#especificação-da-planilha-de-saída)
- [Qualidade e testes](#qualidade-e-testes)
- [Roadmap (opcional)](#roadmap-opcional)
- [Suporte e contato](#suporte-e-contato)
- [Licença](#licença)

---

## Visão geral
Este projeto entrega um **aplicativo desktop para Windows** que realiza ingestão de **dois formatos XML** e **um TXT**, aplica **regras de cruzamento**, calcula **distâncias por rota viária** (com fallback Haversine em caso de falha) e exporta um **arquivo Excel consolidado**.  
O foco é **simplicidade operacional** e **confiabilidade** para equipes de negócio que precisam de dados padronizados.

> **Observação:** a rota viária é obtida via OpenRouteService quando a chave estiver configurada; se a API falhar, a aplicação usa Haversine.

---

## Funcionalidades
- Importação de **2× XML** e **1× TXT**.
- **Validações essenciais** (campos obrigatórios, registros inválidos, encoding).
- **Cruzamento de dados** com base em chaves definidas a partir de amostras do cliente.
- **Rota viária (OpenRouteService)** com fallback **Haversine** local.
- **Exportação Excel (XLSX)** com cabeçalhos e formatação consistente.
- **Relatório de importação** (registros válidos/ignorados) para auditoria.
- **Editor de Rotas**: Permite o ajuste manual de rotas incorretas, com recálculo automático da distância.

---

## Arquitetura (visão rápida)
- **App (WPF/.NET 8)**: Interface desktop com fluxo simples (Selecionar arquivos → Processar → Exportar XLSX).  
- **Services**: Parsers (NFe/MDFe/SPED), merge, alocação de diesel, cálculo de rotas e geração de mapas.  
- **Models**: DTOs e modelo de dados exibido/exportado.  
- **Tools/SmokeRunner**: CLI para smoke test com uma pasta de arquivos.  
- **Installer**: Projeto WiX para gerar MSI.

---

## Estrutura de pastas
```text
/
  CsvIntegratorApp.sln
  CsvIntegratorApp.csproj
  *.xaml / *.xaml.cs          # Janelas WPF
  /Models                     # Modelos e DTOs
  /Services                   # Parsers, merge, rotas, exportação
  /Installer                  # WiX (.msi)
  /Tools/SmokeRunner          # CLI de smoke test
  modelo_para_exportar.xlsx   # Template do XLSX de saída
  README.md
```

---

## Requisitos
- **Windows 10/11**
- **.NET SDK 8.0**
- Visual Studio 2022 (ou VS Code + extensões C#)

---

## Como executar
```bash
# 1) Clonar o repositório
git clone https://github.com/<seu-usuario>/<seu-repo>.git
cd <seu-repo>

# 2) Restaurar e compilar
dotnet restore
dotnet build

# 3) Executar a aplicação (via IDE é o caminho mais simples)
# Abra a solução no Visual Studio e rode o projeto 'CsvIntegratorApp'
```

### Gerando um Executável para Distribuição

Para gerar um único arquivo `.exe` que pode ser distribuído para outras máquinas, use o seguinte comando:

```bash
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
```

- **`-c Release`**: Compila o projeto em modo de `Release`, otimizado para performance.
- **`-r win-x64`**: Especifica o runtime de destino como Windows 64-bit.
- **`--self-contained true`**: Inclui o .NET runtime no executável, para que ele possa ser executado em máquinas que não têm o .NET instalado.
- **`/p:PublishSingleFile=true`**: Agrupa todos os arquivos da aplicação em um único `.exe`.

### Criando um Instalador (.msi)

Para criar um instalador `.msi` para a aplicação, você pode usar o WiX Toolset. O projeto já está configurado com os arquivos necessários.

**1. Instale o WiX Toolset:**

Baixe e instale a versão mais recente do WiX Toolset em [https://wixtoolset.org/](https://wixtoolset.org/).

**2. Compile o instalador:**

Execute o seguinte comando para compilar a solução e gerar o instalador:

```bash
dotnet build -c Release
```

O instalador `.msi` será gerado na pasta `Installer/bin/Release/net8.0/`.

---

## Configuração
- **OpenRouteService (rotas):** defina `ORS_API_KEY` no ambiente **ou** crie `ors_api_key.txt` em `%LOCALAPPDATA%\CsvIntegratorApp` **ou** ao lado do executável.  
- **Template XLSX:** mantenha `modelo_para_exportar.xlsx` junto ao executável (o build copia automaticamente).  
- **Dados locais:** `vehicles.json`, `geocache.json` e `modelo.local.json` ficam em `%LOCALAPPDATA%\CsvIntegratorApp`.

---

## Uso
1. Abra o aplicativo.  
2. Selecione os **3 arquivos** (2× XML + 1× TXT).  
3. Execute o processamento; verifique o **resumo** (registros válidos/ignorados).  
4. Exporte a **planilha Excel consolidada** para a pasta desejada.  
5. Consulte o **relatório de importação** para auditoria.
6. Se uma rota estiver incorreta, clique no botão **Ajustar Rota** na linha correspondente para abrir o editor de rotas e ajustar os endereços.

---

## Especificação da planilha de saída
A especificação exata de colunas será definida nas amostras acordadas com o cliente. Exemplo ilustrativo:

| Coluna                   | Tipo     | Descrição                                    |
|--------------------------|----------|----------------------------------------------|
| `id_registro`            | string   | Identificador único do registro consolidado   |
| `data_evento`            | date     | Data/hora do evento                           |
| `origem_lat`             | decimal  | Latitude de origem                            |
| `origem_lon`             | decimal  | Longitude de origem                           |
| `destino_lat`            | decimal  | Latitude de destino                           |
| `destino_lon`            | decimal  | Longitude de destino                          |
| `distancia_haversine_km` | decimal  | Distância em km (linha reta)                  |
| `categoria`              | string   | (Se aplicável) categoria/agrupador            |
| `observacoes`            | string   | (Se aplicável) observações                     |

> Formatos de número e data serão normalizados conforme o modelo.

---

## Qualidade e testes
- Smoke test via `Tools/SmokeRunner` (processa uma pasta com TXT/XML).  
- Logs gravados em arquivo local para facilitar suporte e auditoria.

---

## Roadmap (opcional)
- Suporte a **outros provedores de rota** e cache avançado.  
- **Base online + API de consulta** (somente leitura).  
- Instalador **MSIX** com auto-update.  
- Dashboard analítico (gráficos, filtros avançados).

---

## Suporte e contato
Para suporte comercial/técnico:
- **E-mail:** adauto.pstech@gmail.com
- **Telefone/WhatsApp:** (67) 99237-0905

> Plano de suporte de 3 meses disponível mediante contratação adicional.

---

## Licença
**Proprietária** — PEREIRA E SOUZA TECNOLOGIA LTDA. O uso é restrito ao cliente e às condições acordadas contratualmente.
