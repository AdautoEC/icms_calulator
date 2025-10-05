
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

O projeto pode existir como **app desktop Windows** (e.g. WPF/WinUI/.NET) com pipeline local e/ou expor um **API** para consultas remotas.

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
- **`MdfeEvent`**: `seq`, `chNFe`, `timestamp`, `placa`, (opcional: origem/destino se constarem).
- **`Entrega`** (entidade correlacionada): `ordem`, `chNFe`, `participanteCodigo`, `endereco`, `lat`, `lon`, `data`, `kmAcumulado`.
- **`Coordinate`**: `lat`, `lon`.
- **`GeoResult`**: `input` (endereço), `lat`, `lon`, `precision`, `provider`.
- **`RouteStats`**: `totalKm`, `tempoEstimado`, `paradas`, `outliers`.

> **Dica**: No inventário automático (seção 10) liste classes e interfaces reais e alinhe com estes modelos.

---

## 5) Pipeline de Processamento

1. **Ingestão**: localizar arquivos `*.txt` (SPED), `*.xml` (MDFe/NFe).  
2. **Parsing**: converter cada linha/registro em objetos.  
3. **Correlação**: usar `chNFe` (MDFe) para encontrar `C100` e, dele, o `cod_part` para puxar o `0150`.  
4. **Geocodificação**: montar string de endereço canônica → `(lat, lon)`.  
5. **Cálculo de Distâncias**: aplicar Haversine entre entregas sequenciais.  
6. **Exportação**: salvar CSV por **entregas** e relatório consolidado (km, tempos, cobertura).  
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

- **entregas.csv**: `ordem, chNFe, cod_part, nome, logradouro, municipio, uf, cep, lat, lon, data, km_acumulado`  
- **rotas.csv**: `seq, lat1, lon1, lat2, lon2, km_segmento, chNFe_origem, chNFe_destino`  
- **erros.csv**: `arquivo, linha, registro, chave, motivo`

---

## 8) UI (Quando Aplicável)

- **Desktop (WPF/WinUI/.NET)**: seleção de arquivos, status de parsing, preview, filtros, botão **Exportar CSV**.  
- **Contadores/BI**: sumários (km total, nº entregas, outliers), gráficos de barras/linha e mapa (se disponível).

---

## 9) Testes & Validação

- **Fixtures**: pequenos trechos de SPED com `0150`/`C100` e MDFes com `chNFe` coerentes.  
- **Casos**: ausência de `0150`, endereços incompletos, CEP inválido, `chNFe` não encontrada, duplicidades.  
- **Smoke**: gerar CSV e conferir colunas mínimas/contagens.

---

## 10) Inventário Automático (preencha com o repositório real)

> Use um destes métodos **no seu ambiente** e anexe o resultado ao contexto do `gemini-cli`.
>
> **Windows (PowerShell):**
> ```powershell
> # 1) Árvore completa
> tree /F > repo_tree.txt
> 
> # 2) Lista de classes/nomes em .cs e .ts
> Get-ChildItem -Recurse -Include *.cs,*.ts | ForEach-Object {
>   $p = $_.FullName
>   $classes = (Select-String -Path $p -Pattern 'class\s+\w+|interface\s+\w+' -AllMatches).Matches.Value -join '; '
>   if ($classes) { "$p -> $classes" } else { "$p" }
> } | Out-File repo_symbols.txt -Encoding utf8
> ```
>
> **Linux/macOS (bash):**
> ```bash
> find . -type f -print > repo_tree.txt
> rg -n --no-heading -e 'class\s+\w+|interface\s+\w+' --glob '!**/bin/**' --glob '!**/obj/**' > repo_symbols.txt
> ```

Inclua ambos arquivos (`repo_tree.txt`, `repo_symbols.txt`) ao rodar o `gemini-cli` junto com este `gemini.md` para que o modelo **enxergue os Models/Services reais** do seu repositório.

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

---

## 13) Roadmap Sugerido

- [ ] Geocoding resiliente (rate limit, cache, retries).  
- [ ] Módulo de mapas para inspecionar rotas/outliers.  
- [ ] Exportar também **JSON** para integrações.  
- [ ] CLI para *batch processing*.  
- [ ] Estratégias de *data quality* e validação fiscal (regras ICMS específicas por UF).

---

> **Observação Final**  
> Se o repositório contiver outras camadas (ex.: API web ou integração com Builder Chat), inclua os inventários (seção 10) para o modelo ajustar este contexto automaticamente ao “projeto correto”.
