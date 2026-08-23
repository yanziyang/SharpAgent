==================================================
--------------------------------------------------
Pre-requisite
--------------------------------------------------
(1) Shadcn skill
px skills add shadcn/ui

(2) UI/UX Pro Max Agent Skill
https://github.com/nextlevelbuilder/ui-ux-pro-max-skill

Stop Letting Agents Guess UI: Installing and Using UI UX Pro Max the Right Way
https://blog.margrop.net/en/post/ui-ux-pro-max-agent-skill-guide/

npx uipro-cli@latest init --ai codex

==================================================
--------------------------------------------------
Feasibility Study
--------------------------------------------------
Prompt:

Evaluate feasibility to use Microsoft agent framework to build AI Agent that have similar capability as Pi Agent, Open Code etc.

--------------------------------------------------
Prompt:

Elaborate this: Microsoft’s framework now includes a coding-oriented harness with planning/todos, context compaction, file access, shell integration, approvals, sessions, and streaming

--------------------------------------------------
Prompt:

Based on research above, create a comprehensive feasibility report.

Tech stack:
- Frontend: React, Vite, strict TypeScript, React Router
- Design system: shadcn
- Styling: Tailwind CSS 4
- Backend: .NET 10, Microsoft Agent Framework, Entity Framework
- Authentication: No need
- LLM: Support OpenCode Go, DeepSeek, OpenRouter
- Database: SQL Lite

Requirement  for the report:
- Generate the report as standalone HTML report and save to 'doc' folder.
- Include professional-looking diagrams for better illustration.
- Save raw diagram in mermaid, drawio, SVG or any other suitable format, and save in 'diagrams' sub-folder, so that I can edit the diagram myself.
- In the diagram, use icons instead of blocks for server, Azure Service, etc. Save the icons as SVG format in 'icons' sub-folder.
- For mermaid diagram, save the original diagram and convert to images.
- Target of the report is management, so try not be too technical. Need be professional yet easy to understand, with illustration etc.

=======================================================
-------------------------------------------------------
Functional Spec
-------------------------------------------------------
Prompt:

I intend to use Microsoft agent framework to build AI Agent that have similar capability as Pi Agent, Open Code etc.

Tech stack:
- Frontend: React, Vite, strict TypeScript, React Router
- Design system: shadcn
- Styling: Tailwind CSS 4
- Backend: .NET 10, Microsoft Agent Framework, Entity Framework
- Authentication: No need
- LLM: Support OpenCode Go, DeepSeek, OpenRouter
- Database: SQL Lite

Have done feasibility study, feasibility report is in 'doc' folder.

Now create functional spec first, save in 'doc' folder. One in markdown format for coding agent, another one in html format for human to read.

==================================================
--------------------------------------------------
HTML Prototype
--------------------------------------------------
Prompt:

I intend to use Microsoft agent framework to build AI Agent that have similar capability as Pi Agent, Open Code etc.

Tech stack:
- Frontend: React, Vite, strict TypeScript, React Router
- Design system: shadcn
- Styling: Tailwind CSS 4
- Backend: .NET 10, Microsoft Agent Framework, Entity Framework
- Authentication: No need
- LLM: Support OpenCode Go, DeepSeek, OpenRouter
- Database: SQL Lite

Based on function spec 'doc\functional-spec.md', create HTML Prototype for team member and management to visualise the system.

Requirement for the html prototype:
- Build the prototype using I/UX Pro Max and Shadcn skills
- Prototype shall be comprehensive as much as possible.
- Save the html prototype in 'prototype(html)' folder.
- The prototype need clickable for the full process, from login to dashbord, statistics report.
- The prototype shall be as comprehensive as possible, cover most of essential use cases.
- The UI need responsive, support mobile device such as tablet etc.
- Use shadcn for UI/UX design.
- Provide four different themes. User can change themes from My Profile or Preference web page.

--------------------------------------------------
Prompt:

Evaluate prototype against functional spec, whether covered all features. Please update the prototype to fill the gap.

=======================================================
-------------------------------------------------------
design spec
-------------------------------------------------------
Prompt:

Next, create technical design spec, save in 'doc' folder. One in markdown format for coding agent, another one in html format for human to read.

=======================================================
-------------------------------------------------------
AGENTS.md
-------------------------------------------------------
Prompt:

The implementation will be done by other AI Coding Agent such as OpenCode + DeepSeek. Create AGENTS.md for other coding agents. Reference functional spec and design spec markdown files in 'doc' folder as progressive disclosure.

=======================================================
-------------------------------------------------------
Implementation Plan
-------------------------------------------------------
Prompt:

The implementation will be done by other AI Coding Agent such as OpenCode + DeepSeek. Create detailed Implementation Plan and save as 'doc\ImplementationPlan V0.1.md'. 

Reference Functional Spec, Design Spec and HTML prototype for what need to be implemented. 

Need unit test and Playwright E2E test coverage more than 90%.

Use OpenCode Go PLan API Key in 'LLM-Key.md' for testing. Do not send this file to GitHub. And only use the following models from OpenCode Go PLan:
- Ox Alpha Free
- Muse Spark 1.2 Contributor
- MiMo-V2.5




