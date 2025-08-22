# Metodología

## Descripción del entorno

El entorno experimental se diseñó con el objetivo de reproducir un escenario simple pero representativo de los problemas de navegación con recompensas escasas. Consta de **cuatro habitaciones principales**, dispuestas en torno a un eje central y conectadas mediante **pasillos estrechos en forma de cruz (+)**. Esta configuración obliga al agente a **atravesar cuellos de botella** para desplazarse entre habitaciones, lo que dificulta la exploración aleatoria y resalta la necesidad de guías estructuradas.

## Implementación de los objetivos (collectibles)

Los objetos recolectables actúan como fuente principal de recompensa. Se implementaron con un **tiempo de vida limitado**, decreciente hasta desaparecer, de modo que el agente debe aprender a priorizar su búsqueda. La visualización incorpora un indicador de tiempo (altura), lo que permite un feedback intuitivo en simulación.

Además, los collectibles pueden estar **asociados a un agente específico** o ser genéricos, lo que en trabajos futuros permitirá extender el análisis a escenarios competitivos multiagente. En este trabajo se emplearon collectibles genéricos (con menor valor) y específicos por agente (con mayor valor). El **respawn controlado** asegura un flujo dinámico de recompensas, evitando memorizar posiciones fijas.

## Dinámica temporal del entorno

El entorno está gobernado por **ciclos y subciclos** que determinan qué tipo de collectibles aparecen en cada fase. Esto introduce una variación temporal que simula cambios de disponibilidad de recursos, aumentando la complejidad del problema. En la configuración final se emplearon **cuatro subciclos**, alternando fases con abundancia y escasez de determinados objetos:
- En uno de ellos abundan los collectibles específicos del agente 1 (el agente estudiado).
- En el subciclo previo y en el posterior, estos collectibles aparecen ocasionalmente.
- En el subciclo opuesto, no aparece ninguno de los específicos del agente 1.
Por otro lado, los collectibles genéricos aparecen de forma homogénea durante todo el episodio, sin depender del subciclo.


## Acciones del agente

El agente dispone de un conjunto **discreto de acciones básicas**:

* **Rotación de 45º** (izquierda/derecha con limitación por cooldown).
* **Movimiento adelante/atrás** con velocidad fija.

Estas acciones simples se eligieron para que la dificultad emergiera del **entorno y las observaciones**, y no de la dinámica motriz.

## Observaciones y evolución de los tres enfoques

Uno de los principales objetivos de los experimentos fue **probar distintos diseños de observaciones** y comprobar cómo afectaban al aprendizaje. El proceso siguió un enfoque iterativo, simplificando progresivamente las entradas para reducir la carga cognitiva del agente.

### Enfoque 1 – Global absoluto

* **Observaciones:** coordenadas absolutas normalizadas del agente y de todos los objetivos, junto con su tiempo de expiración más información del subciclo actual:

    1. **Datos propios del agente:**
       - Cooldown de rotación (normalizado entre 0 y 1)
       - ID propio

    2. **Para cada agente en el área (incluyéndose a sí mismo):**
       - Posición X relativa al centro del área, normalizada
       - Posición Z relativa al centro del área, normalizada
       - Velocidad X, normalizada
       - Velocidad Z, normalizada
       - Rotación Y discretizada y normalizada
       - ID del agente

    3. **Para cada collectible en el área:**
       - Posición X relativa al centro del área, normalizada
       - Posición Z relativa al centro del área, normalizada
       - ID permitido para recoger
       - ¿Está activo? (1 o 0)
       - Tiempo restante de vida, normalizado

    4. **Datos del área:**
       - Índice de subciclo actual
       - Índice de subciclo normalizado


* **Recompensa:** únicamente por recoger un objetivo. 4 puntos por collectible asociado y 1 punto por collectible genérico.
* **Motivación:** establecer una línea base simple.
* **Limitación encontrada:** exceso de información global, sin estructura, lo que dificultó que el agente encontrara estrategias útiles y saliera de la habitación inicial.

