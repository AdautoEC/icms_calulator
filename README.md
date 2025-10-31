# App Integrador CSV — Windows (.NET)

Aplicativo desktop para **importar 2× XML + 1× TXT**, **cruzar os dados**, calcular **distâncias (Haversine)** e **gerar um CSV consolidado** para uso do time de negócios.

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
- [Especificação do CSV de saída](#especificação-do-csv-de-saída)
- [Qualidade e testes](#qualidade-e-testes)
- [Roadmap (opcional)](#roadmap-opcional)
- [Suporte e contato](#suporte-e-contato)
- [Licença](#licença)

---

## Visão geral
Este projeto entrega um **aplicativo desktop para Windows** que realiza ingestão de **dois formatos XML** e **um TXT**, aplica **regras de cruzamento**, calcula **distâncias Haversine** (linha reta entre coordenadas) e exporta um **arquivo CSV consolidado**.  
O foco é **simplicidade operacional** e **confiabilidade** para equipes de negócio que precisam de dados padronizados.

> **Observação:** cálculo por **rota viária** (trajeto real) não está incluído no escopo essencial e pode ser oferecido como opcional com integração a serviços de rotas.

---

## Funcionalidades
- Importação de **2× XML** e **1× TXT**.
- **Validações essenciais** (campos obrigatórios, registros inválidos, encoding).
- **Cruzamento de dados** com base em chaves definidas a partir de amostras do cliente.
- **Distância Haversine** calculada localmente (sem dependência de API externa).
- **Exportação CSV** com cabeçalhos e formatação consistente.
- **Relatório de importação** (registros válidos/ignorados) para auditoria.
- **Editor de Rotas**: Permite o ajuste manual de rotas incorretas, com recálculo automático da distância.
- **Gerenciamento de Veículos**: Funcionalidade para adicionar, editar e excluir informações da frota de veículos.

---

## Arquitetura (visão rápida)
- **App (WPF/.NET 8)**: Interface desktop com fluxo simples (Selecionar arquivos → Processar → Exportar CSV).  
- **Core/Parsers**: Leitura e normalização dos arquivos XML/TXT.  
- **Core/Matching**: Regras de cruzamento (deduplicação, chaves, consistência).  
- **Core/Geo**: Cálculo Haversine.  
- **Core/Export**: Geração do CSV final.  
- **samples/**: Arquivos de exemplo **anonimizados** para testes.

---

## Estrutura de pastas
```text
/src
  /App                # Projeto WPF (.NET 8) — UI e fluxo do usuário
  /Core               # Domínio, serviços (parsers, matching, geo, export)
  /Core.Tests         # Testes unitários do domínio
/samples              # XML/TXT de exemplo (sanitizados)
docs/                 # Manuais, diagramas, notas
tools/                # Scripts auxiliares (opcional)
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
# Abra a solução no Visual Studio e rode o projeto 'App'
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
- **Segredos não vão para o Git.** Use `appsettings.Development.json` para valores padrão e `appsettings.Local.json` (no .gitignore) para overrides locais.  
- Se no futuro houver API/DB, mantenha **strings de conexão** apenas em arquivos locais e/ou variáveis de ambiente.

> Mantenha uma pasta **`/samples/`** com amostras **sanitizadas** (sem dados sensíveis) para testes e CI.

---

## Uso
1. Abra o aplicativo.  
2. Selecione os **3 arquivos** (2× XML + 1× TXT).  
3. Execute o processamento; verifique o **resumo** (registros válidos/ignorados).  
4. Exporte o **CSV consolidado** para a pasta desejada.  
5. Consulte o **relatório de importação** para auditoria.
6. Se uma rota estiver incorreta, clique no botão **Ajustar Rota** na linha correspondente para abrir o editor de rotas e ajustar os endereços.
7. Para gerenciar a frota de veículos (adicionar, editar, excluir), utilize a opção **Gerenciar Veículos**.

---

## Especificação do CSV de saída
A especificação exata de colunas será definida nas amostras acordadas com o cliente. Exemplo ilustrativo:

| Coluna                   | Tipo     | Descrição                                    |
|--------------------------|----------|----------------------------------------------|
| `id_registro`            | string   | Identificador único do registro consolidado   |
| `data_evento`            | date     | Data/hora do evento (formato `dd/MM/yyyy`)  |
| `origem_lat`             | decimal  | Latitude de origem                            |
| `origem_lon`             | decimal  | Longitude de origem                           |
| `destino_lat`            | decimal  | Latitude de destino                           |
| `destino_lon`            | decimal  | Longitude de destino                          |
| `distancia_haversine_km` | decimal  | Distância em km (linha reta, sem casas decimais) |
| `categoria`              | string   | (Se aplicável) categoria/agrupador            |
| `observacoes`            | string   | (Se aplicável) observações                     |
| `quantidade_litros`      | decimal  | Quantidade de litros (quatro casas decimais) |
| `valor_unitario`         | decimal  | Valor unitário (quatro casas decimais)       |
| `valor_total_combustivel`| decimal  | Valor total do combustível (duas casas decimais) |
| `valor_credito`          | decimal  | Valor do crédito (duas casas decimais)       |

> Separador padrão: `,` (ou `;` conforme regionalização). Formatos de número e data serão normalizados.

---

## Qualidade e testes
- Testes unitários em `Core.Tests` para parsers, matching e cálculo Haversine.  
- Logs gravados em arquivo local para facilitar suporte e auditoria.

---

## Roadmap (opcional)
- Integração com **API de rotas viárias** (trajeto real + cache).  
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
