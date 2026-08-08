# HealthReport — Apple Health XML Analyzer con IA Local

## 1. Propósito

Aplicación de escritorio (WPF / C#) que permite al usuario subir el archivo ZIP exportado por la app **Salud de Apple** (`apple_health_export.zip`) y obtener un análisis completo de su salud generado por un **modelo de IA local** (Ollama). El análisis se produce en múltiples fases para adaptarse a modelos pequeños (3B–32B parámetros) con ventanas de contexto limitadas.

---

## 2. Estructura del proyecto

```
HealthReport/
├── src/
│   ├── HealthReport.Core/          # Lógica de negocio pura (sin dependencias UI/AI)
│   │   ├── Models/
│   │   │   ├── HealthRecord.cs          ✅
│   │   │   ├── WorkoutRecord.cs         ✅
│   │   │   ├── UserProfile.cs           ✅  (edad + IMC calculados)
│   │   │   ├── HealthSummary.cs         ✅  (MetricSummary incluido)
│   │   │   ├── AnalysisResult.cs        ✅
│   │   │   └── MetricTimeSeries.cs      ✅  (DailyDataPoint para gráficos)
│   │   ├── Parsing/
│   │   │   ├── IHealthParser.cs         ✅
│   │   │   └── AppleHealthXmlParser.cs  ✅  (streaming, multiformato fecha, DTD ignore)
│   │   ├── Aggregation/
│   │   │   ├── IHealthAggregator.cs     ✅
│   │   │   └── HealthAggregator.cs      ✅  (estadísticas + tendencia lineal + GetTimeSeries)
│   │   └── Export/
│   │       ├── IReportExporter.cs           ✅
│   │       ├── MarkdownReportExporter.cs    ✅
│   │       └── PdfReportExporter.cs         ✅  (PdfSharp 6 + Markdig)
│   │
│   ├── HealthReport.AI/
│   │   ├── Services/
│   │   │   ├── IOllamaClient.cs         ✅
│   │   │   └── OllamaClient.cs          ✅  (HTTP streaming ndjson)
│   │   └── Pipeline/
│   │       ├── AnalysisPhase.cs         ✅
│   │       ├── IAnalysisPipeline.cs     ✅
│   │       ├── AnalysisPipeline.cs      ✅
│   │       └── PromptBuilder.cs         ✅  (prompts <30KB por fase)
│   │
│   ├── HealthReport.UI/
│   │   ├── Views/
│   │   │   ├── ChartsView.xaml          ✅  (OxyPlot: pasos, FC, HRV, peso)
│   │   │   └── ChartsView.xaml.cs       ✅
│   │   ├── ViewModels/
│   │   │   ├── MainViewModel.cs         ✅  (pipeline + config + PDF + gráficos)
│   │   │   ├── PhaseViewModel.cs        ✅  (estado ⏳/⚙️/✅/❌)
│   │   │   └── ChartsViewModel.cs       ✅  (PlotModel por métrica)
│   │   ├── Services/
│   │   │   ├── AppConfig.cs             ✅  (JSON en %AppData%)
│   │   │   └── DialogService.cs         ✅  (Open/Save/SavePdf dialogs)
│   │   ├── MainWindow.xaml              ✅  (TabControl: Análisis + Gráficos)
│   │   └── MainWindow.xaml.cs           ✅  (auto-scroll)
│   │
│   └── HealthReport.Tests/
│       ├── Parsing/
│       │   └── AppleHealthXmlParserTests.cs   ✅  11 tests
│       ├── Aggregation/
│       │   └── HealthAggregatorTests.cs        ✅  8 tests
│       └── AI/
│           └── PromptBuilderTests.cs            ✅  9 tests (tamaño + contenido)
│
├── ARCHITECTURE.md
└── HealthReport.sln
```

---

## 3. Arquitectura general

```
ZIP upload
    │
    ▼
[AppleHealthXmlParser]  ──streaming XmlReader──►  SQLite en memoria / listas
    │
    ▼
[HealthAggregator]  ──►  HealthSummary (JSON compacto, <30 KB por fase)
    │
    ▼
[AnalysisPipeline]  ──► Fase 1 → Fase 2 → Fase 3 → Síntesis final
    │        cada fase: PromptBuilder + OllamaClient.StreamAsync
    │
    ▼
[MainViewModel]  ──►  UI (progreso + texto en tiempo real)
    │
    ▼
[MarkdownReportExporter]  ──►  informe.md / PDF
```

---

## 4. Pipeline de análisis por fases

La estrategia clave: **nunca enviar todos los datos a la vez**. Se envían resúmenes JSON estructurados de <30 KB por prompt.

### Fase 1 — Perfil demográfico y datos básicos
- Datos: fecha de nacimiento, sexo, altura, peso (últimos 90 días), IMC calculado.
- Objetivo: que el modelo establezca el contexto del usuario.
- Salida esperada: párrafo de perfil + lista de valores de referencia.

### Fase 2 — Actividad física
- Datos: media diaria de pasos, energía activa, distancia, minutos de ejercicio (últimos 90 días + tendencia).
- Objetivo: evaluar nivel de actividad, comparar con recomendaciones OMS.
- Salida esperada: evaluación + recomendaciones concretas.

### Fase 3 — Salud cardiovascular y sueño
- Datos: media y tendencia de FC en reposo, HRV, VO₂ máx, SpO₂, distribución de sueño (REM + profundo).
- Objetivo: detectar patrones de riesgo o mejora.
- Salida esperada: análisis cardiovascular + análisis de sueño.

### Fase 4 — Síntesis final
- Datos: los 3 resúmenes de fases anteriores (texto ya generado por el modelo, compacto).
- Objetivo: integrar todo en un informe cohesionado con prioridades de acción.
- Salida esperada: informe completo en Markdown.

---

## 5. Tecnologías y dependencias

| Componente | Tecnología |
|---|---|
| UI | WPF (.NET 9), CommunityToolkit.Mvvm |
| Parsing XML | System.Xml.XmlReader (streaming, sin cargar todo en RAM) |
| Almacenamiento temporal | Colecciones en memoria (listas/dicts); SQLite opcional para datasets grandes |
| Cliente IA | HTTP a Ollama REST API (`/api/generate` con `stream: true`) |
| Serialización | System.Text.Json |
| Exportación | Markdown nativo; PDF via MarkdownSharpLib (futuro) |

### Modelos Ollama soportados
- **3B**: Llama 3.2:3b, Phi3-mini, Qwen2.5:3b (ordenadores modestos)
- **8B**: Llama 3.1:8b, Qwen2.5:8b (ordenadores medios)
- **32B+**: Qwen2.5:32b, Llama 3.3:70b (workstations potentes)

La app detecta qué modelos tiene disponibles en Ollama y permite al usuario seleccionar.

---

## 6. Buenas prácticas de programación

### Principios SOLID aplicados
- **S** (Single Responsibility): cada clase tiene una sola responsabilidad. El parser no agrega, el agregador no envía prompts.
- **O** (Open/Closed): nuevas fases del pipeline se añaden sin modificar `AnalysisPipeline`; se registran como estrategias.
- **D** (Dependency Inversion): la UI y el pipeline dependen de interfaces (`IOllamaClient`, `IHealthParser`), no de implementaciones concretas.

### Patrones de diseño
- **MVVM** en la UI: `MainViewModel` expone propiedades observables; la vista no contiene lógica.
- **Pipeline / Chain of Responsibility**: cada fase de análisis es un eslabón que recibe el contexto acumulado.
- **Strategy**: `PromptBuilder` puede variar la estrategia de construcción de prompt según el modelo (tamaño de contexto).

### Rendimiento y memoria
- El XML de Apple Health puede superar 500 MB. Se usa **XmlReader en streaming** para nunca cargar el archivo completo.
- El parsing se hace en un `Task` con `CancellationToken` para no bloquear la UI.
- Los resúmenes JSON se limitan a **<30 KB por fase** (ajustable según `ModelContextSize`).

### Manejo de errores
- Cada fase del pipeline captura excepciones y puede reintentar o saltar la fase.
- La UI muestra el progreso fase a fase con posibilidad de cancelar.
- Los errores de Ollama (modelo no disponible, timeout) se comunican con mensajes claros al usuario.

### Cancellación y async
- Toda operación larga usa `async/await` con `CancellationToken`.
- El streaming de respuesta del modelo se muestra en tiempo real en la UI (TextBlock con binding).

---

## 7. Configuración de modelo recomendada

El usuario configura en la UI:
- **URL de Ollama** (default: `http://localhost:11434`)
- **Modelo a usar** (desplegable con modelos disponibles vía `/api/tags`)
- **Tamaño máximo de contexto por prompt** (default: 28000 tokens ≈ 20 KB JSON)
- **Días a analizar** (default: 90)

La app ajusta automáticamente el tamaño de los resúmenes JSON según la capacidad del modelo seleccionado.

---

## 8. Roadmap de fases de desarrollo

| Fase | Descripción | Estado |
|---|---|---|
| 0 | Estructura base del proyecto, ARCHITECTURE.md | ✅ Completo |
| 1 | Parser XML streaming robusto (multi-formato de fecha, DTD ignore, capacity hint) | ✅ Completo |
| 2 | Agregador de datos con tendencia lineal | ✅ Completo |
| 3 | Cliente Ollama con streaming (HTTP ndjson) | ✅ Completo |
| 4 | Pipeline de análisis 4 fases | ✅ Completo |
| 5 | UI completa con progreso en tiempo real | ✅ Completo |
| 5a | Panel lateral de fases con estado (⏳/⚙️/✅/❌) | ✅ Completo |
| 5b | Auto-scroll del área de texto | ✅ Completo |
| 5c | Persistencia de configuración (AppConfig → AppData JSON) | ✅ Completo |
| 5d | Diálogo para guardar el informe (SaveFileDialog) | ✅ Completo |
| 5e | Manejo robusto de errores por fase con detalle en UI | ✅ Completo |
| 5f | Soporte ZIPs grandes (+500MB) con archivo temporal | ✅ Completo |
| 5g | Pestañas: Análisis + Gráficos de tendencias | ✅ Completo |
| 6 | Exportación a Markdown | ✅ Completo |
| 6b | Exportación a PDF (PdfSharp 6 + Markdig) | ✅ Completo |
| 7 | Tests unitarios: Parser (11 tests), Agregador (8 tests), PromptBuilder (9 tests) | ✅ Completo — 31/31 ✅ |
| 8 | Gráficos de tendencias con OxyPlot (pasos, FC, HRV, peso) | ✅ Completo |
| 9 | Modelo `DailyDataPoint` + `MetricTimeSeries` + `GetTimeSeries()` | ✅ Completo |

---

## 9. Formato de export de Apple Health

### Archivo ZIP contiene
- `apple_health_export/export.xml` — archivo principal (puede superar 87 MB / 500 MB descomprimido)
- `apple_health_export/export_cda.xml` — registros CDA (secundario)
- Carpeta `electrocardiograms/` — archivos CSV de ECG (opcionales)
- `workout-routes/` — archivos GPX de rutas

### Estructura clave del export.xml

```xml
<HealthData locale="es_ES">
  <Me DateOfBirth="1983-03-06" HKCharacteristicTypeIdentifierBiologicalSex="HKBiologicalSexMale"
      HKCharacteristicTypeIdentifierBloodType="HKBloodTypeNotSet"
      HKCharacteristicTypeIdentifierFitzpatrickSkinType="HKFitzpatrickSkinTypeNotSet"
      HKCharacteristicTypeIdentifierCardioFitnessMedicationsUse="HKCategoryValueNotApplicable"
      HeightInMeters="1.75" WeightInKilograms="74.5"/>

  <!-- Registro tipo más común: Record -->
  <Record type="HKQuantityTypeIdentifierStepCount"
          sourceName="iPhone"
          unit="count"
          value="589"
          startDate="2026-08-07 10:23:11 +0200"
          endDate="2026-08-07 10:23:11 +0200"/>

  <!-- Workout -->
  <Workout workoutActivityType="HKWorkoutActivityTypeRunning"
           duration="43.99"
           durationUnit="min"
           startDate="2026-03-02 15:19:59 +0200"
           endDate="2026-03-02 16:03:54 +0200"/>
</HealthData>
```

### Tipos de Record más importantes

| HKQuantityTypeIdentifier | Descripción | Unidad |
|---|---|---|
| StepCount | Pasos | count |
| HeartRate | FC | count/min |
| ActiveEnergyBurned | Energía activa | kcal |
| BasalEnergyBurned | Energía basal | kcal |
| DistanceWalkingRunning | Distancia | km |
| RestingHeartRate | FC en reposo | count/min |
| HeartRateVariabilitySDNN | HRV | ms |
| VO2Max | VO₂ máx | ml/min·kg |
| OxygenSaturation | SpO₂ | % |
| RespiratoryRate | Frecuencia respiratoria | count/min |
| BodyMass | Peso | kg |
| Height | Altura | cm |
| SleepAnalysis | Sueño (categoría) | — |
| WalkingSpeed | Velocidad de marcha | km/h |
| WalkingStepLength | Longitud de paso | cm |
| WalkingAsymmetryPercentage | Asimetría | % |
| WalkingSteadiness | Estabilidad al caminar | % |
| AppleSleepingBreathingDisturbances | Disturbios respiratorios | events/hour |