### Enfoque 2 – Global con shaping

* **Observaciones:** similares que en el enfoque 1 pero un poco simplificadas. Se eliminó información de los collectibles que no se podían recoger y la información del subciclo actual:

    1. **Datos propios del agente:**
       - Cooldown de rotación (normalizado entre 0 y 1)
       - Posición X relativa al centro del área, normalizada
       - Posición Z relativa al centro del área, normalizada
       - Velocidad X, normalizada
       - Velocidad Z, normalizada
       - Orientación Y discretizada y normalizada

    2. **Agente activo más cercano:**
       - Posición X relativa al centro del área, normalizada
       - Posición Z relativa al centro del área, normalizada

    3. **Para cada collectible pickeable:**
       - Posición X relativa al centro del área, normalizada
       - Posición Z relativa al centro del área, normalizada
       - Tiempo de vida restante normalizado (remainingSeconds / maxLifetime)


* **Recompensas adicionales:**

  * Bonus por **pisar nuevas baldosas** dentro del episodio (incentivar exploración). A lo largo de un episodio el agente obtenía al menos de 1 punto por esta fuente.
  * Variación de un **potencial** definido como la distancia al objetivo más cercano (premiar al acercarse, castigar al alejarse).  lo largo de un episodio el agente obtenía cerca de 5 puntos por esta fuente.
* **Motivación:** guiar al agente hacia objetivos y fomentar la exploración estructurada de pasillos.
* **Limitaciones encontradas:**

  * El shaping era **exploiteable** debido a los cambios abruptos de spawn/despawn. El agente podía acercarse a una pared para ganar reward al acercarse a un collectible y esperar a que este desapareciera para no perder reward por alejarse.
  * Los bonus por baldosas resultaron poco informativos: con celdas grandes no distinguía pasillos, y con celdas pequeñas el agente caía en comportamientos circulares.

### Enfoque 3 – Local con grafo de señales

* **Observaciones:**

  * Datos locales en **coordenadas polares** apoyados por un sistema de **raycast**. Todas las observaciones se proporcionaron con histórico de profundidad 3 para facilitar la navegación.
  * Señales propagadas a través de un **grafo topológico**: cada collectible emite una señal que viaja por el grafo, y el agente recibe únicamente la señal más intensa visible emitida por cada collectible.
  
      1. **Observaciones propias compactas:**
         - Cooldown de rotación normalizado (0..1)
         - Orientación lateral (forward.x)
         - Orientación longitudinal (forward.z)
         - Velocidad relativa (módulo normalizado, seno y coseno respecto a la orientación)
         - 5 raycast en 45º que detectan tags y distancias

      2. **Observaciones de broadcasters externos:**
         Para cada broadcaster (collectable en el grafo) observado, se añaden 4 observaciones:
         - Distancia normalizada al último propagador de la señal propia más cercana
         - Seno del ángulo relativo
         - Coseno del ángulo relativo
         - Tiempo restante normalizado de la señal (expiryNorm)


* **Recompensas:**

  * Premio por **batir récords de aproximación** hacia cada objetivo (mejor mínima distancia alcanzada), eliminando penalizaciones por retroceder.

  * En suposición (acertada) de que este enfoque facilitaría la exploración se planteó un reto adicional generando obstáculos aleatorios en la forma de collectibles con reward negativo, mucho más grandes y abundantes que el resto. Cada obstáculo resta 2 puntos.

* **Motivación:** simplificar las observaciones a información **local y estructurada**, reducir exploits del shaping y guiar de forma natural la exploración de pasillos.
* **Resultado:** el agente aprendió a navegar de manera estable y eficiente, demostrando que una representación local apoyada en estructura topológica era mucho más eficaz que la global ingenua.

## Algoritmo de entrenamiento (PPO con Unity ML‑Agents)

### Configuración común

Se entrenó un **único agente** con **PPO** (ML‑Agents) en los tres experimentos, manteniendo una base homogénea para que las diferencias de rendimiento provinieran principalmente del **diseño de observaciones** y del **shaping**:

