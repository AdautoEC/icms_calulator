# gemini.md — Contexto do Projeto “ICMS / SPED Integrator”
_Data: 2025-10-04 · Fuso: America/Sao_Paulo_

> **Objetivo deste arquivo**  
> Fornecer ao `gemini-cli` **todo o contexto** necessário para entender, manter e evoluir o projeto de **ingestão e correlação de dados fiscais (SPED/MDFe/NFe)** com **georreferenciamento** e **exportação para CSV/dashboards**.  
> Se o repositório contiver código em outras pastas além das descritas abaixo, rode o **Inventário Automático** (seção 10) e anexe o resultado como contexto adicional.

---

## 1) Visão Geral

Este projeto processa arquivos **SPED** (ex.: **EFD ICMS/IPI**) e documentos correlatos (**MDFe**, **NFe**) a partir de **TXT**/**XML**, cruza referências (ex.: **chNFe** do MDFe com **C100** do SPED), enriquece com **geocodificação** (lat/lon) e calcula **distâncias** (fórmula de **Haversine**), produzindo **CSV** e **dashboards**.  
Há requisitos explícitos do histórico:
- Não usar *municipio lookup* fixo; **derivar latitude/longitude** a partir do **endereço encontrado no TXT**.
- No SPED **EFD**, correlacionar **C100** (documentos fiscais) com **0150** (cadastro de participantes) usando o **código do participante**.
- A partir do **MDFe**, obter o **chNFe**, localizar no SPED a linha **C100** correspondente e, via **0150**, obter o **endereço** da entrega; o próximo **chNFe** refere-se à **próxima entrega** (sequência de eventos).
- Produzir **CSV** consolidados e dashboards com métricas de rotas, quilometragem, tempos e cobertura.

O projeto pode existir como **app desktop Windows** (e.g. WPF/WinUI/.NET) com pipeline local e/ou expor um **API** para consultas remotas. Recentemente, foi adicionada uma funcionalidade que permite aos usuários ajustar manualmente as rotas incorretas através de uma nova janela de edição.

---

## 2) Arquitetura Lógica

```
[Arquivos SPED/MDFe/NFe TXT|XML] ─┐
                                 ├─► Parser/Normalizador ─► Modelo Interno (Records/Entities)
[Catálogos/Mapeamentos auxiliares]┘
                                   ├─► Correlator (C100 ↔ 0150 ↔ MDFe chNFe)
                                   ├─► Geocoder (endereço → lat/lon)
                                   ├─► Distance Engine (Haversine / rotas)
                                   ├─► Validator (consistência/regra fiscal)
                                   ├─► Exporter (CSV / JSON / APIs)
                                   └─► Dashboards/UI
```

- **Parser**: lê SPED linha a linha, identifica **blocos/registros** e cria **objetos fortemente tipados**.
- **Correlator**: encadeia `C100` → `0150` → endereço; do **MDFe** extrai **chNFe** para amarrar a entrega.
- **Geocoder**: obtém **latitude/longitude** com base no endereço (ver seção 6.3 para estratégias).
- **Distance Engine**: calcula distâncias **ponto a ponto** (Haversine) e métricas de percurso.
- **Exporter**: gera **CSV** (e opcionalmente JSON) para BI e relatórios.
- **UI**: gerenciamento de arquivos, visualização, filtros, exportações, logs de processamento.

---

## 3) Principais Serviços (Services) — Convenções Sugeridas

- **`SpedParserService`**: detecta versão, bloco e registro; retorna uma coleção tipada (e.g., `Record0150`, `RecordC100`).
- **`MdfeParserService`**: extrai **chNFe**, remetente/destinatário, etapas e timestamps quando disponíveis.
- **`NfeParserService`** (opcional): para quando **XML NFe** está disponível (endereços precisos).
- **`CorrelationService`**: liga `chNFe` → `C100` → `0150`; define **entregas** ordenadas.
- **`GeocodingService`**: converte **logradouro/bairro/município/UF** em **(lat, lon)**; suporta múltiplos provedores.
- **`DistanceService`**: Haversine, somatórios, médias e detecção de outliers.
- **`ExportService`**: CSV/JSON com esquemas estáveis para BI.
- **`ValidationService`**: valida campos obrigatórios, data/numeração, formatos e inconsistências.
- **`StorageService`**: leitura/escrita local (e.g., pasta `data/inputs` e `data/outputs`).

> **Observação**: Caso o projeto já tenha nomes diferentes, preserve-os; adapte este contexto mapeando serviço existente → responsabilidade acima.

---

## 4) Principais Modelos (Models) — Baseline

- **`Record0150`** (Participante): `codigo`, `nome`, `logradouro`, `bairro`, `municipio`, `uf`, `cep`, `cnpjCpf`, `ie`.
- **`RecordC100`** (Documento Fiscal): `chNFe`, `cod_part` (link a 0150), `cod_mod`, `serie`, `num_doc`, `dt_doc`, `vl_doc`, etc.
- **`RecordC190`** (Registro Analítico): `CST`, `CFOP`, `ValorIcms`, `BaseIcms`, `TotalDocumento`.
- **`MdfeEvent`**: `seq`, `chNFe`, `timestamp`, `placa`, (opcional: origem/destino se constarem).
- **`Entrega`** (entidade correlacionada): `ordem`, `chNFe`, `participanteCodigo`, `endereco`, `lat`, `lon`, `data`, `kmAcumulado`.
- **`Coordinate`**: `lat`, `lon`.
- **`GeoResult`**: `input` (endereço), `lat`, `lon`, `precision`, `provider`.
- **`RouteStats`**: `totalKm`, `tempoEstimado`, `paradas`, `outliers`.
- **`VehicleInfo`**: `Placa`, `Renavam`, `Modelo`, `Tipo`.

> **Dica**: No inventário automático (seção 10) liste classes e interfaces reais e alinhe com estes modelos.

---

## 5) Pipeline de Processamento

1. **Ingestão**: localizar arquivos `*.txt` (SPED), `*.xml` (MDFe/NFe).  
2. **Parsing**: converter cada linha/registro em objetos. O parser do SPED agora também processa e armazena os registros `C190`.
3. **Correlação**: usar `chNFe` (MDFe) para encontrar `C100` e, dele, o `cod_part` para puxar o `0150`. Os dados do `C190` são associados ao `C100` correspondente.
4. **Geocodificação**: montar string de endereço canônica → `(lat, lon)`.  
5. **Cálculo de Distâncias**: aplicar Haversine entre entregas sequenciais.  
6. **Exportação**: salvar CSV por **entregas** e relatório consolidado (km, tempos, cobertura). Agora também exporta um relatório de conferência detalhado.
7. **Logs e Erros**: rastrear por arquivo e por registro (linha n°, chaves).

---

## 6) Pontos Críticos & Estratégias

### 6.1 SPED EFD (ICMS/IPI)
- Registros relevantes: **0150** (participantes), **C100** (documentos), **C170/C190** (itens/resumos), entre outros.  
- **Normalização** de CEP, UF e município (diacríticos, abreviações).

### 6.2 Correlação MDFe ↔ SPED
- O **MDFe** lista **chNFe** na sequência do percurso; cada **chNFe** mapeia para **C100** (SPED).  
- Via **cod_part** do C100, encontra-se o endereço em **0150** (entrega).  
- A **ordem** das entregas segue a ordem das **chNFe** no MDFe.

### 6.3 Geocodificação (sem “municipio fixo”)
- Estratégia multi-provedor:
  - **Primário**: geocodificador local/offline (se houver) ou API (Google, Nominatim/OSM, Here, etc.).
  - **Fallback**: heurísticas (CEP+UF), cache local por `hash(endereco)` para evitar chamadas repetidas.
- Armazenar `precision`/`confidence` e **log de fonte** para auditoria.

### 6.4 Distâncias (Haversine)
Fórmula (pseudo):
```
d = 2R * asin( sqrt( sin²((lat2-lat1)/2) + cos(lat1)*cos(lat2)*sin²((lon2-lon1)/2) ) )
```
- `R` ≈ 6371 km.
- Ajustar para resultados em km; tratar coordenadas inválidas (nulas/zero).

---

## 7) Esquema de CSVs (Sugerido)

Abaixo estão as estruturas sugeridas para os arquivos CSV gerados. Este formato de lista garante responsividade e clareza em qualquer dispositivo.

### `entregas.csv`
Detalha cada entrega como um evento individual na rota.
- **`ordem`**: Sequência numérica da entrega na rota.
- **`chNFe`**: Chave de acesso da Nota Fiscal Eletrônica.
- **`cod_part`**: Código do participante (destinatário) conforme registro 0150.
- **`nome`**: Nome do destinatário.
- **`logradouro`**: Endereço completo da entrega.
- **`municipio`**: Município da entrega.
- **`uf`**: Unidade Federativa da entrega.
- **`cep`**: Código de Endereçamento Postal.
- **`lat`**: Latitude do endereço de entrega.
- **`lon`**: Longitude do endereço de entrega.
- **`data`**: Data do documento fiscal associado.
- **`km_acumulado`**: Distância acumulada em quilômetros desde o início da rota.

### `rotas.csv`
Descreve os segmentos de viagem entre as entregas.
- **`seq`**: Sequência numérica do trecho da rota.
- **`lat1`**: Latitude do ponto de partida do trecho.
- **`lon1`**: Longitude do ponto de partida do trecho.
- **`lat2`**: Latitude do ponto de chegada do trecho.
- **`lon2`**: Longitude do ponto de chegada do trecho.
- **`km_segmento`**: Distância do trecho em quilômetros.
- **`chNFe_origem`**: Chave da NFe que marca o início do trecho.
- **`chNFe_destino`**: Chave da NFe que marca o fim do trecho.

### `erros.csv`
Registra quaisquer problemas encontrados durante o processamento.
- **`arquivo`**: Nome do arquivo de origem onde o erro foi detectado.
- **`linha`**: Número da linha no arquivo de origem.
- **`registro`**: Tipo de registro (ex: `C100`, `0150`) com erro.
- **`chave`**: Chave primária ou identificador do registro com erro.
- **`motivo`**: Descrição clara e concisa do erro.

### `conferencia.csv` (Novo Relatório)
- **`ChaveNFe`**: Chave de acesso da Nota Fiscal Eletrônica.
- **`CST`**: Código da Situação Tributária.
- **`CFOP`**: Código Fiscal de Operações e Prestações.
- **`ValorIcms`**: Valor do ICMS.
- **`BaseIcms`**: Base de cálculo do ICMS.
- **`TotalDocumento`**: Valor total do documento.
- **`Rua`**: Rua do destinatário.
- **`Numero`**: Número do endereço do destinatário.
- **`Bairro`**: Bairro do destinatário.
- **`UF`**: UF do destinatário.

---

## 8) UI (Quando Aplicável)

- **Desktop (WPF/WinUI/.NET)**: seleção de arquivos, status de parsing, preview, filtros, botão **Exportar CSV**. A tela principal agora exibe uma única linha com dados somados.
- **Contadores/BI**: sumários (km total, nº entregas, outliers), gráficos de barras/linha e mapa (se disponível). Os pop-ups do mapa agora mostram detalhes de cada registro C190.
- **RouteEditorWindow**: Uma nova janela que permite aos usuários visualizar e editar os endereços de uma rota. Ao salvar, a rota é recalculada automaticamente, e a distância e o mapa são atualizados.

---

## 9) Testes & Validação

- **Fixtures**: pequenos trechos de SPED com `0150`/`C100` e MDFes com `chNFe` coerentes.  
- **Casos**: ausência de `0150`, endereços incompletos, CEP inválido, `chNFe` não encontrada, duplicidades.  
- **Smoke**: gerar CSV e conferir colunas mínimas/contagens.

---

## 10) Inventário Automático (preencha com o repositório real)

> A árvore de arquivos a seguir foi gerada automaticamente.

```
C:\Users\User\Documents\icms\
├───.gitignore
├───12826990000109-284415260-20200801-20200831-1-C20D1596C276902D1A57E92C5C4FD2B9E282E81D-SPED-EFD.txt
├───AddVehicleWindow.xaml
├───AddVehicleWindow.xaml.cs
├───App.xaml
├───App.xaml.cs
├───CsvIntegratorApp.csproj
├───CsvIntegratorApp.sln
├───demonstrativo.xlsx - Demonstrativo.csv
├───exemplo.txt
├───GEMINI.md
├───ImportWizardWindow.xaml
├───ImportWizardWindow.xaml.cs
├───MDFe 50200812826990000109580010000004901000005892.xml
├───ModelEditorWindow.xaml
├───ModelEditorWindow.xaml.cs
├───modelo_para_exportar.xlsx
├───Modelo.xlsx
├───Modelo.xlsx - Nota de Aquisição Combustível.csv
├───NFe Combustivel 5020 0805 0801 1700 0146 5500 1000 0770 0010 0004 0417.xml
├───ors_api_key.txt
├───prompt_layout.txt
├───prompt_modelo_planilha.txt
├───README.md
├───RouteEditorWindow.xaml
├───RouteEditorWindow.xaml.cs
├───VehicleEditorWindow.xaml
├───VehicleEditorWindow.xaml.cs
├───.git\...
├───.idea\
│   └───caches\...
├───.vs\
│   ├───CsvIntegratorApp\...
│   ├───icms\...
│   └───ProjectEvaluation\...
├───bin\
│   └───Debug\...
├───Models\
│   ├───GeoPoint.cs
│   ├───ModelRow.cs
│   ├───VehicleInfo.cs
│   ├───WaypointInfo.cs
│   └───OpenRouteService\
│       ├───DirectionsRequest.cs
│       └───DirectionsResponse.cs
├───obj\
│   └───Debug\...
└───Services\
    ├───CalculationLogService.cs
    ├───DistanceService.cs
    ├───MergeService.cs
    ├───ModelService.cs
    ├───ParserMDFe.cs
    ├───ParserNFe.cs
    ├───RouteLogService.cs
    ├───SpedEfdTxtReader.cs
    ├───SpedLookupService.cs
    ├───SpedTxtLookupService.cs
    ├───VehicleService.cs
    ├───ApiClients\
    │   └───OpenRouteServiceClient.cs
    └───Route\
        └───MdfeRouteService.cs
```

---

## 11) Exemplos de Prompt (gemini-cli)

- “A partir deste `gemini.md` + `repo_symbols.txt`, gere o `CorrelationService` que vincula MDF-e `chNFe` → SPED `C100` → `0150` e produz `entregas.csv`.”  
- “Implemente `GeocodingService` com estratégia primária (Nominatim) e cache local, + fallback dummy para testes offline.”  
- “Escreva testes unitários cobrindo ausência de `0150` e CEP inválido; exporte CSV com colunas obrigatórias.”

---

## 12) Configuração & Execução Local

- **.NET SDK**: versão do projeto (ex.: `net8.0`).  
- **Ferramentas**: `dotnet-ef` para migrações (se houver DB).  
- **Variáveis** (exemplos):  
  ```
  GEOCODER__Provider=Nominatim
  GEOCODER__BaseUrl=...
  GEOCODER__ApiKey=...
  ```
- **Build/Run**: `dotnet build` → `dotnet run` (ou execute o app desktop).

### Gerando um Executável Único

Para distribuir a aplicação como um único arquivo `.exe`, use o seguinte comando:

```bash
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
```

- **`-c Release`**: Compila o projeto em modo de `Release`, otimizado para performance.
- **`-r win-x64`**: Especifica o runtime de destino como Windows 64-bit.
- **`--self-contained true`**: Inclui o .NET runtime no executável, para que ele possa ser executado em máquinas que não têm o .NET instalado.
- **`/p:PublishSingleFile=true`**: Agrupa todos os arquivos da aplicação em um único `.exe`.

### Criando um Instalador com WiX

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

## 13) Roadmap Sugerido

- [ ] Geocoding resiliente (rate limit, cache, retries).  
- [ ] Módulo de mapas para inspecionar rotas/outliers.  
- [ ] Exportar também **JSON** para integrações.  
- [ ] CLI para *batch processing*.  
- [ ] Estratégias de *data quality* e validação fiscal (regras ICMS específicas por UF).

---

## 14) Atualizações Recentes na Arquitetura de Roteamento

O sistema de geocodificação e cálculo de rotas foi significativamente refatorado para melhorar a robustez, manutenibilidade e performance, seguindo padrões de mercado.

### 14.1) Migração para OpenRouteService (ORS)
O provedor de dados geográficos foi migrado da combinação pública de Nominatim/OSRM para o **OpenRouteService (ORS)**. Isso oferece um serviço mais estável e integrado.

- **Autenticação**: A comunicação com o ORS exige uma **chave de API**. O sistema foi configurado para ler esta chave do arquivo `ors_api_key.txt`, localizado na raiz do projeto.

### 14.2) Cliente de API Desacoplado
Para seguir o princípio de responsabilidade única, foi criado um novo serviço, `Services/ApiClients/OpenRouteServiceClient.cs`. 

- **Responsabilidade**: Este cliente encapsula **toda** a lógica de comunicação com a API do ORS (geocodificação e direções), incluindo a montagem das requisições HTTP, tratamento de headers, serialização/deserialização de dados e logging detalhado de erros de API.
- **Modelos de Dados**: Foram criados modelos de dados fortemente tipados para as requisições e respostas da API (`Models/OpenRouteService/DirectionsRequest.cs` e `DirectionsResponse.cs`), eliminando a manipulação manual de strings JSON e tornando o código mais seguro.

### 14.3) Lógica de Roteamento Aprimorada
O `Services/DistanceService.cs` foi simplificado para atuar como um orquestrador, utilizando o novo `OpenRouteServiceClient`.

- **Chunking de Rotas Longas**: Para contornar o limite de 70 pontos por requisição da API do ORS, a lógica agora divide rotas longas em múltiplos "pedaços" (chunks), requisita cada um separadamente e agrega os resultados.
- **Error Handling de Chunks**: Se um trecho (chunk) da rota falhar, o sistema agora calcula a distância para aquele trecho usando o método de backup (Haversine, linha reta) e continua o processo, em vez de abortar o cálculo inteiro.
- **Cache Persistente**: A lógica de cache para geocodificação, que agora reside no `OpenRouteServiceClient`, salva os resultados em um arquivo `geocache.json`, melhorando a performance e reduzindo chamadas desnecessárias à API entre execuções do programa.

> **Observação Final**  
> Se o repositório contiver outras camadas (ex.: API web ou integração com Builder Chat), inclua os inventários (seção 10) para o modelo ajustar este contexto automaticamente ao “projeto correto”.