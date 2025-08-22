# Referencias

> Estructura por referencia: (1) Tema que nos concierne. (2) Conexión con este TFM. (3) Lugar en la memoria.

---

## 1) Policy Invariance under Reward Transformations: Theory and Application to Reward Shaping (Ng, Harada, Russell, 1999)

**Enlace:** [https://people.eecs.berkeley.edu/\~pabbeel/cs287-fa09/readings/NgHaradaRussell-shaping-ICML1999.pdf](https://people.eecs.berkeley.edu/~pabbeel/cs287-fa09/readings/NgHaradaRussell-shaping-ICML1999.pdf)

**(1) Tema:** Formaliza el shaping potencial Φ(s) y demuestra que añadir ΔΦ = γΦ(s') − Φ(s) preserva la política óptima bajo supuestos (estacionariedad, MDP).
**(2) Conexión:** En el Experimento 2 usé un potencial basado en distancia al objetivo; la no estacionariedad por spawn/despawn y la geometría (paredes) lo hicieron exploitable. Justifica migrar a **shaping por récords** (premiar solo mejores mínimos históricos) en el Exp. 3.
**(3) Ubicación:** Trabajo relacionado (teoría de shaping) + Metodología (diseño de recompensas) + Discusión (por qué el potencial ingenuo falló).

**BibTeX**

```bibtex
@inproceedings{Ng1999PolicyInvariance,
  title={Policy Invariance Under Reward Transformations: Theory and Application to Reward Shaping},
  author={Ng, Andrew Y. and Harada, Daishi and Russell, Stuart J.},
  booktitle={Proceedings of the 16th International Conference on Machine Learning (ICML)},
  pages={278--287},
  year={1999},
  url={https://people.eecs.berkeley.edu/~pabbeel/cs287-fa09/readings/NgHaradaRussell-shaping-ICML1999.pdf}
}
```

---

## 2) Neural Topological SLAM for Visual Navigation (Chaplot, Salakhutdinov, A. Gupta, S. Gupta; CVPR 2020)

**Enlace:** [https://openaccess.thecvf.com/content\_CVPR\_2020/papers/Chaplot\_Neural\_Topological\_SLAM\_for\_Visual\_Navigation\_CVPR\_2020\_paper.pdf](https://openaccess.thecvf.com/content_CVPR_2020/papers/Chaplot_Neural_Topological_SLAM_for_Visual_Navigation_CVPR_2020_paper.pdf)

**(1) Tema:** Representación topológica (nodos/enlaces) para navegación por objetivos; política global sobre grafo + control local.
**(2) Conexión:** Mi **grafo de señales** actúa como guía topológica: selecciona subrutas/pasillos y centra la atención sin depender de coordenadas globales.
**(3) Ubicación:** Trabajo relacionado (enfoques topológicos) + Discusión (por qué A3 es más robusto en pasillos).

**BibTeX**

```bibtex
@inproceedings{Chaplot2020NeuralTopoSLAM,
  title={Neural Topological SLAM for Visual Navigation},
  author={Chaplot, Devendra Singh and Salakhutdinov, Ruslan and Gupta, Abhinav and Gupta, Saurabh},
  booktitle={Proceedings of the IEEE/CVF Conference on Computer Vision and Pattern Recognition (CVPR)},
  year={2020},
  url={https://openaccess.thecvf.com/content_CVPR_2020/papers/Chaplot_Neural_Topological_SLAM_for_Visual_Navigation_CVPR_2020_paper.pdf}
}
```

---

## 3) Learning to Explore using Active Neural SLAM (Chaplot, Gandhi, S. Gupta, A. Gupta, Salakhutdinov; ICLR 2020)

**Enlace:** [https://openreview.net/forum?id=HklXn1BKDH](https://openreview.net/forum?id=HklXn1BKDH)

**(1) Tema:** Arquitectura jerárquica: SLAM/planificación clásica combinada con políticas aprendidas (exploración activa).
**(2) Conexión:** Respaldar el enfoque **híbrido**: señales topológicas (alto nivel) + controlador local (bajo nivel). Encaja con una futura integración con planners convencionales.
**(3) Ubicación:** Trabajo relacionado.

**BibTeX**

```bibtex
@inproceedings{Chaplot2020ActiveNeuralSLAM,
  title={Learning to Explore using Active Neural SLAM},
  author={Chaplot, Devendra Singh and Gandhi, Dhiraj and Gupta, Saurabh and Gupta, Abhinav and Salakhutdinov, Ruslan},
  booktitle={International Conference on Learning Representations (ICLR)},
  year={2020},
  url={https://openreview.net/forum?id=HklXn1BKDH}
}
```

---

## 4) A Survey of Object Goal Navigation (Sun, Wu, Ji, Lai; IEEE TASE, 2024)

**Enlace:** [https://orca.cardiff.ac.uk/id/eprint/167432/1/ObjectGoalNavigationSurveyTASE.pdf](https://orca.cardiff.ac.uk/id/eprint/167432/1/ObjectGoalNavigationSurveyTASE.pdf)

**(1) Tema:** Revisión reciente de ObjectNav: tendencias, métricas (SR/SPL), y **métodos modulares** con mapeo, política y planificador; incluye "Graph‑based Map Representation".
**(2) Conexión:** Refuerza la elección del enfoque topológico para guiar la búsqueda de objetivos y sugiere un encaje natural de A3 en arquitecturas modulares.
**(3) Ubicación:** Trabajo relacionado (estado del arte 2024) + Introducción (motivación actualizada).

**BibTeX**

```bibtex
@article{Sun2024ObjectNavSurvey,
  title={A Survey of Object Goal Navigation},
  author={Sun, Jingwen and Wu, Jing and Ji, Ze and Lai, Yu-Kun},
  journal={IEEE Transactions on Automation Science and Engineering},
  year={2024},
  note={Early Access},
  url={https://orca.cardiff.ac.uk/id/eprint/167432/1/ObjectGoalNavigationSurveyTASE.pdf}
}
```

---

## 5) A Behavioral Approach to Visual Navigation with Graph Localization Networks (GraphNav; Chen et al., RSS 2019)

**Enlace:** [https://www.roboticsproceedings.org/rss15/p10.pdf](https://www.roboticsproceedings.org/rss15/p10.pdf)

**(1) Tema:** Navegación visual con **mapas topológicos** y **GNNs** para localización; descomposición en comportamientos.
**(2) Conexión:** Refuerza el uso de **representaciones gráficas** y control local; paralelo claro con el grafo de señales.
**(3) Ubicación:** Trabajo relacionado (topologías y GNNs) + Discusión (política global vs. controlador local).

**BibTeX**

```bibtex
@inproceedings{Chen2019GraphNav,
  title={A Behavioral Approach to Visual Navigation with Graph Localization Networks},
  author={Chen, Kevin and de Vicente, Juan Pablo and Sepulveda, Gabriel and Xia, Fei and Soto, Alvaro and Vazquez, Marynel and Savarese, Silvio},
  booktitle={Robotics: Science and Systems (RSS)},
  year={2019},
  url={https://www.roboticsproceedings.org/rss15/p10.pdf}
}
```

---

## 6) Real-time Obstacle Avoidance for Manipulators and Mobile Robots (Khatib, IJRR 1986)

**Enlace:** [https://khatib.stanford.edu/publications/pdfs/Khatib_1986_IJRR.pdf](https://khatib.stanford.edu/publications/pdfs/Khatib_1986_IJRR.pdf)

**(1) Tema:** **Campos potenciales** para navegación.
**(2) Conexión:** Inspiración para A2.
**(3) Ubicación:** Trabajo relacionado (fundamentos/limitaciones) + Discusión (análisis de fallos de A2).

**BibTeX**

```bibtex
@article{Khatib1986RealTime,
  title={Real-time obstacle avoidance for manipulators and mobile robots},
  author={Khatib, Oussama},
  journal={The International Journal of Robotics Research},
  volume={5},
  number={1},
  pages={90--98},
  year={1986},
  doi={10.1177/027836498600500106},
  url={https://khatib.stanford.edu/publications/pdfs/Khatib_1986_IJRR.pdf}
}
```

---

## 7) Visual Graph Memory with Unsupervised Representation for Visual Navigation (Kwon et al., ICCV 2021)

**Enlace:** [https://openaccess.thecvf.com/content/ICCV2021/papers/Kwon_Visual_Graph_Memory_With_Unsupervised_Representation_for_Visual_Navigation_ICCV_2021_paper.pdf](https://openaccess.thecvf.com/content/ICCV2021/papers/Kwon_Visual_Graph_Memory_With_Unsupervised_Representation_for_Visual_Navigation_ICCV_2021_paper.pdf)

**(1) Tema:** Memoria **estructurada en grafo** para navegación; landmarks e inferencia sobre relaciones espaciales.
**(2) Conexión:** Cercano a la idea de **señales que viajan por el grafo**: soporte bibliográfico para representaciones topológicas/memoria.
**(3) Ubicación:** Trabajo relacionado (memorias topológicas) + Futuro (aprender el grafo/las señales en vez de darlas).

**BibTeX**

```bibtex
@inproceedings{Kwon2021VisualGraphMemory,
  title={Visual Graph Memory with Unsupervised Representation for Visual Navigation},
  author={Kwon, Obin and Kim, Nuri and Choi, Yunho and Yoo, Hwiyeon and Park, Jeongho and Oh, Songhwai},
  booktitle={Proceedings of the IEEE/CVF International Conference on Computer Vision (ICCV)},
  year={2021},
  url={https://openaccess.thecvf.com/content/ICCV2021/papers/Kwon_Visual_Graph_Memory_With_Unsupervised_Representation_for_Visual_Navigation_ICCV_2021_paper.pdf}
}
```

---

## 8) MiniGrid – MultiRoom (Farama Foundation)

**Enlace:** [https://minigrid.farama.org/environments/minigrid/MultiRoomEnv/](https://minigrid.farama.org/environments/minigrid/MultiRoomEnv/)

**(1) Tema:** Benchmark de **habitaciones conectadas por pasillos/puertas** con recompensas escasas; evaluación de secuencias de decisiones.
**(2) Conexión:** Analógico conceptual de mi entorno: múltiples estancias y cuellos de botella; valida el diseño del layout. Se puede poner como inspiración.
**(3) Ubicación:** Trabajo relacionado (benchmarks afines) + Introducción (motivación del layout).

**BibTeX**

```bibtex
@misc{MiniGridMultiRoom,
  title={MiniGrid: MultiRoom Environment},
  howpublished={\url{https://minigrid.farama.org/environments/minigrid/MultiRoomEnv/}},
  note={Accessed Aug 22, 2025},
  year={2025}
}
```

---
