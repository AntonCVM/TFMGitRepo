# Conclusiones

Los resultados muestran que la calidad de la navegación del agente depende más de **cómo se estructura la información y el shaping** que de la cantidad bruta de observaciones disponibles.

El Enfoque 1 evidenció que observaciones globales ricas, sin jerarquía ni estructura, no inducen comportamientos útiles. El Enfoque 2 demostró que los *potential-based shapings* pueden ser frágiles frente a dinámicas de spawn/despawn y a geometrías discretizadas de forma irregular. Finalmente, el Enfoque 3 introdujo un **grafo de señales** y un shaping basado en récords, mostrando un aprendizaje mucho más estable y con comportamientos que emergen de forma más natural.

En conjunto, se confirma que:

* **La estructura topológica** guía la exploración de manera más fiable que la información absoluta.
* **El shaping por récords** evita artefactos y fomenta progresión consistente.
* **La calibración de las penalizaciones** es clave para inducir conductas robustas frente a obstáculos.

---

# Limitaciones

Aunque los resultados del tercer enfoque fueron prometedores, existen amenazas a su validez y limitaciones relevantes de cara a su aplicación práctica:

* El uso de un **grafo de señales requiere infraestructura adicional** introduce un coste y una complejidad logística que limita la aplicabilidad directa del método en entornos reales. En simulación, las coordenadas de nodos y agentes están disponibles de forma directa, pero en un entorno real sería necesario:

  * O bien disponer de un sistema preciso de localización constante de los agentes.
  * O bien materializar físicamente los nodos como balizas o routers distribuidos en el espacio.

* La comparación entre enfoques no es 100% aislada (polares+grafo introducidos juntos), lo que dificulta evaluar el impacto en aislado de cada componente.

---

# Futuro trabajo

De cara a avanzar sobre estas bases, se proponen varias líneas:

* **Reducir la dependencia del grafo**: sustituir las observaciones precisas de coordenadas por referencias más plausibles en un entorno real, como detecciones visuales o mediciones aproximadas de distancia.
* **Priorización de tareas**: usar la distancia estimada (mediante el grafo) para que el agente decida qué objetivos atender en función de su urgencia y lejanía.
* **Penalización por expiración**: añadir una penalización explícita por dejar expirar tareas, para forzar estrategias que no ignoren objetivos cercanos.
* **Escenarios procedurales**: generar mapas de forma automática y entrenar un agente auxiliar encargado de colocar nodos del grafo al inicio (o dinámicamente durante la exploración).
* **Curriculum learning con señales progresivas**: utilizar el grafo como andamiaje temporal en fases iniciales de entrenamiento, e ir retirando sus señales progresivamente para que el agente aprenda a navegar sin depender de infraestructura extra.
* **Memoria de grafo a partir de señales visuales**: Aprender una memoria de grafo a partir de señales visuales en lugar de dar el grafo explícito [@Kwon2021VisualGraphMemory].

---

# Referencias
