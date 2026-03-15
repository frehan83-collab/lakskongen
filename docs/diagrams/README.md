# System Diagrams

Visual documentation for Lakskongen's architecture and systems.

All diagrams use [Mermaid](https://mermaid.js.org/) syntax and render on GitHub, VS Code (with Mermaid extension), and most Markdown viewers.

## Diagrams

| Diagram | Description |
|---------|-------------|
| [Architecture Overview](architecture-overview.md) | Layered architecture: Simulation ↔ Bridge ↔ Presentation |
| [Data Flow](data-flow.md) | Game loop: GameState → SimulationEngine → UI |
| [Biology Lifecycle](biology-lifecycle.md) | Salmon life cycle: Egg → Harvest + cohort model + genetics |
| [Disease Propagation](disease-propagation.md) | SIR epidemic model + LOD tiers + sea lice |
| [Market Pricing](market-pricing.md) | Ornstein-Uhlenbeck price model + seasonal curves |

## Viewing Mermaid Diagrams

- **GitHub**: Renders automatically in `.md` files
- **VS Code**: Install [Mermaid Preview](https://marketplace.visualstudio.com/items?itemName=bierner.markdown-mermaid) extension
- **CLI**: Use [mermaid-cli](https://github.com/mermaid-js/mermaid-cli) to export PNG/SVG
- **Online**: Paste into [mermaid.live](https://mermaid.live/)
