# Introducción

## Contexto y motivación

La navegación autónoma en entornos complejos constituye un reto central dentro del aprendizaje por refuerzo (RL) y la robótica móvil. A diferencia de tareas con recompensas densas y observaciones simplificadas, los entornos de **habitaciones conectadas por pasillos estrechos** presentan **recompensas escasas** y **cuellos de botella** que dificultan tanto la exploración como la generalización. Estos escenarios, a pesar de su simplicidad relativa, son análogos a problemas reales de logística o de robots de servicio en instalaciones industriales: un agente que debe **desplazarse por una zona fija de trabajo** para acudir a distintos objetivos o tareas.

En la industria, esta necesidad aparece en casos como la reposición en almacenes, el transporte de materiales o la limpieza en instalaciones, donde los robots deben desenvolverse en layouts fijos pero complejos. Los sistemas comerciales (ROS2 Navigation2, Nav2) recurren a arquitecturas modulares con planificación topológica y control local. Sin embargo, los enfoques basados en RL ofrecen la posibilidad de **aprender políticas adaptativas** que se ajusten a dinámicas cambiantes, obstáculos inesperados o reconfiguraciones del entorno.

---

## Estado del arte

Diversas líneas de investigación han intentado superar los retos de exploración y navegación:

* **Shaping de recompensas.** Ng et al. [@Ng1999PolicyInvariance] establecieron las bases del *potential-based reward shaping*, garantizando la invariancia de política al añadir funciones potenciales estacionarias. No obstante, se ha observado que en entornos no estacionarios (con spawn/despawn de objetivos) o con geometrías restrictivas, el shaping puede inducir **exploits** o **mínimos locales** (Khatib [@Khatib1986RealTime]).

* **Navegación topológica.** En lugar de depender de coordenadas globales, métodos recientes han mostrado la eficacia de representar el entorno como un **grafo de nodos y conexiones**, sobre el cual una política global selecciona metas intermedias mientras un controlador local ejecuta los movimientos [@Chaplot2020NeuralTopoSLAM; @Chen2019GraphNav]. Además, la memoria topológica en grafo permite persistir landmarks y relaciones espaciales a largo plazo [@Kwon2021VisualGraphMemory].

* **Exploración activa.** Extensiones como Active Neural SLAM [@Chaplot2020ActiveNeuralSLAM] integran percepción y planificación en un marco jerárquico, facilitando que el agente aprenda no solo a seguir objetivos, sino también a descubrir nuevas regiones del entorno de forma eficiente.

* **Tendencias recientes.** Benchmarks como MiniGrid MultiRoom [@MiniGridMultiRoom] confirman que entornos de habitaciones y pasillos son un marco adecuado para estudiar estos retos de manera controlada. Revisiones como la de Sun et al. [@Sun2024ObjectNavSurvey] en **Object Goal Navigation** clasifica los trabajos existentes sobre la Navegación por Objetivos (ObjectNav) en tres categorías principales:

    * **Métodos "end-to-end"**: Estos métodos mapean directamente las observaciones del entorno a las acciones del agente. El artículo subdivide esta categoría en dos enfoques principales:
        * **Representación Visual**: Se centra en extraer información útil de las observaciones para mejorar la comprensión del entorno por parte del agente.
        * **Aprendizaje de Políticas**: Aborda los problemas de generalización deficiente, recompensas escasas y la ineficiencia de las muestras en el aprendizaje.

    * **Métodos Modulares**: Estos métodos se componen de varios módulos, incluyendo uno de mapeo, uno de políticas y uno de planificación de rutas. El artículo también los divide en subcategorías :
        * **Mapa de cuadrícula sin predicción**
        * **Mapa de cuadrícula con predicción**
        * **Representación de mapa basada en gráficos**

    * **Métodos "Zero-shot"**: Utilizan el aprendizaje "zero-shot" para la navegación, lo que permite al agente encontrar objetos que no ha visto previamente durante el entrenamiento. Dentro de esta categoría, el artículo distingue entre:
        * **Configuración "Zero-shot"**
        * **Configuración de vocabulario abierto**

---

## Aportación de este trabajo

En este proyecto se estudia la navegación de un **agente único** en un entorno de varias habitaciones interconectadas por pasillos, con recolectables que aparecen y expiran de forma autónoma. El trabajo explora tres enfoques progresivos:

1. **Observaciones globales** (coordenadas globales normalizadas) con recompensas básicas.
2. **Observaciones globales + shaping**: bonus de exploración espacial y función potencial basada en la distancia al objetivo.
3. **Observaciones locales + grafo de señales**: raycast en y propagación de señales desde los recolectables a través de un grafo (coordenadas locales polares), con shaping por récords de aproximación. Capa extra de dificultad añadida con **obstáculos dinámicos**.

El objetivo es analizar las limitaciones de las aproximaciones ingenuas (1 y 2) y demostrar que un enfoque local con soporte topológico (3) permite superar dificultades de navegación y guiar la atención de manera más eficaz.

---

## Proyección a entornos multiagente

Aunque este trabajo se centra en el entrenamiento de un **agente único**, el diseño del entorno y de las señales de recompensa está concebido para ser **escalable al caso multiagente**. En escenarios con varios robots que comparten un mismo espacio con pasillos estrechos, aparecen de forma natural fenómenos como:

* **Conflictos en cuellos de botella**, donde los agentes deben ceder el paso o coordinarse para evitar bloqueos.
* **Asignación de tareas distribuidas**, en la que varios agentes deben decidir cómo repartirse los recolectables o zonas a explorar.
* **Gestión de información parcial**, ya que cada agente dispone únicamente de observaciones locales y puede necesitar compartir o inferir señales globales a través de comunicación explícita o implícita.

La formulación mediante **grafo de señales** y **shaping por récords** ofrece un marco flexible para este salto: los nodos del grafo pueden actuar como puntos de coordinación o encuentro, mientras que el shaping por récords evita castigos locales que podrían complicarse aún más en presencia de múltiples agentes.

De este modo, el trabajo no solo aporta un análisis sobre cómo superar los retos de navegación en entornos de habitaciones y pasillos para un agente único, sino que también sienta las bases para estudiar fenómenos de cooperación, competición y resolución de dilemas sociales en entornos multiagente, de interés tanto académico como industrial.

---

## Material disponible

El material completo de este Trabajo de Fin de Máster (TFM), incluyendo código, documentación y experimentos, está disponible públicamente en el siguiente repositorio de GitHub:

[https://github.com/AntonCVM/TFMGitRepo](https://github.com/AntonCVM/TFMGitRepo)

Para probar el entorno tanto manualmente como con el modelo entrenado según el escenario tercero ejecutar UnityEnvironment.exe de la carpeta build. Indicaciones:

* AWSD: Mover el agente manualmente.

* Q: Alterna la visualización del grafo de señales.

* E: Alterna la visualización de observaciones del agente en el grafo de señales.

* F: Cambia entre control manual y control por modelo de IA preentrenado.

* Tab: Cambia el ángulo de la cámara.

* º (BackQuote): Vista general del área.

* 1: Vista sobre el agente principal.

* 2, 3, 4: Vista sobre otros agentes (si están activos).

---
