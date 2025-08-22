# Resultados

En este capítulo se presentan los resultados obtenidos a lo largo de los tres enfoques de entrenamiento implementados. Cada uno representa un aumento progresivo de complejidad en la representación del entorno y en las señales de recompensa, lo que permite evaluar las limitaciones y ventajas relativas de cada aproximación.

---

## Enfoque 1: Observaciones globales sin shaping

El agente recibía coordenadas globales normalizadas tanto de sí mismo como de los objetivos. Sin embargo, la ausencia de shaping y de estructuras que guiasen la atención provocó una **exploración muy deficiente**:

* Rara vez abandonaba la habitación inicial.
* Apenas conseguía recolectar objetos.
* El aprendizaje se caracterizó por una **pendiente casi nula** en la evolución del reward.

📈 *Figura 1. Evolución del reward durante el entrenamiento del Enfoque 1.*
*(placeholder para gráfica con pendiente baja, casi plana)*

---

## Enfoque 2: Observaciones globales + shaping basado en potencial

La introducción de un shaping de recompensa produjo una **mejora ligera** en la recogida de recolectables e incentivó algo de exploración adicional. No obstante, también aparecieron limitaciones y comportamientos no deseados:

* **Exploits del potencial** debido a cambios abruptos en el spawn y despawn de objetivos.
* Agente que permanecía inmóvil junto a las paredes, recibiendo recompensas sin progresar en la tarea.
* Problemas de granularidad espacial:

  * Con **baldosas grandes**, no era capaz de detectar los pasillos.
  * Con **baldosas pequeñas**, se movía erráticamente en círculos.

📈 *Figura 2. Evolución del reward durante el entrenamiento del Enfoque 2.*
*(placeholder para gráfica con pendiente ligera, mejora lineal muy baja)*

📊 *Figura 3. Recolectables recogidos en el Enfoque 2 a lo largo del entrenamiento.*
*(placeholder para futura gráfica)*

---

## Enfoque 3: Observaciones locales + grafo de señales

El tercer enfoque introdujo un **grafo de propagación de señales** desde los recolectables, junto con observaciones locales polares. Además, se aplicó shaping basado en récords de aproximación en lugar de potencial directo.

Los resultados fueron claramente superiores:

* El agente aprendió **rápidamente** a navegar habitaciones y pasillos.
* Las señales del grafo guiaron su atención de forma consistente.
* El shaping por récords **evitó los exploits** anteriores.
* Se observó un comportamiento **estable y eficiente**, con una tasa sostenida de progreso hacia los objetivos.
* También mostró la capacidad de **distinguir y esquivar obstáculos negativos** con su información local. Sin embargo, esta evitación no fue completamente fiable, lo que sugiere que la penalización aplicada a los obstáculos era demasiado baja para consolidar un rechazo perfecto.

📈 *Figura 4. Evolución del reward durante el entrenamiento del Enfoque 3.*
*(placeholder para gráfica con subida rápida inicial y plateau alto a mitad del entrenamiento)*

📊 *Figura 5. Recolectables recogidos en el Enfoque 3 a lo largo del entrenamiento.*
*(placeholder para futura gráfica)*

---

## Comparativa general

Aunque los valores absolutos de reward no son directamente comparables entre enfoques, las gráficas permiten observar tendencias claras:

* **Enfoque 1**: estancamiento casi completo.
* **Enfoque 2**: ligera mejora, pero con comportamientos espurios.
* **Enfoque 3**: avance sostenido y eficiente, con un patrón de aprendizaje robusto.