* **Algoritmo:** Proximal Policy Optimization (PPO) con *clipping* (ε=0.2) y **GAE** (λ=0.95).

  * *Motivo:* estabilidad y buen desempeño con espacios de observación medianos, además de ser el estándar recomendado en ML‑Agents para navegación.
* **Descuento y horizonte:** γ=0.99, **time\_horizon**=128.

  * *Motivo:* balance entre crédito a medio plazo (γ alto) y eficiencia de ventaja (horizonte suficiente para capturar transiciones pasillo↔habitación sin degradar la varianza).
* **Batching de PPO:** *buffer\_size* >> *batch\_size* con **num\_epoch**=3.

  * *Motivo:* cada actualización ve múltiples *minibatches* de una gran reserva de experiencias, reduciendo sobreajuste y oscilaciones.
* **Entropía (β)** con *schedule* lineal.

  * *Motivo:* fomentar exploración al inicio y reducirla gradualmente para consolidar la política.
* **Red actor‑crítico MLP**, **2 capas**, *hidden units* (128–256) según experimento.

  * *Motivo:* capacidad suficiente sin sobreparametrizar, evitando inestabilidades con datos ruidosos.
* **Normalización de entradas**: activada en los enfoques con observaciones locales/mixtas.

  * *Motivo:* estabilizar la escala cuando se mezclan señales heterogéneas (polares, raycast, intensidades).
* **Memoria (LSTM)**: activada cuando el agente usa observaciones locales.

  * *Motivo:* compensar **parcial observabilidad** (oclusiones/pasillos) acumulando contexto.
* **Señal de recompensa principal:** **extrínseca** (recogida de objetivos) en todos los experimentos.
* **Infraestructura de entrenamiento:** *time\_scale*≈20 (aceleración de simulación), *no\_graphics* desactivado salvo en el Exp. 1, *summary\_freq* entre 50k–60k pasos para registro periódico.

---

### Configuración específica por experimento

#### Experimento 1 — Global absoluto

* **YAML clave:**

  * `batch_size: 2048`, `buffer_size: 40960`, `num_epoch: 3`
  * `learning_rate: 3e-4`, `beta: 0.01`, `epsilon: 0.2`, `lambd: 0.95`
  * `network_settings`: `hidden_units: 128`, `num_layers: 2`, **sin memoria**, **sin normalización**
  * `reward_signals`: **extrinsic** (γ=0.99, strength=1.0) **+ curiosity** (*ICM/RND de ML‑Agents*, γ=0.99, strength=0.01, *lr* 3e‑4, *encoder* 256)
  * `max_steps: 28.8M`, `summary_freq: 60k`, `checkpoint_interval: 5.76M`
  * Motor: `time_scale: 20`, `no_graphics: true`.
* **Racional de diseño:**

  * Se añadió **curiosity** de baja intensidad (0.01) para mitigar la **escasez de recompensas** con observaciones globales.
  * Arquitectura **compacta (128)** y **sin normalización** para no introducir transformaciones adicionales al vector global.
* **Resultado observado (metodológico):** pese a la ayuda de curiosity, el agente no logró políticas útiles de salida de habitación; este punto justifica los cambios del Exp. 2 (shaping guiado) y del Exp. 3 (observaciones locales + estructura topológica).

#### Experimento 2 — Global + shaping

* **YAML clave:**

  * `batch_size: 1024`, `buffer_size: 20480`, `num_epoch: 3`
  * `learning_rate: 3e-4`, `beta: 0.02` (más exploración inicial que en Exp. 1), `epsilon: 0.2`, `lambd: 0.95`
  * `network_settings`: **normalize: true**, `hidden_units: 256`, `num_layers: 2`, **memoria activada** (`sequence_length: 64`, `memory_size: 128`)
  * `reward_signals`: **solo extrinsic** (γ=0.99, strength=1.0)
  * `max_steps: 92.16M`, `summary_freq: 50k`, `keep_checkpoints: 12`, `checkpoint_interval: 7.68M`
  * Motor: `time_scale: 20`, **gráficos activados** (útil para depuración visual).
