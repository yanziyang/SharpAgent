# SharpAgent diagram assets

All diagram source files are editable and live in this folder.

| Asset | Purpose |
|---|---|
| `target-architecture.mmd` | Mermaid source for the architecture flow. |
| `agent-task-lifecycle.mmd` | Mermaid sequence diagram for the controlled agent loop. |
| `delivery-roadmap.mmd` | Mermaid source for the indicative delivery roadmap. |
| `*-icon.svg` | Icon-rich management views; editable in Figma, draw.io, Inkscape, Illustrator, or any SVG editor. |
| `*-mermaid.svg` / `*-mermaid.png` | Rendered Mermaid exports. |
| `icons/*.svg` | Reusable SVG icons used by the management diagram style. |

## Re-render Mermaid exports

The report was rendered with Mermaid CLI and the local Chrome browser configured in `puppeteer-config.json`.

```powershell
npx --yes @mermaid-js/mermaid-cli -p "doc\diagrams\puppeteer-config.json" -i "doc\diagrams\target-architecture.mmd" -o "doc\diagrams\target-architecture-mermaid.svg"
npx --yes @mermaid-js/mermaid-cli -p "doc\diagrams\puppeteer-config.json" -i "doc\diagrams\target-architecture.mmd" -o "doc\diagrams\target-architecture-mermaid.png"
```

Repeat the two commands for the other `.mmd` files by changing the input and output file names.
