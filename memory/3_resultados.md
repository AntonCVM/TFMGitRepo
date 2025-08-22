# Resultados

En este capítulo se presentan los resultados obtenidos a lo largo de los tres enfoques de entrenamiento implementados. Cada uno representa un aumento progresivo de complejidad en la representación del entorno y en las señales de recompensa, lo que permite evaluar las limitaciones y ventajas relativas de cada aproximación.

---

## Enfoque 1: Observaciones globales sin shaping

El agente recibía coordenadas globales normalizadas tanto de sí mismo como de los objetivos. Este exceso de información global, sin estructura, dificultó que el agente encontrara estrategias útiles y saliera de la habitación inicial. En general resultó en una **exploración muy deficiente**:

* Rara vez abandonaba la habitación inicial.
* Apenas conseguía recolectar objetos.
* El aprendizaje se caracterizó por una **pendiente casi nula** en la evolución del reward.

Reward vs steps. Cada step es una decisión del agente, por tanto un episodio consta de muchos steps.

![Evolución del reward promedio por episodio en escenario 1 y 2](../dataAnalysis/Environment_Cumulative%20Reward%201.png){.H}

---

## Enfoque 2: Observaciones globales + shaping basado en potencial

La introducción de un shaping de recompensa no produjo **ninguna mejora** en la recogida de colectables ni tampoco incentivó la exploración de habitaciones adicionales, en verdad fue **contraproducente**. De hecho, aparecieron limitaciones y comportamientos no deseados:

* El shaping no estaba bien adecuado al experimento. Este proporcionaba recompensas al reducir el potencial y las quitaba al aumentarlo dejando por defecto un saldo neto de 0. Tan solo produciría un saldo positivo en caso de que el agente terminara el episodio cerca de un recolectable, y dado que los episodios eran largos, este posible incentivo era desdeñable.
* **Exploits del potencial** debido a cambios abruptos en el spawn y despawn de objetivos.
* Agente que permanecía inmóvil junto a las paredes, recibiendo recompensas sin progresar en la tarea.
* Problemas de granularidad espacial:

  * Con **baldosas grandes**, no era capaz de detectar los pasillos.
  * Con **baldosas pequeñas**, se movía erráticamente en círculos.

* Al final estuvo calibrado con baldosas pequeñas. Los episodios no fueron lo suficientemente largos como para que al agente le diera tiempo a visitar consistentemente todas las baldosas de la habitación inicial, lo que le desincentivaba a buscar nuevas habitaciones.

### Gráficas de resultados

* **Reward vs steps**.

![Evolución del reward promedio por episodio en escenario 1 y 2](../dataAnalysis/Environment_Cumulative%20Reward%202.png){.H}

---

* **Comparativa con resultados previos**.A continuación se muestran gráficas de la cantidad de recolectables recogidos en ambos enfoques, de los cambios de habitación y de la cantidad de habitaciones exploradas:

![Evolución del nº de recolectables positivos promedio por episodio en escenario 1 y 2](../dataAnalysis/Evolucion%20del%20n%20de%20recolectables%20positivos%20promedio%20por%20episodio%20en%20escenario%201%20y%202.png){.H}

![Promedio de cambios de habitación por episodio en escenario 1 y 2](../dataAnalysis/Promedio%20de%20cambios%20de%20habitación%20por%20episodio%20en%20escenario%201%20y%202.png){.H}

![Promedio de habitaciones visitadas por episodio en escenario 1 y 2](../dataAnalysis/Promedio%20de%20habitaciones%20visitadas%20por%20episodio%20en%20escenario%201%20y%202.png){.H}

---

## Enfoque 3: Observaciones locales + grafo de señales

El tercer enfoque introdujo un **grafo de propagación de señales** desde los recolectables, junto con observaciones locales polares. Además, se aplicó shaping basado en récords de aproximación en lugar de potencial directo.

Los resultados fueron claramente superiores:

* El agente aprendió **rápidamente** a navegar habitaciones y pasillos.
* Las señales del grafo guiaron su atención de forma consistente.
* El shaping por récords **evitó los exploits** anteriores.
* Se observó un comportamiento **estable y eficiente**, con una tasa sostenida de progreso hacia los objetivos.
* También mostró la capacidad de **distinguir y esquivar obstáculos negativos** con su información local. Sin embargo, esta evitación no fue completamente fiable, lo que sugiere que la penalización aplicada a los obstáculos era demasiado baja para consolidar un rechazo perfecto.

Dado que en el mismo experimento se introdujo el grafo y las observaciones polares locales es imposible determinar el impacto individual de cada uno por separado.

### Gráficas de resultados

* **Reward vs steps**.

![Evolución del reward promedio por episodio en escenario 1 y 2](../dataAnalysis/Environment_Cumulative%20Reward%203.png){.H}

---

* **Comparativa con resultados previos**. A continuación se muestran gráficas de la cantidad de recolectables recogidos en ambos enfoques, de los cambios de habitación y de la cantidad de habitaciones exploradas:

![Evolución del nº de recolectables positivos promedio por episodio en escenario 1 y 2](../dataAnalysis/Evolucion%20del%20n%20de%20recolectables%20positivos%20promedio%20por%20episodio.png){.H}

![Promedio de cambios de habitación por episodio en escenario 1 y 2](../dataAnalysis/Promedio%20de%20cambios%20de%20habitación%20por%20episodio.png){.H}

![Promedio de habitaciones visitadas por episodio en escenario 1 y 2](../dataAnalysis/Promedio%20de%20habitaciones%20visitadas%20por%20episodio.png){.H}

---

* **Aciertos vs errores**. En la siguiente gráfica puede verse como los recolectables negativos se estancan en lugar de reducirse:

![Evolución del nº de recolectables positivos y negativos por episodio en el Escenario 3](../dataAnalysis/Evolucion%20del%20n%20de%20recolectables%20positivos%20y%20negativos%20por%20episodio%20en%20el%20Escenario%203.png){.H}

---

## Comparativa general

Aunque los valores absolutos de reward no son directamente comparables entre enfoques, las gráficas de recolectables permiten observar tendencias claras:

* **Enfoque 1**: estancamiento casi completo.
* **Enfoque 2**: empeoramiento por falta de adecuación del shaping.
* **Enfoque 3**: avance sostenido y eficiente, con un patrón de aprendizaje robusto.

---