* **Racional de diseño:**

  * Se retiró curiosity para que el **shaping** (bonus de **baldosas nuevas** + **potencial por distancia al objetivo**) fuese el motor principal de aprendizaje.
  * Se incrementó la **capacidad del MLP (256)** y se activó **memoria** para absorber la variabilidad temporal (caducidad de objetivos) y pequeñas **parcialidades de observación** debidas a embudos.
* **Resultado observado (metodológico):** el shaping impulsó algo de exploración y captación de objetivos, pero emergieron **exploits** por *spawn/despawn* y **miopías** cerca de paredes y movimientos circulares excesivos pisando baldosas. Esto motivó el rediseño del Exp. 3.

#### Experimento 3 — Local + grafo de señales

* **YAML clave:**

  * `batch_size: 1024`, `buffer_size: 20480`, `num_epoch: 3`
  * `learning_rate: 3e-4`, `beta: 0.02`, `epsilon: 0.2`, `lambd: 0.95`
  * `network_settings`: **normalize: true**, `hidden_units: 256`, `num_layers: 2`, **memoria activada** (`sequence_length: 64`, `memory_size: 128`)
  * `reward_signals`: **solo extrinsic** (γ=0.99, strength=1.0)
  * `max_steps: 24.0M`, `summary_freq: 50k`, `checkpoint_interval: 2.0M` (más frecuente para capturar la fase de aprendizaje acelerado)
  * Motor: `time_scale: 20`, **gráficos activados**.
* **Racional de diseño:**

  * Observaciones **locales normalizadas** (polares + raycast) y **estructura topológica** (grafo con señales por objetivo) exigen **normalización** y **memoria** para estabilizar y retener contexto de corto plazo (orientación/giros, señales cambiantes).
  * Sin *intrinsic reward*: el **shaping por récords** (definido en el entorno) reemplaza la presión temporal miopie del potencial tradicional y evita **penalizar retrocesos** necesarios para rodear obstáculos.
* **Resultado observado (metodológico):** aprendizaje rápido y estable; las *checkpoints* más frecuentes facilitaron seleccionar políticas antes de posibles sobre‑ajustes.

---

### Parámetros y su papel (lectura rápida)

* **`learning_rate=3e-4` con *schedule* lineal:** rango estándar para PPO en ML‑Agents; facilita *annealing* hacia el final para estabilizar.
* **`beta` (entropía) 0.01–0.02:** 0.02 aumenta la diversidad de acciones al principio; útil cuando el shaping (Exp. 2) o la estructura local (Exp. 3) requieren **exploración dirigida**.
* **`epsilon=0.2` (clipping PPO):** evita pasos de política demasiado grandes; valor canónico.
* **`buffer_size`/`batch_size` (20–40k / 1–2k):** suficiente variedad por actualización para evitar *myopic updates* sin saturar memoria.
* **`hidden_units` 128→256 y **memoria** ON en Exp. 2–3:** más capacidad y estado para gestionar **parcial observabilidad** y dinámicas por caducidad de objetivos.
* **Normalización ON (Exp. 2–3):** esencial cuando se combinan **polares**, **raycasts** y **intensidades** de señales en magnitudes no homogéneas.

---

### Procedimiento y reproducibilidad

* **Ejecución:** cada enfoque se entrenó con su `run_id` independiente (p. ej., `Competitive_1`, `CompetitiveByPhase`, `Graph`).
* **Registro:** resúmenes cada 50k–60k pasos y **checkpoints** (2–7.68 M pasos) para trazar curvas de aprendizaje y seleccionar la política final.
* **Motor:** `time_scale≈20` para acelerar simulación; resolución 84×84 (config por defecto de ML‑Agents para rendimiento).
* **Semillas:** valor por defecto del trainer (seed = −1) para aleatoriedad controlada por el entorno; al reportar resultados se recomienda incluir *n* ejecuciones por enfoque (si el tiempo lo permite).

---
